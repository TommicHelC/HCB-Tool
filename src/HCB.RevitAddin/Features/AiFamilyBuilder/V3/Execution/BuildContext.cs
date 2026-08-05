using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.AiFamilyBuilder.Models;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Stan wspoldzielony przez etapy budowy rodziny (parametry, referencje, raporty).
/// </summary>
public sealed class BuildContext
{
    public required Document Document { get; init; }

    public required Application Application { get; init; }

    public required AiFamilyBuildRequest Request { get; init; }

    public required IRevitTypeMapper TypeMapper { get; init; }

    public ParameterMap Parameters { get; } = new();

    public ReferenceMap References { get; } = new();

    public AiFamilyBuilderResult Result { get; } = new();

    public FlexTestReport FlexReport { get; } = new();
}
