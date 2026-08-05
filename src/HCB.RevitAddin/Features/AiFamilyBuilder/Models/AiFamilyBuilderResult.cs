namespace HCB.RevitAddin.Features.AiFamilyBuilder.Models;

public sealed class AiFamilyBuilderResult
{
    public int AddedParametersCount { get; set; }

    public int SkippedParametersCount { get; set; }

    public int AddedTypesCount { get; set; }

    public int UpdatedValuesCount { get; set; }

    public int ExecutedGeometryOperationsCount { get; set; }

    public int SkippedGeometryOperationsCount { get; set; }

    public int CreatedConnectorsCount { get; set; }

    public int SkippedConnectorsCount { get; set; }

    public int CreatedReferencePlanesCount { get; set; }

    public int SkippedReferencePlanesCount { get; set; }

    public int CreatedDimensionsCount { get; set; }

    public int SkippedDimensionsCount { get; set; }

    public List<string> Messages { get; } = [];
}
