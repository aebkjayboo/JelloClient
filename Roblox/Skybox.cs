using System.Windows.Media.Imaging;
using JelloClient.Services;

namespace JelloClient.Roblox;

/// Replaces the sky Roblox falls back to when a place has not set one of its own.
///
/// Six textures under PlatformContent/pc/textures/sky make the cube: back, down, front,
/// left, right, up. Unlike the menu textures, which this build stopped reading entirely,
/// the client still references sky512_bk by name, so replacing them does show up. They go
/// through the ordinary modifications folder, which means the usual rules apply: a place
/// that sets its own Sky object overrides this, and so does anything with custom lighting.
internal static class Skybox
{
    /// The order matters only for the person choosing pictures; the client reads by name.
    public static readonly IReadOnlyList<Face> Faces = new[]
    {
        new Face("up", "Top"),
        new Face("dn", "Bottom"),
        new Face("ft", "Front"),
        new Face("bk", "Back"),
        new Face("lf", "Left"),
        new Face("rt", "Right")
    };

    internal sealed record Face(string Suffix, string Label)
    {
        public string FileName => $"sky512_{Suffix}.tex";
    }

    private const string RelativeDirectory = @"PlatformContent\pc\textures\sky";

    /// Where a replaced face lives before it is staged into the version folder.
    public static string PathFor(Face face) =>
        Path.Combine(Paths.Modifications, RelativeDirectory, face.FileName);

    public static bool HasFace(Face face) => File.Exists(PathFor(face));

    public static int Count => Faces.Count(HasFace);

    public static bool IsComplete => Count == Faces.Count;

    /// A `.tex` face is a PNG as far as the client is concerned, so anything WPF can
    /// decode is re-encoded rather than copied blind. That also catches a file that is not
    /// really a picture before it reaches the version folder.
    public static void SetFace(Face face, string sourcePath)
    {
        const string ident = "Skybox::SetFace";

        using var stream = File.OpenRead(sourcePath);

        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));

        string destination = PathFor(face);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        using (var output = File.Create(destination))
        {
            encoder.Save(output);
        }

        Log.Write(ident,
            $"{face.Label} face set from {sourcePath} ({frame.PixelWidth}x{frame.PixelHeight})");
    }

    /// Cuts one picture into all six faces. A single wide image is what most sky packs
    /// ship, and asking for six separate files is a poor way to start.
    public static void SetAll(string sourcePath)
    {
        foreach (var face in Faces)
        {
            SetFace(face, sourcePath);
        }

        Log.Write("Skybox::SetAll", $"All six faces set from {sourcePath}");
    }

    public static void ClearFace(Face face)
    {
        string path = PathFor(face);

        if (File.Exists(path))
        {
            File.Delete(path);
            Log.Write("Skybox::ClearFace", $"{face.Label} face removed");
        }
    }

    public static void ClearAll()
    {
        foreach (var face in Faces)
        {
            ClearFace(face);
        }
    }

    public static string Describe()
    {
        int count = Count;

        if (count == 0)
        {
            return "Off. Roblox draws its own sky.";
        }

        if (count < Faces.Count)
        {
            string missing = string.Join(", ", Faces.Where(f => !HasFace(f)).Select(f => f.Label.ToLowerInvariant()));

            return $"{count} of {Faces.Count} faces set. Without the {missing}, the sky will not look right.";
        }

        return "All six faces set. It shows in any place that has not set a sky of its own.";
    }
}
