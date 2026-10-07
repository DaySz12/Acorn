using Acorn.Core;
using Acorn.Core.Crypto;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Acorn.Application.Tests.Controllers;

public class UnlockControllerTests
{
    [Fact]
    public async Task Correct_password_unlocks_applies_settings_and_shows_vault()
    {
        using var h = new ControllerHarness();
        h.Data.Settings.AutoLockMinutes = 7;
        h.Data.Settings.ScreenCaptureProtection = false;
        h.Data.Settings.Theme = "dark";
        h.Unlock.Initialize();

        var result = await h.Unlock.UnlockWithPasswordAsync(ControllerHarness.Password);

        Assert.True(result.Succeeded);
        Assert.Equal(VaultStatus.Unlocked, h.State.Status);
        Assert.Equal(Screen.Vault, h.State.Screen);
        Assert.Equal("dark", h.State.Theme);
        h.AutoLock.Received().Start(TimeSpan.FromMinutes(7));
        h.ScreenProtection.Received().SetEnabled(false);
        Assert.False(h.State.IsBusy);
    }

    [Fact]
    public async Task Wrong_password_fails_with_generic_message_and_stays_locked()
    {
        using var h = new ControllerHarness();
        h.Unlock.Initialize();

        var result = await h.Unlock.UnlockWithPasswordAsync(ControllerHarness.WrongPassword);

        Assert.False(result.Succeeded);
        Assert.Equal(Messages.UnlockFailed, result.Error);
        Assert.DoesNotContain(ControllerHarness.WrongPassword, result.Error!, StringComparison.Ordinal);
        Assert.Equal(VaultStatus.Locked, h.State.Status);
        Assert.Equal(Screen.Unlock, h.State.Screen);
        h.AutoLock.DidNotReceive().Start(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task Empty_password_is_rejected_without_touching_the_model()
    {
        using var h = new ControllerHarness();
        h.Unlock.Initialize();

        var result = await h.Unlock.UnlockWithPasswordAsync("");

        Assert.Equal(Messages.PasswordRequired, result.Error);
        h.Session.DidNotReceive().UnlockWithPassword(Arg.Any<string>());
    }

    [Theory]
    [MemberData(nameof(UnlockFailures))]
    public async Task Model_failures_map_to_safe_messages(Exception failure, string expected)
    {
        using var h = new ControllerHarness();
        h.Unlock.Initialize();
        h.FailNextUnlockWith(failure);

        var result = await h.Unlock.UnlockWithPasswordAsync(ControllerHarness.Password);

        Assert.Equal(expected, result.Error);
        Assert.Equal(VaultStatus.Locked, h.State.Status);
    }

    public static TheoryData<Exception, string> UnlockFailures() => new()
    {
        { new UnsupportedVaultVersionException(9), Messages.VaultNewer },
        { new VaultFormatException("bad"), Messages.VaultCorrupt },
        { new IOException("disk"), Messages.StorageError },
        { new InvalidOperationException("detail that must not leak"), Messages.Unexpected },
    };

    [Fact]
    public void Initialize_shows_in_use_screen_when_another_process_holds_the_lock()
    {
        using var h = new ControllerHarness(lockAvailable: false);

        h.Unlock.Initialize();

        Assert.Equal(VaultStatus.InUse, h.State.Status);
        Assert.Equal(Screen.VaultInUse, h.State.Screen);
        Assert.Equal(Messages.VaultInUse, h.State.Notice?.Message);
    }

    [Fact]
    public void Initialize_without_vault_shows_create_screen()
    {
        using var h = new ControllerHarness(vaultExists: false);

        h.Unlock.Initialize();

        Assert.Equal(VaultStatus.NoVault, h.State.Status);
        Assert.Equal(Screen.CreateVault, h.State.Screen);
    }

    [Fact]
    public void Initialize_reports_a_quarantined_temp_file()
    {
        using var h = new ControllerHarness();
        h.Session.RecoverInterruptedSave().Returns("unfinished-20260101-000000.acorn.tmp");

        h.Unlock.Initialize();

        Assert.Equal(NoticeKind.Warning, h.State.Notice?.Kind);
        Assert.Contains("unfinished-20260101-000000.acorn.tmp", h.State.Notice!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("short", "short", Messages.PasswordTooShort)]
    [InlineData("a-long-enough-password", "a-different-password", Messages.PasswordMismatch)]
    [InlineData("password1234", "password1234", Messages.PasswordTooWeak)]
    public async Task Create_validates_the_new_password_before_calling_the_model(string password, string confirmation, string expected)
    {
        using var h = new ControllerHarness(vaultExists: false);
        h.Unlock.Initialize();

        var result = await h.Unlock.CreateVaultAsync(password, confirmation);

        Assert.Equal(expected, result.Error);
        h.Session.DidNotReceive().Create(Arg.Any<string>());
    }

    [Fact]
    public async Task Create_shows_recovery_key_once_and_requires_confirmation()
    {
        using var h = new ControllerHarness(vaultExists: false);
        h.Unlock.Initialize();

        var result = await h.Unlock.CreateVaultAsync(ControllerHarness.Password, ControllerHarness.Password);

        Assert.True(result.Succeeded);
        Assert.Equal(VaultStatus.Unlocked, h.State.Status);
        Assert.Equal(Screen.RecoveryKey, h.State.Screen);
        var pending = Assert.IsType<ViewModels.RecoveryKeyViewModel>(h.State.PendingRecoveryKey);
        Assert.Equal(8, pending.Groups.Count);
        Assert.Equal(2, pending.ConfirmGroupIndexes.Distinct().Count());
        Assert.Equal(Screen.Vault, pending.ReturnTo);
    }

    [Fact]
    public async Task Recover_with_valid_key_shows_the_replacement_key()
    {
        using var h = new ControllerHarness();
        h.Unlock.Initialize();

        var result = await h.Unlock.RecoverAsync(ControllerHarness.RecoveryKeyText, ControllerHarness.Password, ControllerHarness.Password);

        Assert.True(result.Succeeded);
        Assert.Equal(Screen.RecoveryKey, h.State.Screen);
        Assert.NotNull(h.State.PendingRecoveryKey);
        Assert.Equal(VaultStatus.Unlocked, h.State.Status);
    }

    [Fact]
    public async Task Recover_with_wrong_key_fails_generically()
    {
        using var h = new ControllerHarness();
        h.Unlock.Initialize();

        var result = await h.Unlock.RecoverAsync("AAAAA-BBBBB", ControllerHarness.Password, ControllerHarness.Password);

        Assert.Equal(Messages.RecoveryFailed, result.Error);
        Assert.Equal(VaultStatus.Locked, h.State.Status);
        Assert.Null(h.State.PendingRecoveryKey);
    }

    [Fact]
    public void Strength_meter_comes_from_the_model()
    {
        using var h = new ControllerHarness();

        Assert.True(h.Unlock.EvaluatePassword("password").Score <= 1);
        Assert.True(h.Unlock.EvaluatePassword("glacier-copper-violin-harbor-7").Score >= 3);
        Assert.Equal(0, h.Unlock.EvaluatePassword("").Percent);
    }
}
