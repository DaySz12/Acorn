using System.Runtime.InteropServices;

namespace Acorn.App.Platform;

/// <summary>
/// Best-effort measures so a crash does not write process memory (which may hold decrypted data)
/// to disk. Release builds only; debug builds must stay debuggable.
/// </summary>
internal static partial class ProcessHardening
{
    private const int RlimitCore = 4;     // Same value on Linux and macOS.
    private const int PrSetDumpable = 4;  // Linux prctl option.

    // WebView2 reads these from the process environment. Either one would let another local
    // process attach a debugger to the web view and read what is on screen.
    private static readonly string[] WebViewDebugVariables =
    [
        "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
        "WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER",
    ];

    public static void Apply()
    {
#if !DEBUG
        foreach (var name in WebViewDebugVariables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // No core dumps.
            var limit = new RLimit { Current = 0, Maximum = 0 };
            _ = SetRLimit(RlimitCore, ref limit);
        }
        if (OperatingSystem.IsLinux())
        {
            // Also blocks ptrace attach by other non-root processes of the same user.
            _ = Prctl(PrSetDumpable, 0, 0, 0, 0);
        }
#endif
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RLimit
    {
        public ulong Current;
        public ulong Maximum;
    }

    [LibraryImport("libc", EntryPoint = "setrlimit", SetLastError = true)]
    private static partial int SetRLimit(int resource, ref RLimit limit);

    [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static partial int Prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);
}
