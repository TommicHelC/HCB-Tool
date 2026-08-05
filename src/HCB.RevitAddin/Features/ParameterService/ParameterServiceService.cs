using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.ParameterService.Models;

namespace HCB.RevitAddin.Features.ParameterService;

public sealed class ParameterServiceService
{
    private const string ParameterServiceAccountId = "a7bc338b-92a2-4e70-8467-56b4e0bd89ea";
    private const string ParameterVersion = "1.0.0";

    private static readonly IReadOnlyList<PredefinedParameterServiceDefinition> PredefinedParameters =
    [
        new() { DisplayName = "HC_Adres", SchemaId = "fdb2d4df-f4ef-4824-ad9d-c2ab4a4fca84" },
        new() { DisplayName = "HC_Duct_System", SchemaId = "5840b3bf-36dc-462e-a342-d9bd0b41f43e" },
        new() { DisplayName = "HC_Piping_System", SchemaId = "5cd45cc0-4741-494f-bbf8-a1f7eaffbc9e" },
        new() { DisplayName = "HC_Koszt", SchemaId = "a5501b97-341c-4c2b-b8a0-85a2b952f03a" },
        new() { DisplayName = "HC_Cena_Jednostkowa", SchemaId = "b9c1822d-9c18-48b6-8d01-45d7f09cb48e" },
        new() { DisplayName = "HC_Order_Length", SchemaId = "7cc0f428-f3c2-417f-b0a8-464776100642" },
        new() { DisplayName = "HC_Projekt", SchemaId = "4f27a281-52e9-4d2b-b5e2-53ce979de57d" },
        new() { DisplayName = "HC_Area", SchemaId = "8bcbf51c-c749-41f2-977b-783c7c1ca8d7" },
        new() { DisplayName = "HC_Masa", SchemaId = "01d53840-5180-4fd6-b11a-e1c482983bc8" },
        new() { DisplayName = "HC_Do_Zamowienia", SchemaId = "d8d812f6-4f6b-40da-b79c-17dadd567c9a" },
        new() { DisplayName = "HC_Etap", SchemaId = "3a9940d2-a70a-4f22-a76a-24bef19bfe0f" },
        new() { DisplayName = "HC_Branza", SchemaId = "a4e69790-82e0-4f25-a276-746b87dc6433" },
        new() { DisplayName = "HC_IPAddress", SchemaId = "e8476e61-ebdd-4b76-9b0f-c9cb586b751e" },
        new() { DisplayName = "HC_Moc_Znamionowa_S", SchemaId = "8f86af69-9396-4344-8c4a-4bdffe40500f" },
        new() { DisplayName = "HC_Prad_I", SchemaId = "d27cc3e3-2848-445c-ab5d-9105278c85a1" },
        new() { DisplayName = "HC_Napiecie_U", SchemaId = "ac9d8b89-0e63-4642-be11-3d7cac55bea0" },
        new() { DisplayName = "HC_Zasilanie", SchemaId = "e2ee5ad8-e38a-4277-a411-9d30d0282275" },
        new() { DisplayName = "HC_Moc_P", SchemaId = "9f8ddbee-3abe-4047-94d7-c51c437c64cd" },
        new() { DisplayName = "HC_Moc_Chlodnicza", SchemaId = "56a88f26-ff55-492c-905f-6024f464c504" },
        new() { DisplayName = "HC_Moc_Grzewcza", SchemaId = "e481d2c6-c34b-48bd-a7a2-0f4a4633d42f" },
        new() { DisplayName = "HC_Kat", SchemaId = "8aee4270-f331-4f4b-9c07-caac5d0f35d4" },
        new() { DisplayName = "HC_System", SchemaId = "a89911e7-64fa-4187-9b4f-5e1733d0f79d" }
    ];

    private static readonly Regex ParameterTypeIdPattern = new(
        @"^parameters\.(?<account>[0-9a-fA-F]{32}):(?<schema>[0-9a-fA-F]{32})-(?<version>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public bool HasConfiguredParameters => PredefinedParameters.Count > 0;

    public bool HasAccountIdConfigured => !string.IsNullOrWhiteSpace(ParameterServiceAccountId);

    public ParameterServiceResolveResult ResolveConfigured(Document document)
    {
        ParameterServiceResolveResult result = new();
        Dictionary<string, ParameterServiceDescriptor> descriptors = new(StringComparer.OrdinalIgnoreCase);

        foreach (PredefinedParameterServiceDefinition definition in PredefinedParameters)
        {
            string parameterTypeIdText = BuildParameterTypeId(definition.SchemaId);
            if (string.IsNullOrWhiteSpace(parameterTypeIdText))
            {
                result.Errors.Add($"Pominieto pusta konfiguracje parametru: {definition.DisplayName}");
                continue;
            }

            if (descriptors.ContainsKey(parameterTypeIdText))
            {
                result.Errors.Add($"Pominieto duplikat konfiguracji: {definition.DisplayName}");
                continue;
            }

            try
            {
                ForgeTypeId parameterTypeId = new(parameterTypeIdText);
                ParameterDownloadOptions options = ParameterUtils.DownloadParameterOptions(parameterTypeId);
                Guid? schemaGuid = TryExtractSchemaGuid(parameterTypeIdText);
                SharedParameterElement? existingParameter = schemaGuid.HasValue
                    ? SharedParameterElement.Lookup(document, schemaGuid.Value)
                    : null;

                descriptors.Add(
                    parameterTypeIdText,
                    new ParameterServiceDescriptor
                    {
                        ParameterTypeId = parameterTypeIdText,
                        DisplayName = ResolveDisplayName(definition.DisplayName, existingParameter?.Name, parameterTypeIdText),
                        GroupLabel = ResolveGroupLabel(options.GetGroupTypeId()),
                        BindingLabel = options.IsInstance ? "Instance" : "Type",
                        CategoriesSummary = ResolveCategoriesSummary(document, options.GetCategories()),
                        ExistsInDocument = existingParameter != null,
                        Visible = options.Visible
                    });
            }
            catch (Exception exception)
            {
                result.Errors.Add($"{definition.DisplayName}: {exception.Message}");
            }
        }

        result.Parameters.AddRange(
            descriptors.Values
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase));

        return result;
    }

