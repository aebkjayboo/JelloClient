using System.Runtime.InteropServices;
using JelloClient.Services;

namespace JelloClient.Roblox.Memory;

/// Recolours the Roblox app's GUI by writing to it directly - no script, no injection.
///
/// This is the thing that turned out to work after a long detour. Writing a frame's
/// BackgroundColor3 alone does nothing: the renderer keeps a retained copy and only
/// repaints when a change is flagged. The missing piece is a single dirty byte next to the
/// colour (measured at +1400, one past LayoutOrder) that the engine's own setter flips.
/// Write the colour and raise that byte through a clean 0->1 edge, and the frame repaints -
/// the whole home screen recoloured this way, with the client staying stable across a great
/// many writes.
///
/// It reaches the app shell that no other route could: not a layer painted over the window,
/// not a texture swap, not a flag, and not memory writes that never rendered. It is off by
/// default and lives in the Lab, because it writes to another process's memory - harmless in
/// testing, but not something to enable behind the person's back.
///
/// It only touches opaque Frame colours. Text, images and thumbnails are left alone because
/// they are not what it writes.
internal sealed class GuiTint : IDisposable
{
    private const int ProcessAccess = 0x0400 | 0x0010 | 0x0008; // query | read | operation
    private const int ProcessVmWrite = 0x0020;

