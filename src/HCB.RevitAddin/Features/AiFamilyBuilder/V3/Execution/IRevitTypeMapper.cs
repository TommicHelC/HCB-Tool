using Autodesk.Revit.DB;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Mapuje logiczne nazwy z JSON na identyfikatory Revit API.
/// Logika wykonawcza nie powinna opierac sie bezposrednio na surowych stringach.
/// </summary>
public interface IRevitTypeMapper
{
    ForgeTypeId MapParameterGroup(string? group);

    ForgeTypeId MapSpecType(string? dataType);

    /// <summary>Konwertuje wartosc wejsciowa (mm, m2, m3, stopnie) na jednostki wewnetrzne Revita.</summary>
    double ConvertToInternalValue(ForgeTypeId? specTypeId, double sourceValue);
}
