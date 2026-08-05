using System;
using System.Text.Json;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3;

/// <summary>
/// Wykrywa wersje schematu na podstawie pola <c>schema</c>, aby skierowac JSON na wlasciwa sciezke (v2 vs v3).
/// </summary>
public static class AiFamilyBuilderSchemaDetector
{
    public static string? ReadSchema(string? jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(jsonText);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("schema", out JsonElement schema) &&
                schema.ValueKind == JsonValueKind.String)
            {
                return schema.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static bool IsV3(string? jsonText) =>
        string.Equals(ReadSchema(jsonText), AiFamilyBuildRequest.SchemaId, StringComparison.OrdinalIgnoreCase);
}
