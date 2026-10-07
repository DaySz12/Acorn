using Acorn.Application.Abstractions;
using Acorn.Application.Services;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Acorn.Application.Tests.Services;

public class ClipboardGuardTests : IDisposable
{
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();
    private readonly FakeTimeProvider _time = new();
    private readonly ClipboardGuard _guard;
    private string? _content;

    public ClipboardGuardTests()
    {
        _clipboard.SetTextAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(call =>
        {
            _content = call.ArgAt<string>(0);
            return Task.CompletedTask;
        });
        _clipboard.GetTextAsync().Returns(_ => Task.FromResult(_content));
        _clipboard.ClearAsync().Returns(_ =>
        {
            _content = null;
            return Task.CompletedTask;
        });
        _guard = new ClipboardGuard(_clipboard, _time);
    }

    public void Dispose() => _guard.Dispose();

    [Fact]
    public async Task Secret_is_cleared_after_the_timeout()
    {
        await _guard.CopySecretAsync("s3cret", TimeSpan.FromSeconds(30));
        await _clipboard.Received().SetTextAsync("s3cret", isSecret: true);

        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal("s3cret", _content);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(_content);
        Assert.False(_guard.OwnsClipboard);
    }

    [Fact]
    public async Task Timer_does_not_clear_what_the_user_copied_afterwards()
    {
        await _guard.CopySecretAsync("s3cret", TimeSpan.FromSeconds(30));
        _content = "copied from a browser";

        _time.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal("copied from a browser", _content);
        await _clipboard.DidNotReceive().ClearAsync();
    }

    [Fact]
    public async Task Copying_a_new_secret_restarts_the_timer()
    {
        await _guard.CopySecretAsync("first", TimeSpan.FromSeconds(30));
        _time.Advance(TimeSpan.FromSeconds(20));
        await _guard.CopySecretAsync("second", TimeSpan.FromSeconds(30));

        _time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal("second", _content);

        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Null(_content);
    }

    [Fact]
    public async Task Non_secret_copies_are_never_cleared()
    {
        await _guard.CopySecretAsync("s3cret", TimeSpan.FromSeconds(30));
        await _guard.CopyTextAsync("10.0.0.1");

        _time.Advance(TimeSpan.FromMinutes(5));
        var cleared = await _guard.ClearIfOwnedAsync();

        Assert.False(cleared);
        Assert.Equal("10.0.0.1", _content);
        await _clipboard.Received().SetTextAsync("10.0.0.1", isSecret: false);
    }

    [Fact]
    public async Task Clear_on_lock_or_exit_is_immediate_when_value_still_matches()
    {
        await _guard.CopySecretAsync("s3cret", TimeSpan.FromSeconds(30));

        Assert.True(await _guard.ClearIfOwnedAsync());

        Assert.Null(_content);
        Assert.False(await _guard.ClearIfOwnedAsync());
    }

    [Fact]
    public async Task Clear_does_nothing_when_nothing_was_copied()
    {
        Assert.False(await _guard.ClearIfOwnedAsync());

        await _clipboard.DidNotReceive().GetTextAsync();
        await _clipboard.DidNotReceive().ClearAsync();
    }
}
