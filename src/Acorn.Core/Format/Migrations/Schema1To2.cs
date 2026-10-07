using System.Text.Json.Nodes;

namespace Acorn.Core.Format.Migrations;

/// <summary>Schema 1 stored tags as one comma-separated string; schema 2 stores a list.</summary>
internal sealed class Schema1To2 : IPayloadMigration
{
    public int FromSchemaVersion => 1;

    public void Apply(JsonObject root)
    {
        if (root["entries"] is not JsonArray entries)
        {
            return;
        }

        foreach (var entry in entries.OfType<JsonObject>())
        {
            if (entry["tags"] is JsonValue value && value.TryGetValue<string>(out var text))
            {
                var tags = new JsonArray();
                foreach (var tag in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    tags.Add(tag);
                }
                entry["tags"] = tags;
            }
        }
    }
}