    public IReadOnlyList<string> GetConfiguredParameterNames()
    {
        return PredefinedParameters
            .Select(parameter => parameter.DisplayName)
            .ToList();
    }

    public ParameterServiceImportResult Import(Document document, IEnumerable<string> parameterTypeIds)
    {
        ParameterServiceImportResult result = new();
        List<string> selectedIds = parameterTypeIds
            .Where(parameterTypeId => !string.IsNullOrWhiteSpace(parameterTypeId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        result.SelectedCount = selectedIds.Count;

        foreach (string parameterTypeIdText in selectedIds)
        {
            Guid? schemaGuid = TryExtractSchemaGuid(parameterTypeIdText);
            SharedParameterElement? existingParameter = schemaGuid.HasValue
                ? SharedParameterElement.Lookup(document, schemaGuid.Value)
                : null;

            if (existingParameter != null)
            {
                result.SkippedCount++;
                result.Messages.Add($"Pominieto juz zaladowany parametr: {existingParameter.Name}");
                continue;
            }

            using Transaction transaction = new(document, "Import Parameter Service Parameter");

            try
            {
                transaction.Start();

                ForgeTypeId parameterTypeId = new(parameterTypeIdText);
                ParameterDownloadOptions options = ParameterUtils.DownloadParameterOptions(parameterTypeId);
                ParameterUtils.DownloadParameter(document, options, parameterTypeId);

                transaction.Commit();

                SharedParameterElement? importedParameter = schemaGuid.HasValue
                    ? SharedParameterElement.Lookup(document, schemaGuid.Value)
                    : null;

                result.ImportedCount++;
                result.Messages.Add($"Dodano parametr: {importedParameter?.Name ?? parameterTypeIdText}");
            }
            catch (Exception exception)
            {
                if (transaction.GetStatus() == TransactionStatus.Started)
                {
                    transaction.RollBack();
                }

                result.FailedCount++;
                result.Messages.Add($"Blad importu {parameterTypeIdText}: {exception.Message}");
            }
        }

        return result;
    }

    private static string ResolveDisplayName(string? alias, string? existingName, string parameterTypeId)
    {
        if (!string.IsNullOrWhiteSpace(alias))
        {
            return alias;
        }

        if (!string.IsNullOrWhiteSpace(existingName))
        {
            return existingName;
        }

        return parameterTypeId;
    }

    private static string ResolveGroupLabel(ForgeTypeId groupTypeId)
    {
        try
        {
            return LabelUtils.GetLabelForGroup(groupTypeId);
        }
        catch
        {
            return string.IsNullOrWhiteSpace(groupTypeId.TypeId) ? "Other" : groupTypeId.TypeId;
        }
    }

    private static string ResolveCategoriesSummary(Document document, ISet<ElementId>? categoryIds)
    {
        if (categoryIds == null || categoryIds.Count == 0)
        {
            return "Brak kategorii";
        }

        List<string> names = categoryIds
            .Select(categoryId => Category.GetCategory(document, categoryId)?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList()!;

        if (names.Count == 0)
        {
            return "Brak kategorii";
        }

        if (names.Count <= 4)
        {
            return string.Join(", ", names);
        }

        return $"{string.Join(", ", names.Take(4))} +{names.Count - 4}";
    }

    private static Guid? TryExtractSchemaGuid(string parameterTypeId)
    {
        Match match = ParameterTypeIdPattern.Match(parameterTypeId);
        if (!match.Success)
        {
            return null;
        }

        string schemaId = match.Groups["schema"].Value;
        return Guid.TryParseExact(schemaId, "N", out Guid parsedGuid) ? parsedGuid : null;
    }

    private static string BuildParameterTypeId(string schemaId)
    {
        if (string.IsNullOrWhiteSpace(ParameterServiceAccountId) || string.IsNullOrWhiteSpace(schemaId))
        {
            return string.Empty;
        }

        string normalizedAccountId = NormalizeGuidLikeValue(ParameterServiceAccountId);
        string normalizedSchemaId = NormalizeGuidLikeValue(schemaId);
        return $"parameters.{normalizedAccountId}:{normalizedSchemaId}-{ParameterVersion}";
    }

    private static string NormalizeGuidLikeValue(string value)
    {
        return value.Replace("-", string.Empty).Trim();
    }
}
