using Acorn.Application.Abstractions;

namespace Acorn.App.Platform.Common;

/// <summary>
/// Used where no OS screen-capture API is wired up (Linux). Reports unsupported so Settings can say so.
/// </summary>
internal sealed class UnsupportedScreenProtection : IScreenProtection
{
    public bool IsSupported => false;

    public bool SetEnabled(bool enabled) => !enabled;
}

/// <summary>
/// Linux: only window-minimize is detected. Screen lock / suspend are not observed, so the
/// idle auto-lock timer is the fallback (documented in SECURITY.md).
/// </summary>
internal sealed class MinimizeOnlySessionEvents : ISessionEvents, IDisposable
{
    private readonly MainWindowHolder _window;

    public MinimizeOnlySessionEvents(MainWindowHolder window)
    {
        _window = window;
        _window.Minimized += OnMinimized;
    }

    public event EventHandler<SessionLockReason>? LockRequested;

    public void Start()
    {
    }

    public void Dispose() => _window.Minimized -= OnMinimized;

    private void OnMinimized(object? sender, EventArgs e) => LockRequested?.Invoke(this, SessionLockReason.WindowMinimized);
}
