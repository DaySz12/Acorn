using Acorn.Core.Crypto;
using Acorn.Core.Format;
using Acorn.Core.Models;
using Acorn.Core.Storage;
using Acorn.Core.Tests.Format;

namespace Acorn.Core.Tests.Session;

public class VaultSessionTests
{
    private static Entry SampleEntry(string name = "web-01") => new()
    {
        Name = name,
        Env = "prod",
        Host = "10.0.0.1",
        Port = 22,
        Username = "admin",
        Password = "entry-secret",
        Tags = ["web"],
        CustomFields = [new CustomField { Label = "api", Value = "token-value", IsSecret = true }],
    };

    private static RecoveryKey CreateWithEntry(SessionHarness h, string password = TestKeys.Password)
    {
        var recovery = h.Session.Create(password);
        h.Session.Data.Entries.Add(SampleEntry());
        Assert.True(h.Session.Save());
        return recovery;
    }

    [Fact]
    public void Create_close_and_reopen_with_correct_password_returns_same_data()
    {
        using var first = new SessionHarness();
        using (CreateWithEntry(first))
        {
        }

        using var reopened = first.Reopen();
        var result = reopened.Session.UnlockWithPassword(TestKeys.Password);

        Assert.False(result.Migrated);
        var entry = Assert.Single(reopened.Session.Data.Entries);
        Assert.Equal("web-01", entry.Name);
        Assert.Equal("entry-secret", entry.Password);
        Assert.Equal("token-value", Assert.Single(entry.CustomFields).Value);
    }

    [Fact]
    public void Wrong_password_fails_generically_and_stays_locked()
    {
        using var h = new SessionHarness();
        using (CreateWithEntry(h))
        {
        }
        h.Session.Lock();

        var ex = Assert.Throws<VaultAuthenticationException>(() => h.Session.UnlockWithPassword(TestKeys.OtherPassword));

        Assert.Equal(VaultAuthenticationException.GenericMessage, ex.Message);
        Assert.False(h.Session.IsUnlocked);
        Assert.Throws<VaultLockedException>(() => h.Session.Data);
    }

    [Fact]
    public void Recovery_key_opens_vault_sets_new_password_and_issues_new_key()
    {
        using var h = new SessionHarness();
        var original = CreateWithEntry(h);
        var originalText = original.ToDisplayString();
        original.Dispose();
        h.Session.Lock();

        using var replacement = h.Session.RecoverWithKey(originalText, TestKeys.OtherPassword);

        Assert.True(h.Session.IsUnlocked);
        Assert.Single(h.Session.Data.Entries);
        h.Session.Lock();
        Assert.Throws<VaultAuthenticationException>(() => h.Session.UnlockWithPassword(TestKeys.Password));
        h.Session.UnlockWithPassword(TestKeys.OtherPassword);
        h.Session.Lock();
        Assert.Throws<VaultAuthenticationException>(() => h.Session.RecoverWithKey(originalText, TestKeys.Password));
        using (h.Session.RecoverWithKey(replacement.ToDisplayString(), TestKeys.Password))
        {
        }
        Assert.Single(h.Session.Data.Entries);
    }

    [Theory]
    [InlineData("not a recovery key")]
    [InlineData("ACRN0-FXTR1-VKEY1-TEST0-ABCDE-FGHJK-MNPQR-STVWX")]
    public void Wrong_or_malformed_recovery_key_fails_generically(string recoveryKey)
    {
        using var h = new SessionHarness();
        using (CreateWithEntry(h))
        {
        }
        h.Session.Lock();
        var before = h.VaultBytes;

        var ex = Assert.Throws<VaultAuthenticationException>(() => h.Session.RecoverWithKey(recoveryKey, TestKeys.OtherPassword));

        Assert.Equal(VaultAuthenticationException.GenericMessage, ex.Message);
        Assert.False(h.Session.IsUnlocked);
        Assert.Equal(before, h.VaultBytes);
    }

    [Fact]
    public void Change_password_old_fails_new_works_data_intact_and_recovery_rotated()
    {
        using var h = new SessionHarness();
        var oldRecovery = CreateWithEntry(h).ToDisplayString();

        using var newRecovery = h.Session.ChangePassword(TestKeys.Password, TestKeys.OtherPassword);
        using var reopened = h.Reopen();

        Assert.Throws<VaultAuthenticationException>(() => reopened.Session.UnlockWithPassword(TestKeys.Password));
        reopened.Session.UnlockWithPassword(TestKeys.OtherPassword);
        Assert.Equal("entry-secret", Assert.Single(reopened.Session.Data.Entries).Password);
        reopened.Session.Lock();
        Assert.Throws<VaultAuthenticationException>(() => reopened.Session.RecoverWithKey(oldRecovery, TestKeys.Password));
        using (reopened.Session.RecoverWithKey(newRecovery.ToDisplayString(), TestKeys.Password))
        {
        }
    }

