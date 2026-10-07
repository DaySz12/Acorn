using Acorn.Application.Abstractions;

namespace Acorn.Application.Services;

/// <summary>
/// Idle timer on <see cref="TimeProvider"/>. Activity (key presses, clicks in the window) pushes the
/// deadline back; when it passes, <see cref="Expired"/> fires once.
/// </summary>
public sealed class AutoLockTimer : IAutoLockTimer, IDisposable
{
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private ITimer? _timer;
    private TimeSpan _timeout;

    public AutoLockTimer(TimeProvider time) => _time = time;

    public event EventHandler? Expired;

    public void Start(TimeSpan idleTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleTimeout, TimeSpan.Zero);
        lock (_gate)
        {
            _timer?.Dispose();
            _timeout = idleTimeout;
            _timer = _time.CreateTimer(OnElapsed, null, idleTimeout, Timeout.InfiniteTimeSpan);
        }
    }

    public void RegisterActivity()
    {
        lock (_gate)
        {
            _timer?.Change(_timeout, Timeout.InfiniteTimeSpan);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Dispose() => Stop();

    private void OnElapsed(object? state)
    {
        lock (_gate)
        {
            if (_timer is null)
            {
                return;
            }
            _timer.Dispose();
            _timer = null;
        }
        Expired?.Invoke(this, EventArgs.Empty);
    }
}