    // GuiObject field offsets, stable across recent builds and measured against the live
    // client. BackgroundColor3 is where the dump says; the dirty byte and the real
    // transparency were found by watching what the engine changes on a legitimate recolour.
    private const int Background = 1344;
    private const int Dirty = 1400;
    private const int Transparency = 1380;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtWriteVirtualMemory(
        IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr written);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left, Top, Right, Bottom;
    }

    private readonly DataModelReader _reader;
    private IntPtr _writeHandle;

    /// What each frame held before Jello touched it, keyed by address, so the tint can be
    /// taken back off exactly rather than guessed at.
    private readonly Dictionary<long, byte[]> _originals = new();

    private GuiTint(DataModelReader reader, IntPtr writeHandle)
    {
        _reader = reader;
        _writeHandle = writeHandle;
    }

    public static async Task<GuiTint?> AttachAsync(CancellationToken token = default)
    {
        var reader = await DataModelReader.AttachAsync(token);

        if (reader is null)
        {
            return null;
        }

        IntPtr handle = OpenProcess(ProcessAccess | ProcessVmWrite, false, reader.Memory.ProcessId);

        if (handle == IntPtr.Zero)
        {
            Log.Write("GuiTint::Attach", $"Could not open the client for writing ({Marshal.GetLastWin32Error()})");
            reader.Dispose();
            return null;
        }

        return new GuiTint(reader, handle);
    }

    /// The result of one apply, for the interface to report.
    public sealed record Result(bool Ok, int Recoloured, string Summary);

    /// Blends every opaque frame under CoreGui towards a tint and repaints it. Strength 1
    /// is the flat colour; lower values keep some of the original so the shape stays
    /// readable.
    public Result Apply(byte tintR, byte tintG, byte tintB, double strength)
    {
        const string ident = "GuiTint::Apply";

        IntPtr coreGui = _reader.Service("CoreGui");

        if (coreGui == IntPtr.Zero)
        {
            return new Result(false, 0, "CoreGui could not be read - the client may still be loading.");
        }

        strength = Math.Clamp(strength, 0, 1);

        int recoloured = 0;
        int painted = 0;

        foreach (var (instance, _) in _reader.Walk(coreGui, 24))
        {
            if (instance.ClassName is not ("Frame" or "ScrollingFrame" or "CanvasGroup"))
            {
                continue;
            }

            IntPtr address = instance.Address;

            float transparency = _reader.Memory.ReadFloat(_reader.Memory.Offset(address, Transparency));

            // Transparent frames draw nothing; recolouring them makes hidden things appear.
            if (transparency is < 0f or > 0.05f)
            {
                continue;
            }

            byte[] current = _reader.Memory.Read(_reader.Memory.Offset(address, Background), 12);

            if (current.Length < 12)
            {
                continue;
            }

            // Keep the very first value seen for each frame, so a second apply still knows
            // the true original rather than a previous tint.
            _originals.TryAdd(address.ToInt64(), current);

            float r = BitConverter.ToSingle(current, 0);
            float g = BitConverter.ToSingle(current, 4);
            float b = BitConverter.ToSingle(current, 8);

            if (r is < 0f or > 1f || g is < 0f or > 1f || b is < 0f or > 1f)
            {
                continue;
            }

            byte[] blended = Blend(r, g, b, tintR / 255f, tintG / 255f, tintB / 255f, (float)strength);

            // Already the right colour (from an earlier tick): leave it, so a settled tint
            // is not re-written and re-flagged every pass.
            if (current.AsSpan(0, 12).SequenceEqual(blended))
            {
                recoloured++;
                continue;
            }

            if (Paint(address, blended))
            {
                recoloured++;
                painted++;
            }
        }

        // The colours are now in memory, but the engine only repaints changed frames on
        // its own render pass, and not reliably for all of them. A one-pixel resize forces
        // it to lay the whole surface out again, which reads every colour we wrote. Done
        // only when something actually changed, so a settled tint does not nudge forever.
        if (painted > 0)
        {
            ForceRelayout();
        }

        Log.Write(ident, $"Recoloured {recoloured} frame(s), {painted} newly painted, at {strength * 100:0}% strength");

        return recoloured == 0
            ? new Result(false, 0, "No opaque frames were found to recolour.")
            : new Result(true, recoloured, $"Recoloured {recoloured} frame(s).");
    }

    /// Puts every frame back to the colour it had before the first apply.
    public int Restore()
    {
        int restored = 0;

        foreach (var (addressValue, original) in _originals)
        {
            if (Paint(new IntPtr(addressValue), original))
            {
                restored++;
            }
        }

        _originals.Clear();

        // Same as apply: the colours are back in memory, so force the relayout that makes
        // the app read them.
        if (restored > 0)
        {
            ForceRelayout();
        }

        Log.Write("GuiTint::Restore", $"Put back {restored} frame(s)");

        return restored;
    }

    /// Writes a colour and raises the dirty byte the engine's own setter flips. Matched to
    /// what was proven by hand: write the colour, then set the byte to 1. The engine sees
    /// the change and repaints, then clears the byte itself.
    private bool Paint(IntPtr address, byte[] colour)
    {
        bool wrote = WriteBytes(_reader.Memory.Offset(address, Background), colour);

        WriteBytes(_reader.Memory.Offset(address, Dirty), new byte[] { 1 });

        return wrote;
    }

    private IntPtr _window = IntPtr.Zero;

    /// Nudges the Roblox window by a pixel and back, which forces the app to lay itself out
    /// again and repaint from the colours now in memory. Cheap and barely visible; it is
    /// the same relayout a real resize causes.
    private void ForceRelayout()
    {
        try
        {
            if (_window == IntPtr.Zero)
            {
                _window = System.Diagnostics.Process.GetProcessById(_reader.Memory.ProcessId).MainWindowHandle;
            }

            if (_window == IntPtr.Zero || !GetWindowRect(_window, out WindowRect rect))
            {
                return;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            const uint noMoveNoZ = 0x0002 | 0x0004 | 0x0010; // NOMOVE | NOZORDER | NOACTIVATE

            SetWindowPos(_window, IntPtr.Zero, 0, 0, width, height - 1, noMoveNoZ);
            SetWindowPos(_window, IntPtr.Zero, 0, 0, width, height, noMoveNoZ);
        }
        catch (Exception ex)
        {
            Log.Write("GuiTint::ForceRelayout", ex.Message);
        }
    }

    private bool WriteBytes(IntPtr address, byte[] data)
    {
        if (_writeHandle == IntPtr.Zero)
        {
            return false;
        }

        return NtWriteVirtualMemory(_writeHandle, address, data, data.Length, out _) == 0;
    }

    private static byte[] Blend(float r, float g, float b, float tr, float tg, float tb, float strength)
    {
        var result = new byte[12];

        BitConverter.GetBytes(r + (tr - r) * strength).CopyTo(result, 0);
        BitConverter.GetBytes(g + (tg - g) * strength).CopyTo(result, 4);
        BitConverter.GetBytes(b + (tb - b) * strength).CopyTo(result, 8);

        return result;
    }

    public void Dispose()
    {
        if (_writeHandle != IntPtr.Zero)
        {
            CloseHandle(_writeHandle);
            _writeHandle = IntPtr.Zero;
        }

        _reader.Dispose();
    }
}
