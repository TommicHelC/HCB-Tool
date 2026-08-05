using System.Text.Json.Serialization;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

/// <summary>
/// Element szkieletu sterujacego rodziny (reference plane / level).
/// </summary>
public sealed class ReferenceSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>plane | level</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "plane";

    /// <summary>vertical | horizontal</summary>
    [JsonPropertyName("orientation")]
    public string? Orientation { get; init; }

    /// <summary>center | control | base | connector_anchor</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    /// <summary>Wartosc wlasciwosci Revita "Is Reference" (np. Weak, Strong, Left, Center Left/Right).</summary>
    [JsonPropertyName("is_reference")]
    public string? IsReference { get; init; }

    [JsonPropertyName("defines_origin")]
    public bool DefinesOrigin { get; init; }

    [JsonPropertyName("pinned")]
    public bool Pinned { get; init; }

    [JsonPropertyName("offset")]
    public double Offset { get; init; }
}

/// <summary>
/// Wiazanie miedzy referencjami: wymiar sterujacy lub relacja rownosci/centrowania.
/// </summary>
public sealed class ConstraintSpec
{
    /// <summary>dimension | equality</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Para referencji dla wymiaru.</summary>
    [JsonPropertyName("between")]
    public List<string> Between { get; init; } = [];

    /// <summary>Lancuch referencji dla relacji rownosci.</summary>
    [JsonPropertyName("chain")]
    public List<string> Chain { get; init; } = [];

    /// <summary>Parametr przypisany do wymiaru (dla kind = dimension).</summary>
    [JsonPropertyName("parameter")]
    public string? Parameter { get; init; }
}
