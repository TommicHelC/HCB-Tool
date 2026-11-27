using System;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.ScheduleExcel.Models;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>
/// Finds a schedule field's parameter on an element by id (built-in, shared GUID or project parameter
/// definition) - never by scanning element.Parameters (see REVIT_MCP_ERRORS.md). Shared by export and sync.
/// </summary>
internal sealed class ScheduleExcelParameterLookup
{
    private readonly BuiltInParameter _builtIn;
    private readonly Definition? _definition;

    private ScheduleExcelParameterLookup(BuiltInParameter builtIn, Guid? guid, Definition? definition)
    {
        _builtIn = builtIn;
        Guid = guid;
        _definition = definition;
    }

    public Guid? Guid { get; }

    public static ScheduleExcelParameterLookup? Create(Document document, ElementId? parameterId)
    {
        if (parameterId == null || parameterId == ElementId.InvalidElementId)
        {
            return null;
        }

        if (parameterId.Value < 0)
        {
            return new ScheduleExcelParameterLookup((BuiltInParameter)parameterId.Value, null, null);
        }

        return document.GetElement(parameterId) switch
        {
            SharedParameterElement shared => new ScheduleExcelParameterLookup(BuiltInParameter.INVALID, shared.GuidValue, null),
            ParameterElement parameterElement => new ScheduleExcelParameterLookup(BuiltInParameter.INVALID, null, parameterElement.GetDefinition()),
            _ => null
        };
    }

    public Parameter? Get(Element? element)
    {
        if (element == null)
        {
            return null;
        }

        if (_builtIn != BuiltInParameter.INVALID)
        {
            return element.get_Parameter(_builtIn);
        }

        if (Guid.HasValue)
        {
            return element.get_Parameter(Guid.Value);
        }

        return _definition == null ? null : element.get_Parameter(_definition);
    }

    /// <summary>The element itself, or its type for type parameters.</summary>
    public static Element? GetTarget(Document document, Element element, bool isType)
    {
        if (!isType)
        {
            return element;
        }

        ElementId typeId = element.GetTypeId();
        return typeId == ElementId.InvalidElementId ? null : document.GetElement(typeId);
    }

    /// <summary>Parameter value as exported: numbers in display units, Yes/No as bool, references as display text.</summary>
    public static ScheduleExcelValue ReadValue(Parameter? parameter, ScheduleExcelValueKind kind, ForgeTypeId? unit)
    {
        if (parameter == null)
        {
            return new ScheduleExcelValue(null, false, true);
        }

        bool readOnly = parameter.IsReadOnly;
        if (!parameter.HasValue && kind != ScheduleExcelValueKind.Text)
        {
            return new ScheduleExcelValue(null, false, readOnly);
        }

        object? value = kind switch
        {
            ScheduleExcelValueKind.Number => unit == null
                ? parameter.AsDouble()
                : UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), unit),
            ScheduleExcelValueKind.Integer => (double)parameter.AsInteger(),
            ScheduleExcelValueKind.YesNo => parameter.AsInteger() == 1,
            ScheduleExcelValueKind.ElementId => parameter.AsValueString(),
            _ => parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString()
        };

        if (value is string text && text.Length == 0)
        {
            value = null;
        }

        return new ScheduleExcelValue(value, false, readOnly);
    }
}
