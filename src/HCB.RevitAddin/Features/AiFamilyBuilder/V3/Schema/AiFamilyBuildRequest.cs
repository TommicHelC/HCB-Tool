using System.Text.Json.Serialization;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

/// <summary>
/// Root DTO dla kontraktu wejsciowego <c>hcb.ai_family_builder.v3</c>.
/// JSON opisuje intencje i strukture sterowania rodzina; logika Revita zyje w builderach domenowych.
/// </summary>
public sealed class AiFamilyBuildRequest
{
    public const string SchemaId = "hcb.ai_family_builder.v3";

    [JsonPropertyName("schema")]
    public string Schema { get; init; } = string.Empty;

    [JsonPropertyName("meta")]
    public MetaSpec Meta { get; init; } = new();

    [JsonPropertyName("builder")]
    public BuilderSpec Builder { get; init; } = new();

    [JsonPropertyName("parameters")]
    public List<ParameterSpec> Parameters { get; init; } = [];

    [JsonPropertyName("parameter_rules")]
    public List<ParameterRuleSpec> ParameterRules { get; init; } = [];

    [JsonPropertyName("types")]
    public List<TypeSpec> Types { get; init; } = [];

    [JsonPropertyName("references")]
    public List<ReferenceSpec> References { get; init; } = [];

    [JsonPropertyName("constraints")]
    public List<ConstraintSpec> Constraints { get; init; } = [];

    [JsonPropertyName("geometry")]
    public List<GeometrySpec> Geometry { get; init; } = [];

    [JsonPropertyName("connectors")]
    public List<ConnectorSpec> Connectors { get; init; } = [];

    [JsonPropertyName("flex_tests")]
    public List<FlexTestSpec> FlexTests { get; init; } = [];

    [JsonPropertyName("validation")]
    public ValidationSpec Validation { get; init; } = new();
}

public sealed class MetaSpec
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "3.0";

    [JsonPropertyName("family_name")]
    public string FamilyName { get; init; } = string.Empty;

    [JsonPropertyName("category_hint")]
    public string CategoryHint { get; init; } = string.Empty;

    [JsonPropertyName("template_hint")]
    public string TemplateHint { get; init; } = string.Empty;
}

/// <summary>
/// Sekcja obowiazkowa. Wybiera builder domenowy, ktory wie, jak poprawnie zbudowac rodzine danej klasy.
/// </summary>
public sealed class BuilderSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;
}
