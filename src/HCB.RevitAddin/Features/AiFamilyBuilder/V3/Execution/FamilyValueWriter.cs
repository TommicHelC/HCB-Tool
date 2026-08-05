using System;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Wspoldzielony zapis wartosci parametru rodziny z <see cref="JsonElement"/>.
/// Logika portowana 1:1 z dotychczasowej (potwierdzonej) sciezki w <c>V3ParameterService</c>;
/// wydzielona, aby flex testy ustawialy wartosci dokladnie tak samo jak ApplyTypeValues.
/// </summary>
internal static class FamilyValueWriter
{
    public static bool TrySet(FamilyManager familyManager, FamilyParameter parameter, JsonElement valueElement, IRevitTypeMapper mapper)
    {
        try
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    familyManager.Set(parameter, valueElement.ValueKind == JsonValueKind.String
                        ? valueElement.GetString() ?? string.Empty
                        : valueElement.ToString());
                    return true;

                case StorageType.Integer:
                    if (valueElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        familyManager.Set(parameter, valueElement.GetBoolean() ? 1 : 0);
                        return true;
                    }

                    if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetInt32(out int intValue))
                    {
                        familyManager.Set(parameter, intValue);
                        return true;
                    }

                    break;

                case StorageType.Double:
                    if (valueElement.ValueKind != JsonValueKind.Number)
                    {
                        break;
                    }

                    double internalValue = mapper.ConvertToInternalValue(parameter.Definition?.GetDataType(), valueElement.GetDouble());
                    familyManager.Set(parameter, internalValue);
                    return true;

                case StorageType.ElementId:
                    return false;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
