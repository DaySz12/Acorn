using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Acorn.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Acorn.App.Platform.MacOS;

/// <summary>
/// macOS screen lock (com.apple.screenIsLocked distributed notification), sleep
/// (NSWorkspaceWillSleepNotification / ScreensDidSleep) and fast user switching, via an
/// Objective-C observer class registered at runtime. If interop fails, the idle auto-lock
/// timer remains the fallback (SPEC R6). NOT verified on real hardware by the author —
/// see docs/MANUAL_TESTS.md.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacSessionEvents : ISessionEvents, IDisposable
{
    private const string ObserverClassName = "AcornSessionObserver";
    private static MacSessionEvents? s_current;

    private readonly MainWindowHolder _window;
    private readonly ILogger<MacSessionEvents> _logger;
    private nint _observer;
    private bool _started;

    public MacSessionEvents(MainWindowHolder window, ILogger<MacSessionEvents> logger)
    {
        _window = window;
        _logger = logger;
        _window.Minimized += OnMinimized;
    }

    public event EventHandler<SessionLockReason>? LockRequested;

    /// <summary>Call on the main thread after the window (and therefore NSApplication) exists.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        s_current = this;

        try
        {
            var cls = ObjC.GetClass(ObserverClassName);
            if (cls == 0)
            {
                cls = ObjC.AllocateClassPair(ObjC.GetClass("NSObject"), ObserverClassName, 0);
                ObjC.AddMethod(cls, ObjC.Selector("acornScreenLocked:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnScreenLocked, "v@:@");
                ObjC.AddMethod(cls, ObjC.Selector("acornWillSleep:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnWillSleep, "v@:@");
                ObjC.RegisterClassPair(cls);
            }
            _observer = ObjC.Send(ObjC.Send(cls, ObjC.Selector("alloc")), ObjC.Selector("init"));
            var addObserver = ObjC.Selector("addObserver:selector:name:object:");

            var distributed = ObjC.Send(ObjC.GetClass("NSDistributedNotificationCenter"), ObjC.Selector("defaultCenter"));
            ObjC.Send(distributed, addObserver, _observer, ObjC.Selector("acornScreenLocked:"), ObjC.NSString("com.apple.screenIsLocked"), 0);

            var workspace = ObjC.Send(ObjC.GetClass("NSWorkspace"), ObjC.Selector("sharedWorkspace"));
            var center = ObjC.Send(workspace, ObjC.Selector("notificationCenter"));
            ObjC.Send(center, addObserver, _observer, ObjC.Selector("acornWillSleep:"), ObjC.NSString("NSWorkspaceWillSleepNotification"), 0);
            ObjC.Send(center, addObserver, _observer, ObjC.Selector("acornWillSleep:"), ObjC.NSString("NSWorkspaceScreensDidSleepNotification"), 0);
            ObjC.Send(center, addObserver, _observer, ObjC.Selector("acornScreenLocked:"), ObjC.NSString("NSWorkspaceSessionDidResignActiveNotification"), 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            _logger.LogWarning("macOS lock/sleep notifications unavailable; relying on the idle auto-lock timer.");
        }
    }

    public void Dispose()
    {
        _window.Minimized -= OnMinimized;
        if (_observer != 0)
        {
            try
            {
                var removeObserver = ObjC.Selector("removeObserver:");
                ObjC.Send(ObjC.Send(ObjC.GetClass("NSDistributedNotificationCenter"), ObjC.Selector("defaultCenter")), removeObserver, _observer);
                var workspace = ObjC.Send(ObjC.GetClass("NSWorkspace"), ObjC.Selector("sharedWorkspace"));
                ObjC.Send(ObjC.Send(workspace, ObjC.Selector("notificationCenter")), removeObserver, _observer);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
            }
            _observer = 0;
        }
        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnScreenLocked(nint self, nint selector, nint notification) =>
        s_current?.LockRequested?.Invoke(s_current, SessionLockReason.ScreenLocked);

    [UnmanagedCallersOnly]
    private static void OnWillSleep(nint self, nint selector, nint notification) =>
        s_current?.LockRequested?.Invoke(s_current, SessionLockReason.SystemSuspend);

    private void OnMinimized(object? sender, EventArgs e) => LockRequested?.Invoke(this, SessionLockReason.WindowMinimized);
}
