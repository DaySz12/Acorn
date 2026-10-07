using System.Text.Json.Nodes;
using Acorn.Core.Models;

namespace Acorn.Core.Format.Migrations;

/// <summary>
/// Applies payload migrations in order until the JSON reaches <see cref="VaultData.CurrentSchemaVersion"/>.
/// Container-level changes (header layout) are handled by the versioned readers in <see cref="VaultCodec"/>.
/// </summary>
public static class PayloadMigrator
{
    private static readonly IPayloadMigration[] Migrations = [new Schema1To2()];

    /// <summary>Returns true when the JSON was changed.</summary>
    public static bool Migrate(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var version = root["schemaVersion"]?.GetValue<int>() ?? 1;
        if (version > VaultData.CurrentSchemaVersion)
        {
            throw new UnsupportedVaultVersionException(version);
        }
        if (version < 1)
        {
            throw new VaultFormatException("The vault data has an invalid schema version.");
        }

        var start = version;
        while (version < VaultData.CurrentSchemaVersion)
        {
            var migration = Migrations.SingleOrDefault(m => m.FromSchemaVersion == version)
                ?? throw new VaultFormatException("No migration is available for this vault data version.");
            migration.Apply(root);
            version++;
            root["schemaVersion"] = version;
        }
        return version != start;
    }
}
