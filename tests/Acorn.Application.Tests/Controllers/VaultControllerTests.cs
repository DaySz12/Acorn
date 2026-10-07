using Acorn.Application.Abstractions;
using Acorn.Application.Controllers;
using Acorn.Application.ViewModels;
using Acorn.Core;
using Acorn.Core.Crypto;
using Acorn.Core.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Acorn.Application.Tests.Controllers;

public class VaultControllerTests
{
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(condition());
    }

    [Fact]
    public async Task Lock_clears_clipboard_then_zeroes_key_then_resets_state_and_shows_unlock()
    {
        using var h = new ControllerHarness();
        h.Data.Entries.Add(new Entry { Name = "db", Password = "secret" });
        await h.UnlockAsync();
        await h.Guard.CopySecretAsync("secret", TimeSpan.FromSeconds(30));
        h.State.RevealSecret(SecretRef.Password(Guid.NewGuid()), "secret");

        await h.Vault.LockAsync();

        Received.InOrder(() =>
        {
            h.Clipboard.ClearAsync();
            h.Session.Lock();
            h.Navigator.GoTo(Screen.Unlock);
        });
        Assert.Null(h.ClipboardContent);
        Assert.Equal(VaultStatus.Locked, h.State.Status);
        Assert.Equal(Screen.Unlock, h.State.Screen);
        Assert.Empty(h.State.EntryList.Items);
        Assert.Empty(h.State.RevealedSecrets);
        Assert.Null(h.State.SelectedEntry);
        Assert.Null(h.State.Editor);
        h.AutoLock.Received().Stop();
        h.ScreenProtection.Received().SetEnabled(true);
    }

    [Fact]
    public async Task Lock_leaves_clipboard_alone_when_user_copied_something_else()
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();
        await h.Guard.CopySecretAsync("secret", TimeSpan.FromSeconds(30));
        h.ClipboardContent = "something the user copied later";

        await h.Vault.LockAsync();

        await h.Clipboard.DidNotReceive().ClearAsync();
        Assert.Equal("something the user copied later", h.ClipboardContent);
        h.Session.Received().Lock();
    }

    [Theory]
    [InlineData(SessionLockReason.ScreenLocked)]
    [InlineData(SessionLockReason.SystemSuspend)]
    [InlineData(SessionLockReason.SessionEnding)]
    public async Task Os_session_events_lock_the_vault(SessionLockReason reason)
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();
        await h.Guard.CopySecretAsync("secret", TimeSpan.FromSeconds(30));

        h.SessionEvents.LockRequested += Raise.Event<EventHandler<SessionLockReason>>(h.SessionEvents, reason);

        await WaitUntil(() => h.State.Screen == Screen.Unlock);
        h.Session.Received().Lock();
        Assert.Null(h.ClipboardContent);
        Assert.Equal(VaultStatus.Locked, h.State.Status);
    }

    [Fact]
    public async Task Auto_lock_timer_expiry_locks_the_vault()
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();

        h.AutoLock.Expired += Raise.Event();

        await WaitUntil(() => h.State.Screen == Screen.Unlock);
        h.Session.Received().Lock();
        Assert.Equal(Messages.LockedIdle, h.State.Notice?.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Minimize_locks_only_when_enabled_in_settings(bool lockOnMinimize)
    {
        using var h = new ControllerHarness();
        h.Data.Settings.LockOnMinimize = lockOnMinimize;
        await h.UnlockAsync();

        h.SessionEvents.LockRequested += Raise.Event<EventHandler<SessionLockReason>>(h.SessionEvents, SessionLockReason.WindowMinimized);

        if (lockOnMinimize)
        {
            await WaitUntil(() => h.State.Screen == Screen.Unlock);
            h.Session.Received().Lock();
        }
        else
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            h.Session.DidNotReceive().Lock();
            Assert.Equal(Screen.Vault, h.State.Screen);
        }
    }

    [Fact]
    public async Task Activity_postpones_auto_lock_only_while_unlocked()
    {
        using var h = new ControllerHarness();
        h.Unlock.Initialize();
        h.Vault.NotifyActivity();
        h.AutoLock.DidNotReceive().RegisterActivity();

        await h.UnlockAsync();
        h.Vault.NotifyActivity();
        h.AutoLock.Received(1).RegisterActivity();
    }

    [Fact]
    public async Task Shutdown_clears_owned_clipboard_and_zeroes_keys()
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();
        await h.Guard.CopySecretAsync("secret", TimeSpan.FromSeconds(30));

        await h.Vault.ShutdownAsync();

        Assert.Null(h.ClipboardContent);
        h.Session.Received().Lock();
    }

    [Fact]
    public async Task Change_password_shows_new_recovery_key_and_returns_to_settings()
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();
        h.Session.ChangePassword(ControllerHarness.Password, "a-new-long-master-passphrase").Returns(_ => RecoveryKey.Generate());

        var result = await h.Vault.ChangePasswordAsync(ControllerHarness.Password, "a-new-long-master-passphrase", "a-new-long-master-passphrase");

        Assert.True(result.Succeeded);
        Assert.Equal(Screen.RecoveryKey, h.State.Screen);
        Assert.Equal(Screen.Settings, h.State.PendingRecoveryKey?.ReturnTo);
        Assert.Equal(Messages.PasswordChanged, h.State.Notice?.Message);
        Assert.Equal(VaultStatus.Unlocked, h.State.Status);
    }

    [Fact]
    public async Task Change_password_with_wrong_current_password_reports_it()
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();
        h.Session.ChangePassword(Arg.Any<string>(), Arg.Any<string>()).Throws(new VaultAuthenticationException());

        var result = await h.Vault.ChangePasswordAsync(ControllerHarness.WrongPassword, "a-new-long-master-passphrase", "a-new-long-master-passphrase");

        Assert.Equal(Messages.CurrentPasswordWrong, result.Error);
        Assert.Null(h.State.PendingRecoveryKey);
        Assert.Equal(Screen.Vault, h.State.Screen);
    }

    [Theory]
    [InlineData("short", "short", Messages.PasswordTooShort)]
    [InlineData("a-new-long-master-passphrase", "typo", Messages.PasswordMismatch)]
    [InlineData(ControllerHarness.Password, ControllerHarness.Password, Messages.PasswordSameAsCurrent)]
    public async Task Change_password_validates_before_calling_the_model(string newPassword, string confirmation, string expected)
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();

        var result = await h.Vault.ChangePasswordAsync(ControllerHarness.Password, newPassword, confirmation);

        Assert.Equal(expected, result.Error);
        h.Session.DidNotReceive().ChangePassword(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Regenerate_recovery_key_goes_through_confirmation()
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();
        h.Session.RegenerateRecoveryKey(ControllerHarness.Password).Returns(_ => RecoveryKey.Generate());

        var result = await h.Vault.RegenerateRecoveryKeyAsync(ControllerHarness.Password);

        Assert.True(result.Succeeded);
        Assert.Equal(Screen.RecoveryKey, h.State.Screen);
        Assert.NotNull(h.State.PendingRecoveryKey);
    }

    [Fact]
    public async Task Upgrade_kdf_uses_preset_and_clears_suggestion()
    {
        using var h = new ControllerHarness { UnlockResult = new UnlockResult(false, KdfBelowRecommended: true) };
        await h.UnlockAsync();
        Assert.True(h.State.KdfUpgradeSuggested);

        var result = await h.Vault.UpgradeKdfAsync(ControllerHarness.Password, KdfPreset.Strong);

        Assert.True(result.Succeeded);
        h.Session.Received().UpgradeKdf(ControllerHarness.Password, Argon2idParameters.Strong);
        Assert.False(h.State.KdfUpgradeSuggested);
    }

    [Fact]
    public async Task Recovery_key_confirmation_accepts_only_the_requested_groups()
    {
        using var h = new ControllerHarness(vaultExists: false);
        h.Unlock.Initialize();
        await h.Unlock.CreateVaultAsync(ControllerHarness.Password, ControllerHarness.Password);
        var pending = h.State.PendingRecoveryKey!;
        var expected = pending.ConfirmGroupIndexes.Select(i => pending.Groups[i]).ToList();

        var wrong = h.Vault.ConfirmRecoveryKeySaved(["XXXXX", "YYYYY"]);
        Assert.Equal(Messages.RecoveryConfirmMismatch, wrong.Error);
        Assert.Same(pending, h.State.PendingRecoveryKey);

        // Lower case and O-for-0 / l-for-1 typing is accepted.
        var typed = expected.Select(g => g.ToLowerInvariant().Replace('0', 'o').Replace('1', 'l')).ToList();
        var ok = h.Vault.ConfirmRecoveryKeySaved(typed);

        Assert.True(ok.Succeeded);
        Assert.Null(h.State.PendingRecoveryKey);
        Assert.Equal(Screen.Vault, h.State.Screen);
    }

    [Fact]
    public async Task Copying_the_recovery_key_is_treated_as_a_secret()
    {
        using var h = new ControllerHarness(vaultExists: false);
        h.Unlock.Initialize();
        await h.Unlock.CreateVaultAsync(ControllerHarness.Password, ControllerHarness.Password);

        await h.Vault.CopyRecoveryKeyAsync();

        await h.Clipboard.Received().SetTextAsync(h.State.PendingRecoveryKey!.Formatted, isSecret: true);
        h.Time.Advance(TimeSpan.FromSeconds(31));
        Assert.Null(h.ClipboardContent);
    }

    [Fact]
    public async Task Locking_while_the_recovery_key_is_displayed_drops_it()
    {
        using var h = new ControllerHarness(vaultExists: false);
        h.Unlock.Initialize();
        await h.Unlock.CreateVaultAsync(ControllerHarness.Password, ControllerHarness.Password);

        await h.Vault.LockAsync();

        Assert.Null(h.State.PendingRecoveryKey);
        Assert.Equal(Screen.Unlock, h.State.Screen);
        Assert.Equal(Messages.RecoveryKeyLostOnLock, h.State.Notice?.Message);
    }

    [Fact]
    public async Task Unconfirmed_recovery_key_is_flagged_after_unlock_and_cleared_by_confirmation()
    {
        using var h = new ControllerHarness();
        h.Data.RecoveryKeyConfirmed = false;
        await h.UnlockAsync();
        Assert.True(h.State.RecoveryKeyUnconfirmed);
        h.Session.RegenerateRecoveryKey(ControllerHarness.Password).Returns(_ => RecoveryKey.Generate());

        await h.Vault.RegenerateRecoveryKeyAsync(ControllerHarness.Password);
        var pending = h.State.PendingRecoveryKey!;
        var result = h.Vault.ConfirmRecoveryKeySaved(pending.ConfirmGroupIndexes.Select(i => pending.Groups[i]).ToList());

        Assert.True(result.Succeeded);
        Assert.True(h.Data.RecoveryKeyConfirmed);
        Assert.False(h.State.RecoveryKeyUnconfirmed);
        h.Session.Received(1).Save();
    }
}
