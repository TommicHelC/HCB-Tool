using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

/// <summary>
/// Przypadek flex testu: zmiana parametrow + Regenerate + zebranie bledow.
/// Czesc kontraktu wykonawczego, nie dodatek.
/// </summary>
public sealed class FlexTestSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Nazwa istniejacego typu do aktywacji (alternatywa dla values).</summary>
    [JsonPropertyName("type_name")]
    public string? TypeName { get; init; }

    /// <summary>Zestaw wartosci parametrow do ustawienia w tescie.</summary>
    [JsonPropertyName("values")]
    public Dictionary<string, JsonElement> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Flagi sterujace walidacja i flex testami.
/// </summary>
public sealed class ValidationSpec
{
    [JsonPropertyName("run_framework_flex")]
    public bool RunFrameworkFlex { get; init; } = true;

    [JsonPropertyName("run_final_flex")]
    public bool RunFinalFlex { get; init; } = true;

    [JsonPropertyName("require_connectors")]
    public bool RequireConnectors { get; init; }
}
