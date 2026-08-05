using System.Text.Json.Serialization;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

/// <summary>
/// Konektor MEP. Dla rodzin bioracych udzial w systemie jest warstwa funkcjonalna, nie ozdobna.
/// </summary>
public sealed class ConnectorSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>hvac | piping | electrical</summary>
    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;

    [JsonPropertyName("system_type")]
    public string? SystemType { get; init; }

    /// <summary>rectangular | round | oval</summary>
    [JsonPropertyName("shape")]
    public string Shape { get; init; } = string.Empty;

    [JsonPropertyName("host")]
    public ConnectorHostSpec? Host { get; init; }

    [JsonPropertyName("size")]
    public ConnectorSizeSpec? Size { get; init; }

    [JsonPropertyName("origin")]
    public ConnectorOriginSpec? Origin { get; init; }

    /// <summary>in | out | bidirectional</summary>
    [JsonPropertyName("direction")]
    public string Direction { get; init; } = string.Empty;

    [JsonPropertyName("is_primary")]
    public bool IsPrimary { get; init; }

    [JsonPropertyName("linked_to")]
    public string? LinkedTo { get; init; }
}

public sealed class ConnectorHostSpec
{
    /// <summary>Nazwa geometrii, ktorej scianka hostuje konektor.</summary>
    [JsonPropertyName("geometry")]
    public string? Geometry { get; init; }

    /// <summary>face | top | bottom | left | right | front | back</summary>
    [JsonPropertyName("face")]
    public string? Face { get; init; }

    /// <summary>Nazwa work plane / reference do hostowania, gdy brak sciany geometrii.</summary>
    [JsonPropertyName("work_plane")]
    public string? WorkPlane { get; init; }
}

public sealed class ConnectorSizeSpec
{
    [JsonPropertyName("width")]
    public ValueRefSpec? Width { get; init; }

    [JsonPropertyName("height")]
    public ValueRefSpec? Height { get; init; }

    [JsonPropertyName("diameter")]
    public ValueRefSpec? Diameter { get; init; }
}

public sealed class ConnectorOriginSpec
{
    /// <summary>Nazwa referencji wyznaczajacej osadzenie konektora.</summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; init; }

    [JsonPropertyName("x")]
    public ValueRefSpec? X { get; init; }

    [JsonPropertyName("y")]
    public ValueRefSpec? Y { get; init; }

    [JsonPropertyName("z")]
    public ValueRefSpec? Z { get; init; }
}
