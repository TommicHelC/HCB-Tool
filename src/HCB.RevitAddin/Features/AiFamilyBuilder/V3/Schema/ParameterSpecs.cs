using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

/// <summary>
/// Deklaracja parametru rodziny lub shared parameter.
/// </summary>
public sealed class ParameterSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>family | shared</summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = "family";

    /// <summary>length | number | text | yesno | integer | area | volume | angle | material</summary>
    [JsonPropertyName("data_type")]
    public string DataType { get; init; } = "text";

    /// <summary>Geometry | Mechanical | Data ...</summary>
    [JsonPropertyName("group")]
    public string Group { get; init; } = "Data";

    /// <summary>type | instance</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = "type";

    [JsonPropertyName("reporting")]
    public bool Reporting { get; init; }

    [JsonPropertyName("formula")]
    public string? Formula { get; init; }
}

/// <summary>
/// Reguly walidacyjne i zrodlo testow skrajnych dla parametru.
/// </summary>
public sealed class ParameterRuleSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("min")]
    public double? Min { get; init; }

    [JsonPropertyName("max")]
    public double? Max { get; init; }

    [JsonPropertyName("step")]
    public double? Step { get; init; }
}

/// <summary>
/// Typ rodziny i wartosci jego parametrow.
/// </summary>
public sealed class TypeSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("values")]
    public Dictionary<string, JsonElement> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
