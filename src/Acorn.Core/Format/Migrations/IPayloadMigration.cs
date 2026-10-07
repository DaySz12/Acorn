using System.Text.Json.Nodes;

namespace Acorn.Core.Format.Migrations;

/// <summary>Upgrades the decrypted payload JSON from <see cref="FromSchemaVersion"/> to the next version.</summary>
public interface IPayloadMigration
{
    int FromSchemaVersion { get; }

    void Apply(JsonObject root);
}
