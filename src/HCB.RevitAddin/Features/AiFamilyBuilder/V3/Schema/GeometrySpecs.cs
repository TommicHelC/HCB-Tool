using System;
using System.Text.Json.Serialization;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

/// <summary>
/// Operacja geometryczna wykonywana na zwalidowanym frameworku referencyjnym.
/// </summary>
public sealed class GeometrySpec
{
    /// <summary>rectangular_prism | void_rectangular_prism | circular_extrusion ...</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("solid")]
    public bool Solid { get; init; } = true;

    [JsonPropertyName("placement")]
    public PlacementSpec? Placement { get; init; }

    [JsonPropertyName("profile_refs")]
    public ProfileRefsSpec? ProfileRefs { get; init; }

    [JsonPropertyName("width")]
    public ValueRefSpec? Width { get; init; }

    [JsonPropertyName("height")]
    public ValueRefSpec? Height { get; init; }

    [JsonPropertyName("depth")]
    public ValueRefSpec? Depth { get; init; }

    [JsonPropertyName("radius")]
    public ValueRefSpec? Radius { get; init; }

    [JsonPropertyName("offsets")]
    public Dictionary<string, ValueRefSpec>? Offsets { get; init; }

    [JsonPropertyName("lock_profile_to_refs")]
    public bool LockProfileToRefs { get; init; }

    [JsonPropertyName("lock_extrusion_end")]
    public bool LockExtrusionEnd { get; init; }
}

/// <summary>
/// Sposob osadzenia geometrii wzgledem frameworku.
/// </summary>
public sealed class PlacementSpec
{
    /// <summary>Nazwa work plane / reference, na ktorej powstaje szkic.</summary>
    [JsonPropertyName("work_plane")]
    public string? WorkPlane { get; init; }

    [JsonPropertyName("origin")]
    public string? Origin { get; init; }

    [JsonPropertyName("level")]
    public string? Level { get; init; }
}

/// <summary>
/// Referencje krawedzi profilu do reference planes szkieletu.
/// </summary>
public sealed class ProfileRefsSpec
{
    [JsonPropertyName("left")]
    public string? Left { get; init; }

    [JsonPropertyName("right")]
    public string? Right { get; init; }

    [JsonPropertyName("front")]
    public string? Front { get; init; }

    [JsonPropertyName("back")]
    public string? Back { get; init; }

    [JsonPropertyName("top")]
    public string? Top { get; init; }

    [JsonPropertyName("bottom")]
    public string? Bottom { get; init; }
}

/// <summary>
/// Wartosc liczbowa, referencja do parametru/referencji albo wyrazenie.
/// Dokladnie jedno z pol powinno byc wypelnione.
/// </summary>
public sealed class ValueRefSpec
{
    /// <summary>Nazwa parametru lub referencji.</summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; init; }

    /// <summary>Ograniczone wyrazenie: liczby, nazwy parametrow, + - * /, nawiasy.</summary>
    [JsonPropertyName("expr")]
    public string? Expr { get; init; }

    /// <summary>Stala wartosc liczbowa (mm dla dlugosci).</summary>
    [JsonPropertyName("value")]
    public double? Value { get; init; }

    [JsonIgnore]
    public bool HasRef => !string.IsNullOrWhiteSpace(Ref);

    [JsonIgnore]
    public bool HasExpr => !string.IsNullOrWhiteSpace(Expr);

    [JsonIgnore]
    public bool HasValue => Value.HasValue;

    [JsonIgnore]
    public bool IsEmpty => !HasRef && !HasExpr && !HasValue;
}
