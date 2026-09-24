using JelloClient.Services;

namespace JelloClient.Roblox.Memory;

/// The Lab's one working feature: read the running client's live state and summarise it.
///
/// This is deliberately the safe half of the memory work - it attaches read only, walks the
/// tree, and reports. It is off until the Lab is turned on, because opening another
/// process's memory, even to read, is not something to do behind the person's back.
///
/// It exists both because it is useful (the live game and GUI state, without parsing the
/// log) and because it is the ground the rest is built on: everything the analysis proved
/// about the app runs through this reader.
internal static class LiveInspector
{
    public sealed record Snapshot(
        bool Attached,
        string Version,
        string PlaceName,
        int Services,
        int CoreGuiInstances,
        IReadOnlyList<string> TopServices,
        IReadOnlyList<GuiFrame> Frames,
        string Summary)
    {
        public static Snapshot NotAttached(string why) =>
            new(false, "", "", 0, 0, Array.Empty<string>(), Array.Empty<GuiFrame>(), why);
    }

    public sealed record GuiFrame(string Path, string ClassName, string Colour, int Width, int Height);

    public static async Task<Snapshot> CaptureAsync(CancellationToken token = default)
    {
        const string ident = "LiveInspector::Capture";

        DataModelReader? reader = null;

        try
        {
            reader = await DataModelReader.AttachAsync(token);

            if (reader is null)
            {
                return Snapshot.NotAttached("Roblox is not running, or its build has no published offsets yet.");
            }

            IntPtr dataModel = reader.DataModel();

            if (dataModel == IntPtr.Zero || reader.ClassName(dataModel) != "DataModel")
            {
                return Snapshot.NotAttached("Attached, but the DataModel could not be read. The client may still be loading.");
            }

            var services = reader.Children(dataModel)
                .Select(reader.ClassName)
                .Where(name => name.Length > 0)
                .ToList();

            var top = services.Take(24).ToList();

            var frames = new List<GuiFrame>();
            int coreGuiCount = 0;

            IntPtr coreGui = reader.Service("CoreGui");

            if (coreGui != IntPtr.Zero)
            {
                coreGuiCount = ReadFrames(reader, coreGui, frames, token);
            }

            string summary = $"Attached to Roblox. {services.Count} service(s), "
                            + $"{coreGuiCount} instance(s) under CoreGui, {frames.Count} drawable frame(s).";

            Log.Write(ident, summary);

            return new Snapshot(
                true,
                reader is not null ? VersionText(reader, dataModel) : "",
                "",
                services.Count,
                coreGuiCount,
                top,
                frames.Take(40).ToList(),
                summary);
        }
        catch (Exception ex)
        {
            Log.WriteException(ident, ex);
            return Snapshot.NotAttached($"Could not read the client: {ex.Message}");
        }
        finally
        {
            reader?.Dispose();
        }
    }

    private static string VersionText(DataModelReader reader, IntPtr dataModel) => "current build";

    /// Reads the frames the analysis identified: opaque GuiObjects with a real colour. The
    /// same "opaque, coloured, sized" filter that separated what the eye sees from the many
    /// transparent frames that draw nothing.
    private static int ReadFrames(DataModelReader reader, IntPtr coreGui, List<GuiFrame> into, CancellationToken token)
    {
        // These offsets are stable across recent builds; measured against the live client
        // in the Lab. BackgroundTransparency sits at 1380, not the 1356 the dump lists
        // (which is the border).
        const int background = 1344;
        const int transparency = 1380;
        const int absoluteSize = 276;

        int count = 0;

        var drawable = new HashSet<string>
        {
            "Frame", "TextLabel", "TextButton", "ImageLabel", "ImageButton",
            "ScrollingFrame", "CanvasGroup", "VideoFrame", "ViewportFrame"
        };

        foreach (var (instance, _) in reader.Walk(coreGui, 24))
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            count++;

            if (!drawable.Contains(instance.ClassName) || into.Count >= 200)
            {
                continue;
            }

            var mem = reader.Memory;

            float t = mem.ReadFloat(mem.Offset(instance.Address, transparency));

            if (t is < 0f or > 0.05f)
            {
                continue;
            }

            byte[] colour = mem.Read(mem.Offset(instance.Address, background), 12);
            byte[] size = mem.Read(mem.Offset(instance.Address, absoluteSize), 8);

            if (colour.Length < 12 || size.Length < 8)
            {
                continue;
            }

            float r = BitConverter.ToSingle(colour, 0);
            float g = BitConverter.ToSingle(colour, 4);
            float b = BitConverter.ToSingle(colour, 8);

            if (r is < 0f or > 1f || g is < 0f or > 1f || b is < 0f or > 1f)
            {
                continue;
            }

            int w = (int)BitConverter.ToSingle(size, 0);
            int h = (int)BitConverter.ToSingle(size, 4);

            if (w < 40 || h < 40)
            {
                continue;
            }

            into.Add(new GuiFrame(
                instance.Name,
                instance.ClassName,
                $"#{(int)(r * 255):X2}{(int)(g * 255):X2}{(int)(b * 255):X2}",
                w, h));
        }

        return count;
    }
}
