using System.Text.Json.Nodes;
using Acorn.Core.Crypto;
using Acorn.Core.Format;
using Acorn.Core.Format.Migrations;
using Acorn.Core.Models;

namespace Acorn.Core.Tests.Format;

public class MigrationTests
{
    [Fact]
    public void V1_fixture_parses_as_version_1()
    {
        var file = VaultCodec.Parse(File.ReadAllBytes(LegacyFixtures.V1FixturePath));

        Assert.Equal(1, file.Header.FormatVersion);
        Assert.Equal(TestKeys.FastKdf, file.Header.PasswordKdf);
    }

    [Fact]
    public void V1_fixture_decrypts_migrates_and_reseals_as_current_version()
    {
        var file = VaultCodec.Parse(File.ReadAllBytes(LegacyFixtures.V1FixturePath));
        var dek = VaultCrypto.UnwrapWithPassword(file.Header, TestKeys.Utf8(LegacyFixtures.V1Password));
        var (data, migrated) = VaultSerializer.Deserialize(VaultCrypto.OpenPayload(file, dek));

        Assert.True(migrated);
        Assert.Equal(VaultData.CurrentSchemaVersion, data.SchemaVersion);
        var entry = Assert.Single(data.Entries);
        Assert.Equal(LegacyFixtures.V1EntryId, entry.Id);
        Assert.Equal(["web", "legacy"], entry.Tags);
        Assert.Equal(2222, entry.Port);
        Assert.True(entry.Favorite);
        Assert.Equal("dark", data.Settings.Theme);

        // Re-seal: the recovery wrap is carried over unchanged and still opens the upgraded file.
        var upgraded = VaultCodec.Parse(VaultCrypto.Seal(file.Header, dek, VaultSerializer.Serialize(data)));
        Assert.Equal(VaultFormat.CurrentVersion, upgraded.Header.FormatVersion);
        Assert.True(RecoveryKey.TryParse(LegacyFixtures.V1RecoveryKey, out var recovery));
        using (recovery)
        {
            var dekFromRecovery = VaultCrypto.UnwrapWithRecoveryKey(upgraded.Header, recovery);
            var (reopened, migratedAgain) = VaultSerializer.Deserialize(VaultCrypto.OpenPayload(upgraded, dekFromRecovery));
            Assert.False(migratedAgain);
            Assert.Equal(["web", "legacy"], Assert.Single(reopened.Entries).Tags);
        }
    }

    [Fact]
    public void Payload_with_newer_schema_is_rejected()
    {
        var json = System.Text.Encoding.UTF8.GetBytes($$"""{"schemaVersion":{{VaultData.CurrentSchemaVersion + 1}},"entries":[]}""");

        Assert.Throws<UnsupportedVaultVersionException>(() => VaultSerializer.Deserialize(json));
    }

    [Fact]
    public void Migrator_converts_comma_separated_tags()
    {
        var root = JsonNode.Parse("""{"schemaVersion":1,"entries":[{"tags":" a,b ,, c "},{"tags":["x"]}]}""")!.AsObject();

        Assert.True(PayloadMigrator.Migrate(root));

        Assert.Equal(2, root["schemaVersion"]!.GetValue<int>());
        Assert.Equal("""["a","b","c"]""", root["entries"]![0]!["tags"]!.ToJsonString());
        Assert.Equal("""["x"]""", root["entries"]![1]!["tags"]!.ToJsonString());
    }

    [Fact]
    public void Current_schema_round_trips_without_migration()
    {
        var data = new VaultData
        {
            Entries =
            [
                new Entry
                {
                    Name = "db",
                    Tags = ["sql"],
                    CustomFields = [new CustomField { Label = "token", Value = "v", IsSecret = true }],
                },
            ],
        };

        var (copy, migrated) = VaultSerializer.Deserialize(VaultSerializer.Serialize(data));

        Assert.False(migrated);
        var entry = Assert.Single(copy.Entries);
        Assert.Equal("db", entry.Name);
        Assert.True(Assert.Single(entry.CustomFields).IsSecret);
    }

    [Fact]
    public void Malformed_payload_error_does_not_echo_content()
    {
        var ex = Assert.Throws<VaultFormatException>(() => VaultSerializer.Deserialize("""{"schemaVersion":2,"entries":"leaked-secret"}"""u8));

        Assert.DoesNotContain("leaked-secret", ex.Message, StringComparison.Ordinal);
        Assert.Null(ex.InnerException);
    }
}
