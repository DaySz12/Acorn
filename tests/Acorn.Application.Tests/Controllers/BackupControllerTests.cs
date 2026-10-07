using Acorn.Core.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Acorn.Application.Tests.Controllers;

public class BackupControllerTests
{
    private static readonly BackupInfo Older = new("vault-20260101-080000.acorn.bak", new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), 2048);
    private static readonly BackupInfo Newer = new("vault-20260102-080000.acorn.bak", new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc), 4096);

    [Fact]
    public async Task Restore_while_unlocked_replaces_vault_then_locks_with_notice()
    {
        using var h = new ControllerHarness();
        h.Session.ListBackups().Returns([Newer, Older]);
        await h.UnlockAsync();
        await h.Guard.CopySecretAsync("secret", TimeSpan.FromSeconds(30));
        h.Backups.Open();

        var result = await h.Backups.RestoreAsync(Older.FileName);

        Assert.True(result.Succeeded);
        h.Session.Received(1).RestoreBackup(Older.FileName);
        Assert.Null(h.ClipboardContent);
        Assert.Equal(Screen.Unlock, h.State.Screen);
        Assert.Equal(VaultStatus.Locked, h.State.Status);
        Assert.Equal(Messages.LockedRestored, h.State.Notice?.Message);
        Assert.Empty(h.State.EntryList.Items);
    }

    [Fact]
    public async Task Restore_from_the_lock_screen_ends_on_unlock_screen()
    {
        using var h = new ControllerHarness();
        h.Session.ListBackups().Returns([Older]);
        h.Unlock.Initialize();
        h.Backups.Open();

        Assert.Equal(Screen.Backups, h.State.Screen);
        Assert.Equal("2 KB", Assert.Single(h.State.Backups).SizeText);

        var result = await h.Backups.RestoreAsync(Older.FileName);

        Assert.True(result.Succeeded);
        Assert.Equal(Screen.Unlock, h.State.Screen);
    }

    [Fact]
    public async Task Only_listed_backups_can_be_restored()
    {
        using var h = new ControllerHarness();
        h.Session.ListBackups().Returns([Older]);
        h.Unlock.Initialize();
        h.Backups.Open();

        var result = await h.Backups.RestoreAsync("../vault.acorn");

        Assert.Equal(Messages.BackupNotFound, result.Error);
        h.Session.DidNotReceive().RestoreBackup(Arg.Any<string>());
    }

    [Fact]
    public async Task Failed_restore_keeps_the_current_state()
    {
        using var h = new ControllerHarness();
        h.Session.ListBackups().Returns([Older]);
        h.Session.When(s => s.RestoreBackup(Arg.Any<string>())).Do(_ => throw new IOException("locked file"));
        await h.UnlockAsync();
        h.Backups.Open();

        var result = await h.Backups.RestoreAsync(Older.FileName);

        Assert.Equal(Messages.StorageError, result.Error);
        Assert.Equal(Screen.Backups, h.State.Screen);
        Assert.Equal(VaultStatus.Unlocked, h.State.Status);
    }

    [Fact]
    public async Task Back_returns_to_where_the_user_came_from()
    {
        using var h = new ControllerHarness();
        await h.UnlockAsync();
        h.Settings.Open();
        h.Backups.Open();

        h.Backups.Back();

        Assert.Equal(Screen.Settings, h.State.Screen);
    }
}
