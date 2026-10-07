using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Acorn.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Acorn.App.Platform.Windows;

/// <summary>
/// SetWindowDisplayAffinity on the main window (SPEC R7). WDA_EXCLUDEFROMCAPTURE needs Windows 10 2004+;
/// older versions fall back to WDA_MONITOR (window shows black in captures).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsScreenProtection : IScreenProtection
{
    private const uint WdaNone = 0x00;
    private const uint WdaMonitor = 0x01;
    private const uint WdaExcludeFromCapture = 0x11;

    private readonly MainWindowHolder _window;
    private readonly ILogger<WindowsScreenProtection> _logger;
    private volatile bool _enabled = true;

    public WindowsScreenProtection(MainWindowHolder window, ILogger<WindowsScreenProtection> logger)
    {
        _window = window;
        _logger = logger;
        _window.Created += (_, _) => Apply();
    }

    public bool IsSupported => true;

    public bool SetEnabled(bool enabled)
    {
        _enabled = enabled;
        return Apply();
    }

    private bool Apply()
    {
        var applied = false;
        var error = 0;
        var invoked = _window.TryInvoke(window =>
        {
            var hwnd = window.WindowHandle;
            if (!_enabled)
            {
                applied = SetWindowDisplayAffinity(hwnd, WdaNone);
            }
            else
            {
                applied = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
                if (!applied)
                {
                    applied = SetWindowDisplayAffinity(hwnd, WdaMonitor);
                }
            }
            if (!applied)
            {
                error = Marshal.GetLastPInvokeError();
            }
        });

        if (invoked && !applied)
        {
            _logger.LogWarning("Screen capture protection could not be changed (Win32 error {Error}).", error);
        }
        // Not created yet: the Created handler applies the requested state.
        return applied || !invoked;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);
}
