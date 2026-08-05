using System;
using Autodesk.Revit.DB;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Mapa logicznych nazw parametrow na obiekty <see cref="FamilyParameter"/>.
/// </summary>
public sealed class ParameterMap
{
    private readonly Dictionary<string, FamilyParameter> _parameters = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, FamilyParameter> Parameters => _parameters;

    public void Set(string name, FamilyParameter parameter) => _parameters[name] = parameter;

    public bool TryGet(string name, out FamilyParameter parameter) => _parameters.TryGetValue(name, out parameter!);

    public bool Contains(string name) => _parameters.ContainsKey(name);
}

/// <summary>
/// Mapa logicznych nazw referencji na elementy Revita (reference planes / levels).
/// </summary>
public sealed class ReferenceMap
{
    public Dictionary<string, ElementId> Planes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ElementId> Levels { get; } = new(StringComparer.OrdinalIgnoreCase);
}