    [Fact]
    public void Change_password_with_wrong_current_password_writes_nothing()
    {
        using var h = new SessionHarness();
        using (CreateWithEntry(h))
        {
        }
        var before = h.VaultBytes;
        var backups = h.Store.Backups.List().Count;

        Assert.Throws<VaultAuthenticationException>(() => h.Session.ChangePassword(TestKeys.OtherPassword, "a-brand-new-password"));

        Assert.Equal(before, h.VaultBytes);
        Assert.Equal(backups, h.Store.Backups.List().Count);
        Assert.True(h.Session.IsUnlocked);
    }

    [Fact]
    public void New_passwords_shorter_than_12_characters_are_rejected()
    {
        using var h = new SessionHarness();

        Assert.Throws<ArgumentException>(() => h.Session.Create("short-pass"));
        Assert.False(h.Store.VaultExists);

        using (h.Session.Create(TestKeys.Password))
        {
        }
        Assert.Throws<ArgumentException>(() => h.Session.ChangePassword(TestKeys.Password, "short"));
    }

    [Fact]
    public void Regenerating_recovery_key_invalidates_the_old_one_and_keeps_password()
    {
        using var h = new SessionHarness();
        var oldRecovery = CreateWithEntry(h).ToDisplayString();

        using var newRecovery = h.Session.RegenerateRecoveryKey(TestKeys.Password);
        h.Session.Lock();

        Assert.Throws<VaultAuthenticationException>(() => h.Session.RecoverWithKey(oldRecovery, TestKeys.OtherPassword));
        h.Session.UnlockWithPassword(TestKeys.Password);
        h.Session.Lock();
        using (h.Session.RecoverWithKey(newRecovery.ToDisplayString(), TestKeys.OtherPassword))
        {
        }
    }

    [Fact]
    public void Upgrade_kdf_rewraps_password_and_keeps_recovery_key()
    {
        using var h = new SessionHarness();
        var recovery = CreateWithEntry(h).ToDisplayString();
        var stronger = TestKeys.FastKdf with { Iterations = 2 };

        h.Session.UpgradeKdf(TestKeys.Password, stronger);
        h.Session.Lock();

        Assert.Equal(stronger, VaultCodec.Parse(h.VaultBytes).Header.PasswordKdf);
        h.Session.UnlockWithPassword(TestKeys.Password);
        Assert.Equal(stronger, h.Session.CurrentKdf);
        h.Session.Lock();
        using (h.Session.RecoverWithKey(recovery, TestKeys.Password))
        {
        }
    }

    [Fact]
    public void Kdf_weaker_than_recommended_is_reported_on_unlock()
    {
        using var h = new SessionHarness(TestKeys.FastKdf);
        using (CreateWithEntry(h))
        {
        }

        using var reopened = h.Reopen(TestKeys.FastKdf with { Iterations = 2 });
        var result = reopened.Session.UnlockWithPassword(TestKeys.Password);

        Assert.True(result.KdfBelowRecommended);
    }

    [Fact]
    public void Save_is_skipped_when_nothing_changed()
    {
        using var h = new SessionHarness();
        using (CreateWithEntry(h))
        {
        }
        var backups = h.Store.Backups.List().Count;

        Assert.False(h.Session.Save());
        h.Session.Lock();
        h.Session.UnlockWithPassword(TestKeys.Password);
        Assert.False(h.Session.Save());

        Assert.Equal(backups, h.Store.Backups.List().Count);
        h.Session.Data.Entries[0].Notes = "changed";
        Assert.True(h.Session.Save());
        Assert.Equal(backups + 1, h.Store.Backups.List().Count);
    }

    [Fact]
    public void Thousand_saves_never_reuse_a_payload_nonce()
    {
        using var h = new SessionHarness();
        using (h.Session.Create(TestKeys.Password))
        {
        }
        var entry = SampleEntry();
        h.Session.Data.Entries.Add(entry);
        var nonces = new HashSet<string>();

        for (var i = 0; i < 1000; i++)
        {
            entry.Notes = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(h.Session.Save());
            Assert.True(nonces.Add(Convert.ToHexString(VaultCodec.Parse(h.VaultBytes).Payload.Nonce)));
        }

        Assert.Equal(10, h.Store.Backups.List().Count);
    }

    [Fact]
    public void Interrupted_save_leaves_previous_vault_openable()
    {
        using var h = new SessionHarness();
        using (CreateWithEntry(h))
        {
        }
        h.Session.Data.Entries[0].Name = "renamed";
        h.Store.Writer.FaultInjector = stage =>
        {
            if (stage == AtomicWriteStage.TempFlushed)
            {
                throw new IOException("simulated power loss");
            }
        };

        Assert.Throws<IOException>(() => h.Session.Save());
        h.Store.Writer.FaultInjector = null;

        using var reopened = h.Reopen();
        reopened.Session.UnlockWithPassword(TestKeys.Password);
        Assert.Equal("web-01", Assert.Single(reopened.Session.Data.Entries).Name);
    }

