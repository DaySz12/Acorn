namespace Acorn.Application.Abstractions;

/// <summary>Idle timer for auto-lock (SPEC R6).</summary>
public interface IAutoLockTimer
{
    /// <summary>Raised once when no activity was registered for the timeout. May be raised on any thread.</summary>
    event EventHandler? Expired;

    void Start(TimeSpan idleTimeout);

    void RegisterActivity();

    void Stop();
}
