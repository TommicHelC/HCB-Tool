using Autodesk.Revit.DB;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Domyslny mapper typow Revit. Tablice mapowania pochodza z dotychczasowego
/// <c>AiFamilyBuilderService</c> i zachowuja jego zachowanie (parametry/typy dzialaja jak dotad).
/// </summary>
public sealed class RevitTypeMapper : IRevitTypeMapper
{
    public ForgeTypeId MapParameterGroup(string? group)
    {
        return group?.Trim().ToLowerInvariant() switch
        {
            "data" => GroupTypeId.Data,
            "text" => GroupTypeId.Text,
            "identitydata" => GroupTypeId.IdentityData,
            "construction" => GroupTypeId.Construction,
            "geometry" => GroupTypeId.Geometry,
            "materials" => GroupTypeId.Materials,
            "mechanical" => GroupTypeId.Mechanical,
            "electrical" => GroupTypeId.Electrical,
            "plumbing" => GroupTypeId.Plumbing,
            _ => GroupTypeId.Data
        };
    }

    public ForgeTypeId MapSpecType(string? dataType)
    {
        return dataType?.Trim().ToLowerInvariant() switch
        {
            "text" => SpecTypeId.String.Text,
            "yesno" => SpecTypeId.Boolean.YesNo,
            "integer" => SpecTypeId.Int.Integer,
            "number" => SpecTypeId.Number,
            "length" => SpecTypeId.Length,
            "area" => SpecTypeId.Area,
            "volume" => SpecTypeId.Volume,
            "angle" => SpecTypeId.Angle,
            "material" => SpecTypeId.Reference.Material,
            _ => SpecTypeId.String.Text
        };
    }

    public double ConvertToInternalValue(ForgeTypeId? specTypeId, double sourceValue)
    {
        if (specTypeId == null)
        {
            return sourceValue;
        }

        if (specTypeId == SpecTypeId.Length)
        {
            return UnitUtils.ConvertToInternalUnits(sourceValue, UnitTypeId.Millimeters);
        }

        if (specTypeId == SpecTypeId.Area)
        {
            return UnitUtils.ConvertToInternalUnits(sourceValue, UnitTypeId.SquareMeters);
        }

        if (specTypeId == SpecTypeId.Volume)
        {
            return UnitUtils.ConvertToInternalUnits(sourceValue, UnitTypeId.CubicMeters);
        }

        if (specTypeId == SpecTypeId.Angle)
        {
            return UnitUtils.ConvertToInternalUnits(sourceValue, UnitTypeId.Degrees);
        }

        return sourceValue;
    }
}
