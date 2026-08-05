using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.Models;

public sealed class FamilyDefinitionSpec
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = string.Empty;

    [JsonPropertyName("meta")]
    public FamilyMetaSpec Meta { get; init; } = new();

    [JsonPropertyName("parameters")]
    public List<FamilyParameterSpec> Parameters { get; init; } = [];

    [JsonPropertyName("types")]
    public List<FamilyTypeSpec> Types { get; init; } = [];

    [JsonPropertyName("reference_planes")]
    public List<ReferencePlaneSpec> ReferencePlanes { get; init; } = [];

    [JsonPropertyName("dimensions")]
    public List<DimensionConstraintSpec> Dimensions { get; init; } = [];

    [JsonPropertyName("constraints")]
    public List<DimensionConstraintSpec> Constraints { get; init; } = [];

    [JsonPropertyName("geometry")]
    public List<GeometryOperationSpec> Geometry { get; init; } = [];
}

public sealed class FamilyMetaSpec
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.1";

    [JsonPropertyName("family_name")]
    public string FamilyName { get; init; } = string.Empty;

    [JsonPropertyName("category_hint")]
    public string CategoryHint { get; init; } = string.Empty;

    [JsonPropertyName("template_hint")]
    public string TemplateHint { get; init; } = string.Empty;
}

public sealed class FamilyParameterSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "family";

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("spec_type")]
    public string SpecType { get; init; } = "text";

    [JsonPropertyName("data_type")]
    public string? DataType { get; init; }

    [JsonPropertyName("group")]
    public string Group { get; init; } = "Data";

    [JsonPropertyName("is_instance")]
    public bool IsInstance { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("formula")]
    public string? Formula { get; init; }
}

public sealed class FamilyTypeSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("values")]
    public Dictionary<string, JsonElement> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ReferencePlaneSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("view")]
    public string? View { get; init; }

    [JsonPropertyName("line_direction")]
    public string? LineDirection { get; init; }

    [JsonPropertyName("orientation")]
    public string Orientation { get; init; } = "vertical";

    [JsonPropertyName("offset")]
    public double Offset { get; init; }

    [JsonPropertyName("is_reference")]
    public bool IsReference { get; init; } = true;
}

public sealed class DimensionConstraintSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("view")]
    public string? View { get; init; }

    [JsonPropertyName("orientation")]
    public string Orientation { get; init; } = "horizontal";

    [JsonPropertyName("reference_a")]
    public string ReferenceA { get; init; } = string.Empty;

    [JsonPropertyName("reference_b")]
    public string ReferenceB { get; init; } = string.Empty;

    [JsonPropertyName("parameter")]
    public string Parameter { get; init; } = string.Empty;

    [JsonPropertyName("offset")]
    public double Offset { get; init; } = 100;
}

public sealed class GeometryOperationSpec
{
    [JsonPropertyName("op")]
    public string Op { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("solid")]
    public bool Solid { get; init; } = true;

    [JsonPropertyName("width")]
    public JsonElement Width { get; init; }

    [JsonPropertyName("height")]
    public JsonElement Height { get; init; }

    [JsonPropertyName("depth")]
    public JsonElement Depth { get; init; }

    [JsonPropertyName("radius")]
    public JsonElement Radius { get; init; }

    [JsonPropertyName("top_width")]
    public JsonElement TopWidth { get; init; }

    [JsonPropertyName("top_height")]
    public JsonElement TopHeight { get; init; }

    [JsonPropertyName("bottom_width")]
    public JsonElement BottomWidth { get; init; }

    [JsonPropertyName("bottom_height")]
    public JsonElement BottomHeight { get; init; }

    [JsonPropertyName("start_angle")]
    public double StartAngle { get; init; }

    [JsonPropertyName("end_angle")]
    public double EndAngle { get; init; } = 360;

    [JsonPropertyName("material_parameter")]
    public string? MaterialParameter { get; init; }
}
