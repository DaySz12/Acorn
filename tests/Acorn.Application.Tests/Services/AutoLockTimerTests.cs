using Acorn.Application.Services;
using Microsoft.Extensions.Time.Testing;

namespace Acorn.Application.Tests.Services;

public class AutoLockTimerTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Expires_once_after_idle_timeout()
    {
        using var timer = new AutoLockTimer(_time);
        var expirations = 0;
        timer.Expired += (_, _) => expirations++;

        timer.Start(TimeSpan.FromMinutes(5));
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, expirations);

        _time.Advance(TimeSpan.FromMinutes(1));
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, expirations);
    }

    [Fact]
    public void Activity_pushes_the_deadline_back()
    {
        using var timer = new AutoLockTimer(_time);
        var expired = false;
        timer.Expired += (_, _) => expired = true;

        timer.Start(TimeSpan.FromMinutes(5));
        _time.Advance(TimeSpan.FromMinutes(4));
        timer.RegisterActivity();
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.False(expired);

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(expired);
    }

    [Fact]
    public void Stop_cancels_and_activity_after_stop_does_not_restart()
    {
        using var timer = new AutoLockTimer(_time);
        var expired = false;
        timer.Expired += (_, _) => expired = true;

        timer.Start(TimeSpan.FromMinutes(5));
        timer.Stop();
        timer.RegisterActivity();
        _time.Advance(TimeSpan.FromHours(1));

        Assert.False(expired);
    }
}
