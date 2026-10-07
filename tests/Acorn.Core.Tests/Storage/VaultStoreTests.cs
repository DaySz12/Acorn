using Acorn.Core.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Acorn.Core.Tests.Storage;

public class VaultStoreTests
{
    private static readonly Action<byte[]> AcceptAll = _ => { };

    private static VaultStore CreateStore(TempDirectory dir, FakeTimeProvider? time = null)
    {
        var store = new VaultStore(new VaultPaths(dir.Path), time ?? new FakeTimeProvider());
        Assert.True(store.TryAcquireLock());
        return store;
    }

    [Fact]
    public void Writing_requires_the_lock()
    {
        using var dir = new TempDirectory();
        using var store = new VaultStore(new VaultPaths(dir.Path), TimeProvider.System);

        Assert.Throws<VaultInUseException>(() => store.Write("x"u8, AcceptAll, 10));
    }

    [Fact]
    public void Every_overwrite_is_preceded_by_a_backup_of_the_previous_file()
    {
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider();
        using var store = CreateStore(dir, time);

        store.Write("one"u8, AcceptAll, 10);
        Assert.Empty(store.Backups.List());

        time.Advance(TimeSpan.FromSeconds(1));
        store.Write("two"u8, AcceptAll, 10);
        time.Advance(TimeSpan.FromSeconds(1));
        store.Write("three"u8, AcceptAll, 10);

        var backups = store.Backups.List();
        Assert.Equal(2, backups.Count);
        Assert.Equal("two", File.ReadAllText(store.Backups.ResolvePath(backups[0].FileName)));
        Assert.Equal("one", File.ReadAllText(store.Backups.ResolvePath(backups[1].FileName)));
        Assert.Equal("three", File.ReadAllText(store.Paths.VaultFile));
    }

    [Fact]
    public void Backups_are_pruned_to_the_configured_count()
    {
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider();
        using var store = CreateStore(dir, time);

        for (var i = 0; i < 9; i++)
        {
            store.Write([(byte)i], AcceptAll, 5);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(5, store.Backups.List().Count);
    }

    [Fact]
    public void Failed_write_keeps_the_existing_vault()
    {
        using var dir = new TempDirectory();
        using var store = CreateStore(dir);
        store.Write("good"u8, AcceptAll, 10);

        Assert.Throws<VaultAuthenticationException>(() => store.Write("bad"u8, _ => throw new VaultAuthenticationException(), 10));

        Assert.Equal("good", File.ReadAllText(store.Paths.VaultFile));
        Assert.False(File.Exists(store.Paths.TempFile));
    }

    [Fact]
    public void Leftover_temp_file_is_moved_to_backups_without_touching_the_vault()
    {
        using var dir = new TempDirectory();
        using var store = CreateStore(dir);
        store.Write("vault"u8, AcceptAll, 10);
        File.WriteAllText(store.Paths.TempFile, "half-written");

        var name = store.QuarantineLeftoverTemp();

        Assert.NotNull(name);
        Assert.False(File.Exists(store.Paths.TempFile));
        Assert.Equal("half-written", File.ReadAllText(Path.Combine(store.Paths.BackupsDirectory, name)));
        Assert.Equal("vault", File.ReadAllText(store.Paths.VaultFile));
        Assert.Null(store.QuarantineLeftoverTemp());
    }

    [Fact]
    public void Restore_replaces_vault_with_backup_and_backs_up_the_current_one()
    {
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider();
        using var store = CreateStore(dir, time);
        store.Write("old"u8, AcceptAll, 10);
        time.Advance(TimeSpan.FromSeconds(1));
        store.Write("new"u8, AcceptAll, 10);
        var oldBackup = store.Backups.List().Single();

        time.Advance(TimeSpan.FromSeconds(1));
        store.RestoreBackup(oldBackup.FileName, AcceptAll, backupKeep: null);

        Assert.Equal("old", File.ReadAllText(store.Paths.VaultFile));
        Assert.Contains(store.Backups.List(), b => File.ReadAllText(store.Backups.ResolvePath(b.FileName)) == "new");
    }
}
