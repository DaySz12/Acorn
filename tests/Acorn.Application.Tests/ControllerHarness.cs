using Acorn.Application.Abstractions;
using Acorn.Application.Controllers;
using Acorn.Application.Services;
using Acorn.Core;
using Acorn.Core.Crypto;
using Acorn.Core.Models;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Acorn.Application.Tests;

/// <summary>
/// Real controllers wired to NSubstitute mocks of every abstraction. The mocked session behaves
/// like a vault protected by <see cref="Password"/>; the mocked clipboard stores one string.
/// </summary>
internal sealed class ControllerHarness : IDisposable
{
    public const string Password = "maple-river-quartz-lantern-42";
    public const string WrongPassword = "definitely-not-the-password";
    public const string RecoveryKeyText = "ACRN0-FXTR1-VKEY1-TEST0-ABCDE-FGHJK-MNPQR-STVWX";

    private bool _unlocked;

    public ControllerHarness(bool vaultExists = true, bool lockAvailable = true)
    {
        VaultExists = vaultExists;
        Session.TryAcquireProcessLock().Returns(lockAvailable);
        Session.VaultExists.Returns(_ => VaultExists);
        Session.IsUnlocked.Returns(_ => _unlocked);
        Session.Data.Returns(_ => _unlocked ? Data : throw new VaultLockedException());
        Session.CurrentKdf.Returns(Argon2idParameters.Recommended);
        Session.StorageLocation.Returns("C:\\data\\Acorn");
        Session.Save().Returns(true);
        Session.When(s => s.Lock()).Do(_ => _unlocked = false);
        Session.UnlockWithPassword(Arg.Any<string>()).Returns(call =>
        {
            if (call.Arg<string>() != Password)
            {
                throw new VaultAuthenticationException();
            }
            _unlocked = true;
            return UnlockResult;
        });
        Session.RecoverWithKey(Arg.Any<string>(), Arg.Any<string>()).Returns(call =>
        {
            if (call.ArgAt<string>(0) != RecoveryKeyText)
            {
                throw new VaultAuthenticationException();
            }
            _unlocked = true;
            return RecoveryKey.Generate();
        });
        Session.Create(Arg.Any<string>()).Returns(_ =>
        {
            _unlocked = true;
            VaultExists = true;
            return RecoveryKey.Generate();
        });

        Clipboard.SetTextAsync(Arg.Any<string>(), Arg.Any<bool>()).Returns(call =>
        {
            ClipboardContent = call.ArgAt<string>(0);
            return Task.CompletedTask;
        });
        Clipboard.GetTextAsync().Returns(_ => Task.FromResult(ClipboardContent));
        Clipboard.ClearAsync().Returns(_ =>
        {
            ClipboardContent = null;
            return Task.CompletedTask;
        });

        var realNavigator = new AppStateNavigator(State);
        Navigator.When(n => n.GoTo(Arg.Any<Screen>())).Do(call => realNavigator.GoTo(call.Arg<Screen>()));
        ScreenProtection.SetEnabled(Arg.Any<bool>()).Returns(true);

        Guard = new ClipboardGuard(Clipboard, Time);
        Vault = new VaultController(Session, State, Navigator, Guard, AutoLock, SessionEvents, ScreenProtection);
        Unlock = new UnlockController(Session, State, Navigator, Vault);
        Entries = new EntryController(Session, State, Navigator, Guard, Vault, Time);
        Settings = new SettingsController(Session, State, Navigator, Vault, ScreenProtection);
        Backups = new BackupController(Session, State, Navigator, Vault);
    }

    public IVaultSession Session { get; } = Substitute.For<IVaultSession>();
    public IClipboardService Clipboard { get; } = Substitute.For<IClipboardService>();
    public ISessionEvents SessionEvents { get; } = Substitute.For<ISessionEvents>();
    public IScreenProtection ScreenProtection { get; } = Substitute.For<IScreenProtection>();
    public IAutoLockTimer AutoLock { get; } = Substitute.For<IAutoLockTimer>();
    public INavigator Navigator { get; } = Substitute.For<INavigator>();
    public FakeTimeProvider Time { get; } = new();
    public AppState State { get; } = new();
    public ClipboardGuard Guard { get; }
    public VaultController Vault { get; }
    public UnlockController Unlock { get; }
    public EntryController Entries { get; }
    public SettingsController Settings { get; }
    public BackupController Backups { get; }

    public bool VaultExists { get; set; }
    public string? ClipboardContent { get; set; }
    public VaultData Data { get; } = new();
    public UnlockResult UnlockResult { get; set; } = new(Migrated: false, KdfBelowRecommended: false);

    /// <summary>Unlocks through the controller and clears the mock call history.</summary>
    public async Task UnlockAsync()
    {
        Unlock.Initialize();
        var result = await Unlock.UnlockWithPasswordAsync(Password);
        Assert.True(result.Succeeded);
        Session.ClearReceivedCalls();
        Clipboard.ClearReceivedCalls();
        Navigator.ClearReceivedCalls();
        AutoLock.ClearReceivedCalls();
        ScreenProtection.ClearReceivedCalls();
    }

    public void FailNextUnlockWith(Exception exception) =>
        Session.UnlockWithPassword(Arg.Any<string>()).Throws(exception);

    public void Dispose()
    {
        Entries.Dispose();
        Vault.Dispose();
        Guard.Dispose();
    }
}
