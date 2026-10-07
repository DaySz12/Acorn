namespace Acorn.Application.Abstractions;

public enum SessionLockReason
{
    ScreenLocked,
    SystemSuspend,
    SessionEnding,
    WindowMinimized,
}

/// <summary>OS signals that should lock the vault (SPEC R6). Implemented per platform.</summary>
public interface ISessionEvents
{
    /// <summary>May be raised on any thread.</summary>
    event EventHandler<SessionLockReason>? LockRequested;

    /// <summary>Starts listening. Safe to call more than once.</summary>
    void Start();
}
