using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Acorn.Core.Format.Migrations;
using Acorn.Core.Models;

namespace Acorn.Core.Format;

/// <summary>
/// JSON (de)serialization of the decrypted payload. Output only ever goes into
/// <see cref="VaultCrypto.Seal"/>; it is never written to disk or logs in plaintext.
/// </summary>
public static class VaultSerializer
{
    /// <summary>Returns UTF-8 JSON. The caller should zero the array after encrypting it.</summary>
    public static byte[] Serialize(VaultData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return JsonSerializer.SerializeToUtf8Bytes(data, VaultJsonContext.Default.VaultData);
    }

    /// <summary>Deserializes the payload, migrating older schemas first. <c>Migrated</c> is true when the data changed shape.</summary>
    public static (VaultData Data, bool Migrated) Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var probe = JsonSerializer.Deserialize(utf8Json, VaultJsonContext.Default.SchemaProbe);
            var schema = probe?.SchemaVersion ?? 1;
            VaultData? data;
            var migrated = false;

            if (schema == VaultData.CurrentSchemaVersion)
            {
                data = JsonSerializer.Deserialize(utf8Json, VaultJsonContext.Default.VaultData);
            }
            else
            {
                var root = JsonNode.Parse(utf8Json) as JsonObject
                    ?? throw new VaultFormatException("The vault data is malformed.");
                migrated = PayloadMigrator.Migrate(root);
                data = root.Deserialize(VaultJsonContext.Default.VaultData);
            }

            if (data is null)
            {
                throw new VaultFormatException("The vault data is malformed.");
            }
            Normalize(data);
            return (data, migrated);
        }
        catch (JsonException)
        {
            // Never surface JsonException: its message can quote fragments of the decrypted payload.
            throw new VaultFormatException("The vault data is malformed.");
        }
        catch (InvalidOperationException)
        {
            throw new VaultFormatException("The vault data is malformed.");
        }
        catch (FormatException)
        {
            throw new VaultFormatException("The vault data is malformed.");
        }
    }

    private static void Normalize(VaultData data)
    {
        data.Entries ??= [];
        data.Settings ??= new VaultSettings();
        data.Settings.Normalize();
        foreach (var entry in data.Entries)
        {
            entry.Tags ??= [];
            entry.CustomFields ??= [];
            entry.Type = EntryTypes.IsValid(entry.Type) ? entry.Type : EntryTypes.Server;
            entry.Name ??= "";
            entry.Env ??= "";
            entry.Host ??= "";
            entry.Username ??= "";
            entry.Password ??= "";
            entry.SshKeyPath ??= "";
            entry.Notes ??= "";
        }
    }
}

internal sealed class SchemaProbe
{
    public int? SchemaVersion { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VaultData))]
[JsonSerializable(typeof(SchemaProbe))]
internal sealed partial class VaultJsonContext : JsonSerializerContext;