    [Fact]
    public void Lock_zeroes_the_key_and_drops_data()
    {
        using var h = new SessionHarness();
        using (CreateWithEntry(h))
        {
        }
        var dek = h.Session.DekForTesting!;
        var data = h.Session.Data;
        Assert.Contains(dek, b => b != 0);

        h.Session.Lock();

        Assert.All(dek, b => Assert.Equal(0, b));
        Assert.Empty(data.Entries);
        Assert.False(h.Session.IsUnlocked);
        Assert.Throws<VaultLockedException>(() => h.Session.Save());
    }

    [Fact]
    public void Legacy_v1_vault_is_migrated_on_unlock_with_backup_of_original()
    {
        using var h = new SessionHarness();
        var original = File.ReadAllBytes(LegacyFixtures.V1FixturePath);
        File.WriteAllBytes(h.Store.Paths.VaultFile, original);

        var result = h.Session.UnlockWithPassword(LegacyFixtures.V1Password);

        Assert.True(result.Migrated);
        Assert.Equal(VaultFormat.CurrentVersion, VaultCodec.Parse(h.VaultBytes).Header.FormatVersion);
        var backup = Assert.Single(h.Store.Backups.List());
        Assert.Equal(original, File.ReadAllBytes(h.Store.Backups.ResolvePath(backup.FileName)));
        Assert.Equal(["web", "legacy"], Assert.Single(h.Session.Data.Entries).Tags);

        h.Session.Lock();
        using (h.Session.RecoverWithKey(LegacyFixtures.V1RecoveryKey, TestKeys.Password))
        {
        }
    }

    [Fact]
    public void Newer_format_version_is_refused_and_file_is_not_modified()
    {
        using var h = new SessionHarness();
        using (CreateWithEntry(h))
        {
        }
        h.Session.Lock();
        var bytes = h.VaultBytes;
        bytes[5] = VaultFormat.CurrentVersion + 1;
        File.WriteAllBytes(h.Store.Paths.VaultFile, bytes);
        var backups = h.Store.Backups.List().Count;

        Assert.Throws<UnsupportedVaultVersionException>(() => h.Session.UnlockWithPassword(TestKeys.Password));

        Assert.Equal(bytes, h.VaultBytes);
        Assert.Equal(backups, h.Store.Backups.List().Count);
    }

    [Fact]
    public void Restore_backup_locks_and_reopens_older_contents()
    {
        using var h = new SessionHarness();
        using (CreateWithEntry(h))
        {
        }
        h.Time.Advance(TimeSpan.FromSeconds(1));
        h.Session.Data.Entries.Add(SampleEntry("web-02"));
        h.Session.Save();
        var backupWithOneEntry = h.Session.ListBackups()[0];

        h.Session.RestoreBackup(backupWithOneEntry.FileName);

        Assert.False(h.Session.IsUnlocked);
        h.Session.UnlockWithPassword(TestKeys.Password);
        Assert.Equal("web-01", Assert.Single(h.Session.Data.Entries).Name);
    }

    [Fact]
    public void New_and_rotated_recovery_keys_start_unconfirmed_and_the_flag_is_persisted()
    {
        using var h = new SessionHarness();
        using (h.Session.Create(TestKeys.Password))
        {
        }
        Assert.False(h.Session.Data.RecoveryKeyConfirmed);
        h.Session.Data.RecoveryKeyConfirmed = true;
        Assert.True(h.Session.Save());

        using (h.Session.ChangePassword(TestKeys.Password, TestKeys.OtherPassword))
        {
        }
        Assert.False(h.Session.Data.RecoveryKeyConfirmed);

        using var reopened = h.Reopen();
        reopened.Session.UnlockWithPassword(TestKeys.OtherPassword);
        Assert.False(reopened.Session.Data.RecoveryKeyConfirmed);
    }

    [Fact]
    public void Operations_require_the_process_lock()
    {
        using var dir = new TempDirectory();
        using var store = new VaultStore(new VaultPaths(dir.Path), TimeProvider.System);
        using var session = new VaultSession(store, TestKeys.FastKdf);

        Assert.Throws<VaultInUseException>(() => session.Create(TestKeys.Password));
    }

    [Fact]
    public void Creating_over_an_existing_vault_is_refused()
    {
        using var h = new SessionHarness();
        using (h.Session.Create(TestKeys.Password))
        {
        }
        h.Session.Lock();

        Assert.Throws<InvalidOperationException>(() => h.Session.Create(TestKeys.OtherPassword));
    }
}
