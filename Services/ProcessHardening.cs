using System.Runtime.InteropServices;
using System.Threading;

namespace JelloClient.Services;

/// User-mode process hardening: makes this process meaningfully harder to read, inject into,
/// or debug. It is defense-in-depth, not a guarantee - an attacker running as admin with
/// SeDebugPrivilege (or from the kernel) can still bypass user-mode protection; the point is
/// to stop everything short of that (same-user malware, cheat engines, casual reverse
/// engineering) and to react when tampering is detected.
///
///   - Mitigation policies  : block legacy DLL-injection / global hooks, and DLLs loaded from
///                            a remote path or at a low integrity level.
///   - Process DACL         : deny VM read/write/operation, remote-thread creation and
///                            handle-duplication to other processes, so a normal process
///                            can't ReadProcessMemory or inject us. Terminate + query stay
///                            allowed, so Task Manager still shows and can kill it.
///   - Anti-debug watchdog  : detect a debugger and react (invoke the tamper handler, exit).
internal static class ProcessHardening
{
    private static Action? _onTamper;

    public static void Apply(Action? onTamper = null)
    {
        _onTamper = onTamper;
        Safe(SetMitigations);
        Safe(LockProcessDacl);
        Safe(StartAntiDebugWatchdog);
        Log.Write("Hardening", "Process hardening applied");
    }

    private static void Safe(Action action)
    {
        try { action(); } catch (Exception ex) { Log.Write("Hardening", ex.Message); }
    }

    // ── Mitigation policies ──────────────────────────────────────────────────────────────────
    private const int ProcessExtensionPointDisablePolicy = 6;
    private const int ProcessImageLoadPolicy = 10;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessMitigationPolicy(int policy, ref uint buffer, IntPtr length);

    private static void SetMitigations()
    {
        // Block legacy DLL injection: global Windows hooks, AppInit_DLLs, IME injection.
        uint extensionPoints = 1; // DisableExtensionPoints
        SetProcessMitigationPolicy(ProcessExtensionPointDisablePolicy, ref extensionPoints, (IntPtr)4);

        // Refuse DLLs loaded from a remote/UNC path or carrying a low integrity label.
        uint imageLoad = (1u << 1) | (1u << 2); // NoRemoteImages | NoLowMandatoryLabelImages
        SetProcessMitigationPolicy(ProcessImageLoadPolicy, ref imageLoad, (IntPtr)4);
    }

    // ── Process DACL ─────────────────────────────────────────────────────────────────────────
    private const int DACL_SECURITY_INFORMATION = 4;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string sddl, uint revision, out IntPtr securityDescriptor, out uint size);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetKernelObjectSecurity(IntPtr handle, int securityInformation, IntPtr securityDescriptor);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    private static void LockProcessDacl()
    {
        // Deny to Everyone (WD): CREATE_THREAD 0x2 | VM_OPERATION 0x8 | VM_READ 0x10 | VM_WRITE 0x20
        //   | DUP_HANDLE 0x40 | CREATE_PROCESS 0x80 | SET_INFORMATION 0x200 | SUSPEND_RESUME 0x800 = 0xAFA
        // Allow to Everyone: TERMINATE 0x1 | QUERY_INFORMATION 0x400 | QUERY_LIMITED_INFORMATION 0x1000
        //   | READ_CONTROL 0x20000 | SYNCHRONIZE 0x100000 = 0x121401  (Task Manager still works)
        const string sddl = "D:(D;;0xAFA;;;WD)(A;;0x121401;;;WD)";

        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out IntPtr sd, out _))
        {
            return;
        }

        try
        {
            SetKernelObjectSecurity(GetCurrentProcess(), DACL_SECURITY_INFORMATION, sd);
        }
        finally
        {
            LocalFree(sd);
        }
    }

    // ── Anti-debug watchdog ──────────────────────────────────────────────────────────────────
    [DllImport("kernel32.dll")] private static extern bool IsDebuggerPresent();
    [DllImport("kernel32.dll")] private static extern bool CheckRemoteDebuggerPresent(IntPtr handle, ref bool present);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr handle, int infoClass, ref IntPtr info, int len, IntPtr returnLen);
    [DllImport("ntdll.dll")] private static extern int NtSetInformationThread(IntPtr handle, int infoClass, IntPtr info, int len);

    private const int ProcessDebugPort = 7;
    private const int ProcessDebugObjectHandle = 0x1e;
    private const int ThreadHideFromDebugger = 0x11;

    private static void StartAntiDebugWatchdog()
    {
        var thread = new Thread(WatchdogLoop) { IsBackground = true, Name = "jhw" };
        thread.Start();
    }

    private static void WatchdogLoop()
    {
        // Detach this thread from any attached debugger.
        try { NtSetInformationThread(GetCurrentThread(), ThreadHideFromDebugger, IntPtr.Zero, 0); }
        catch (Exception) { /* older/locked-down systems */ }

        int hits = 0;

        while (true)
        {
            // Require two consecutive detections so a one-off blip never nukes a clean run.
            hits = DebuggerDetected() ? hits + 1 : 0;

            if (hits >= 2)
            {
                Log.Write("Hardening", "Debugger/tamper detected - standing down");
                try { _onTamper?.Invoke(); } catch (Exception) { /* best effort */ }
                Environment.Exit(0);
            }

            Thread.Sleep(1500);
        }
    }

    private static bool DebuggerDetected()
    {
        try
        {
            if (IsDebuggerPresent())
            {
                return true;
            }

            bool remote = false;
            if (CheckRemoteDebuggerPresent(GetCurrentProcess(), ref remote) && remote)
            {
                return true;
            }

            IntPtr port = IntPtr.Zero;
            if (NtQueryInformationProcess(GetCurrentProcess(), ProcessDebugPort, ref port, IntPtr.Size, IntPtr.Zero) == 0 && port != IntPtr.Zero)
            {
                return true;
            }

            IntPtr debugObject = IntPtr.Zero;
            if (NtQueryInformationProcess(GetCurrentProcess(), ProcessDebugObjectHandle, ref debugObject, IntPtr.Size, IntPtr.Zero) == 0 && debugObject != IntPtr.Zero)
            {
                return true;
            }
        }
        catch (Exception) { /* a probe being unavailable is not, by itself, tamper */ }

        return false;
    }
}
