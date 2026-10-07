using System.Runtime.Versioning;
using Acorn.Application.Abstractions;
using Microsoft.Win32;

namespace Acorn.App.Platform.Windows;

/// <summary>Screen lock, sleep, sign-out and minimize on Windows (SPEC R6).</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsSessionEvents : ISessionEvents, IDisposable
{
    private readonly MainWindowHolder _window;
    private bool _started;

    public WindowsSessionEvents(MainWindowHolder window)
    {
        _window = window;
        _window.Minimized += OnMinimized;
    }

    public event EventHandler<SessionLockReason>? LockRequested;

    /// <summary>Must be called on the UI (STA) thread so SystemEvents uses its message loop.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    public void Dispose()
    {
        _window.Minimized -= OnMinimized;
        if (_started)
        {
            // SystemEvents are static: always unsubscribe.
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect
            or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.SessionLogoff)
        {
            LockRequested?.Invoke(this, SessionLockReason.ScreenLocked);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            LockRequested?.Invoke(this, SessionLockReason.SystemSuspend);
        }
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) =>
        LockRequested?.Invoke(this, SessionLockReason.SessionEnding);

    private void OnMinimized(object? sender, EventArgs e) =>
        LockRequested?.Invoke(this, SessionLockReason.WindowMinimized);
}
