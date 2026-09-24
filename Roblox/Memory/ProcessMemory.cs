using JelloClient.Services;
using System.Runtime.InteropServices;

namespace JelloClient.Roblox.Memory;

/// A read-only view into another process's memory.
///
/// This is the foundation the analysis in the Lab section is built on. It reads; it never
/// writes. The handle is opened with query and read rights only - no VM_WRITE, no
/// VM_OPERATION - so nothing here can change the target even by mistake. Writing memory is
/// a separate, deliberately harder step that lives elsewhere and stays off by default.
///
/// Reads go through ntdll's NtReadVirtualMemory, which is the same call the Win32
/// ReadProcessMemory makes one layer down; using it directly avoids a wrapper that some
/// environments hook.
internal sealed class ProcessMemory : IDisposable
{
    private const int ProcessQueryInformation = 0x0400;
    private const int ProcessVmRead = 0x0010;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtReadVirtualMemory(
        IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetModuleInformation(
        IntPtr process, IntPtr module, out ModuleInfo info, int size);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumProcessModulesEx(
        IntPtr process, [Out] IntPtr[] modules, int size, out int needed, int filter);

    [StructLayout(LayoutKind.Sequential)]
    private struct ModuleInfo
    {
        public IntPtr BaseOfDll;
        public int SizeOfImage;
        public IntPtr EntryPoint;
    }

    private IntPtr _handle;

    public int ProcessId { get; }

    /// The base address and size of the client's own module, for anything that works in
    /// terms of a relative offset into the executable.
    public IntPtr ModuleBase { get; private set; }

    public int ModuleSize { get; private set; }

    public bool IsOpen => _handle != IntPtr.Zero;

    private ProcessMemory(int processId, IntPtr handle)
    {
        ProcessId = processId;
        _handle = handle;
    }

    /// Opens a process for reading, or returns null when it cannot be opened - which is an
    /// ordinary outcome (the process closed, or it is more privileged than Jello) and not
    /// an error to throw over.
    public static ProcessMemory? Open(int processId)
    {
        IntPtr handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, processId);

        if (handle == IntPtr.Zero)
        {
            Log.Write("ProcessMemory::Open", $"Could not open process {processId} ({Marshal.GetLastWin32Error()})");
            return null;
        }

        var memory = new ProcessMemory(processId, handle);
        memory.ResolveMainModule();

        return memory;
    }

    private void ResolveMainModule()
    {
        var modules = new IntPtr[1024];

        // 0x03 is LIST_MODULES_ALL; the first entry is the process's own image.
        if (!EnumProcessModulesEx(_handle, modules, modules.Length * IntPtr.Size, out int needed, 0x03))
        {
            return;
        }

        if (GetModuleInformation(_handle, modules[0], out ModuleInfo info, Marshal.SizeOf<ModuleInfo>()))
        {
            ModuleBase = info.BaseOfDll;
            ModuleSize = info.SizeOfImage;
        }
    }

    public byte[] Read(IntPtr address, int size)
    {
        if (_handle == IntPtr.Zero || address == IntPtr.Zero || size <= 0)
        {
            return Array.Empty<byte>();
        }

        var buffer = new byte[size];

        if (NtReadVirtualMemory(_handle, address, buffer, size, out IntPtr read) != 0)
        {
            return Array.Empty<byte>();
        }

        int got = read.ToInt32();

        return got == size ? buffer : buffer[..Math.Max(0, got)];
    }

    public long ReadInt64(IntPtr address)
    {
        byte[] raw = Read(address, 8);
        return raw.Length == 8 ? BitConverter.ToInt64(raw) : 0;
    }

    public IntPtr ReadPointer(IntPtr address) => new(ReadInt64(address));

    public int ReadInt32(IntPtr address)
    {
        byte[] raw = Read(address, 4);
        return raw.Length == 4 ? BitConverter.ToInt32(raw) : 0;
    }

    public float ReadFloat(IntPtr address)
    {
        byte[] raw = Read(address, 4);
        return raw.Length == 4 ? BitConverter.ToSingle(raw) : 0f;
    }

    /// Reads a std::string: a small-buffer optimisation holds up to fifteen characters
    /// inline, longer ones behind a pointer, with the length always at +16.
    public string ReadStdString(IntPtr address)
    {
        byte[] header = Read(address, 24);

        if (header.Length < 24)
        {
            return string.Empty;
        }

        long length = BitConverter.ToInt64(header, 16);

        if (length <= 0 || length > 4096)
        {
            return string.Empty;
        }

        byte[] data = length < 16
            ? header[..(int)length]
            : Read(new IntPtr(BitConverter.ToInt64(header, 0)), (int)length);

        return System.Text.Encoding.UTF8.GetString(data);
    }

    public IntPtr Offset(IntPtr address, int offset) => new(address.ToInt64() + offset);

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
