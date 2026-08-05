using System;
using System.Text.Json;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Etap 2: tworzy/reuzywa parametry rodziny i shared, aplikuje formuly oraz wartosci typow.
/// Logika odwzorowuje sprawdzona sciezke z <c>AiFamilyBuilderService</c> (parametry/typy dzialaja jak dotad),
/// zaadaptowana do DTO v3 i <see cref="IRevitTypeMapper"/>.
/// </summary>
public sealed class V3ParameterService : IParameterService
{
    public void CreateOrReuse(BuildContext context)
    {
        FamilyManager familyManager = context.Document.FamilyManager;
        Dictionary<string, FamilyParameter> existing = familyManager.Parameters
            .Cast<FamilyParameter>()
            .Where(parameter => parameter.Definition != null && !string.IsNullOrWhiteSpace(parameter.Definition.Name))
            .GroupBy(parameter => parameter.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        bool needsShared = context.Request.Parameters.Any(parameter =>
            string.Equals(parameter.Source, "shared", StringComparison.OrdinalIgnoreCase));
        DefinitionFile? sharedFile = needsShared ? context.Application.OpenSharedParameterFile() : null;
        if (needsShared && sharedFile == null)
        {
            throw new InvalidOperationException("JSON wymaga shared parameters, ale Revit nie ma ustawionego pliku Shared Parameters.");
        }

        Dictionary<string, ExternalDefinition> sharedDefinitions = BuildSharedDefinitions(sharedFile);

        foreach (ParameterSpec spec in context.Request.Parameters)
        {
            if (string.IsNullOrWhiteSpace(spec.Name))
            {
                continue;
            }

            if (existing.TryGetValue(spec.Name, out FamilyParameter? existingParameter))
            {
                context.Parameters.Set(spec.Name, existingParameter);
                context.Result.SkippedParametersCount++;
                context.Result.Messages.Add($"Uzyto istniejacego parametru: {spec.Name}");
                continue;
            }

            ForgeTypeId group = context.TypeMapper.MapParameterGroup(spec.Group);
            bool isInstance = string.Equals(spec.Scope, "instance", StringComparison.OrdinalIgnoreCase);

            FamilyParameter parameter;
            if (string.Equals(spec.Source, "shared", StringComparison.OrdinalIgnoreCase))
            {
                if (!sharedDefinitions.TryGetValue(spec.Name, out ExternalDefinition? definition))
                {
                    throw new InvalidOperationException($"Nie znaleziono shared parameter wsrod dostepnych definicji: {spec.Name}");
                }

                parameter = familyManager.AddParameter(definition, group, isInstance);
            }
            else
            {
                ForgeTypeId specType = context.TypeMapper.MapSpecType(spec.DataType);
                parameter = familyManager.AddParameter(spec.Name, group, specType, isInstance);
            }

            context.Parameters.Set(spec.Name, parameter);
            existing[spec.Name] = parameter;
            context.Result.AddedParametersCount++;
            context.Result.Messages.Add($"Dodano parametr: {spec.Name}");
        }
    }

    public void ApplyFormulas(BuildContext context)
    {
        FamilyManager familyManager = context.Document.FamilyManager;
        foreach (ParameterSpec spec in context.Request.Parameters)
        {
            if (string.IsNullOrWhiteSpace(spec.Formula) || !context.Parameters.TryGet(spec.Name, out FamilyParameter parameter))
            {
                continue;
            }

            try
            {
                familyManager.SetFormula(parameter, spec.Formula);
                context.Result.Messages.Add($"Ustawiono formule: {spec.Name}");
            }
            catch (Exception ex)
            {
                context.Result.Messages.Add($"Nie udalo sie ustawic formuly dla {spec.Name}: {ex.Message}");
            }
        }
    }

    public void ApplyTypeValues(BuildContext context)
    {
        FamilyManager familyManager = context.Document.FamilyManager;
        HashSet<string> existingTypes = familyManager.Types
            .Cast<FamilyType>()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (familyManager.CurrentType == null && context.Request.Types.Count == 0)
        {
            familyManager.NewType("Default");
            context.Result.AddedTypesCount++;
            context.Result.Messages.Add("Dodano domyslny typ rodziny: Default");
            return;
        }

        foreach (TypeSpec typeSpec in context.Request.Types)
        {
            if (string.IsNullOrWhiteSpace(typeSpec.Name) || existingTypes.Contains(typeSpec.Name))
            {
                continue;
            }

            familyManager.NewType(typeSpec.Name);
            existingTypes.Add(typeSpec.Name);
            context.Result.AddedTypesCount++;
            context.Result.Messages.Add($"Dodano typ: {typeSpec.Name}");
        }

        if (context.Request.Types.Count == 0 || familyManager.CurrentType == null)
        {
            return;
        }

        FamilyType? originalType = familyManager.CurrentType;
        Dictionary<string, FamilyType> typeMap = familyManager.Types
            .Cast<FamilyType>()
            .ToDictionary(type => type.Name, StringComparer.OrdinalIgnoreCase);

        foreach (TypeSpec typeSpec in context.Request.Types)
        {
            if (!typeMap.TryGetValue(typeSpec.Name, out FamilyType? familyType))
            {
                continue;
            }

            familyManager.CurrentType = familyType;
            foreach ((string parameterName, JsonElement valueElement) in typeSpec.Values)
            {
                if (!context.Parameters.TryGet(parameterName, out FamilyParameter parameter))
                {
                    context.Result.Messages.Add($"Brak parametru do ustawienia wartosci: {parameterName}");
                    continue;
                }

                if (FamilyValueWriter.TrySet(familyManager, parameter, valueElement, context.TypeMapper))
                {
                    context.Result.UpdatedValuesCount++;
                }
                else
                {
                    context.Result.Messages.Add($"Nie udalo sie ustawic wartosci parametru: {parameterName} dla typu {typeSpec.Name}");
                }
            }
        }

        familyManager.CurrentType = originalType;
    }

    private static Dictionary<string, ExternalDefinition> BuildSharedDefinitions(DefinitionFile? file)
    {
        Dictionary<string, ExternalDefinition> map = new(StringComparer.OrdinalIgnoreCase);
        if (file == null)
        {
            return map;
        }

        foreach (DefinitionGroup group in file.Groups)
        {
            foreach (Definition definition in group.Definitions)
            {
                if (definition is ExternalDefinition externalDefinition && !map.ContainsKey(definition.Name))
                {
                    map[definition.Name] = externalDefinition;
                }
            }
        }

        return map;
    }
}
