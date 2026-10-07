using Acorn.Core.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Acorn.Core.Tests.Storage;

public class BackupRotatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    [Fact]
    public void Backup_name_uses_utc_timestamp()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("vault.acorn"), "v1");
        var rotator = new BackupRotator(dir.File("backups"), new FakeTimeProvider(Start));

        var backup = rotator.CreateBackup(dir.File("vault.acorn"));

        Assert.Equal("vault-20260304-050607.acorn.bak", backup.FileName);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(dir.File("backups"), backup.FileName)));
    }

    [Fact]
    public void Backups_in_the_same_second_get_sequence_numbers_and_sort_newest_first()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("vault.acorn"), "v");
        var rotator = new BackupRotator(dir.File("backups"), new FakeTimeProvider(Start));

        rotator.CreateBackup(dir.File("vault.acorn"));
        rotator.CreateBackup(dir.File("vault.acorn"));
        rotator.CreateBackup(dir.File("vault.acorn"));

        Assert.Equal(
            ["vault-20260304-050607-3.acorn.bak", "vault-20260304-050607-2.acorn.bak", "vault-20260304-050607.acorn.bak"],
            rotator.List().Select(b => b.FileName));
    }

    [Fact]
    public void Prune_keeps_newest_and_ignores_unrelated_files()
    {
        using var dir = new TempDirectory();
        var backups = dir.File("backups");
        File.WriteAllText(dir.File("vault.acorn"), "v");
        var time = new FakeTimeProvider(Start);
        var rotator = new BackupRotator(backups, time);
        for (var i = 0; i < 12; i++)
        {
            rotator.CreateBackup(dir.File("vault.acorn"));
            time.Advance(TimeSpan.FromMinutes(1));
        }
        string[] unrelated = ["notes.txt", "vault-mine.acorn.bak", "unfinished-20200101-000000.acorn.tmp", "vault-20200101-000000.acorn"];
        foreach (var name in unrelated)
        {
            File.WriteAllText(Path.Combine(backups, name), "keep me");
        }

        var deleted = rotator.Prune(5);

        Assert.Equal(7, deleted);
        var remaining = rotator.List();
        Assert.Equal(5, remaining.Count);
        Assert.Equal("vault-20260304-051707.acorn.bak", remaining[0].FileName);
        Assert.Equal("vault-20260304-051307.acorn.bak", remaining[^1].FileName);
        Assert.All(unrelated, name => Assert.True(File.Exists(Path.Combine(backups, name))));
    }

    [Theory]
    [InlineData("../vault.acorn")]
    [InlineData("..\\vault-20260304-050607.acorn.bak")]
    [InlineData("vault.acorn")]
    [InlineData("")]
    public void Resolve_rejects_anything_but_a_backup_file_name(string name)
    {
        var rotator = new BackupRotator("backups", TimeProvider.System);

        Assert.Throws<ArgumentException>(() => rotator.ResolvePath(name));
    }
}
