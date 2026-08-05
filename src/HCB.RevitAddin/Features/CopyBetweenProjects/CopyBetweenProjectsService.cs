using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.CopyBetweenProjects.Models;

namespace HCB.RevitAddin.Features.CopyBetweenProjects;

public sealed class CopyBetweenProjectsService
{
    public IReadOnlyList<Document> GetAvailableProjectDocuments(Application application)
    {
        return application.Documents
            .Cast<Document>()
            .Where(document => !document.IsFamilyDocument && !document.IsLinked)
            .OrderBy(document => document.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<CopyBetweenProjectsItem> GetAvailableItems(Document sourceDocument)
    {
        List<CopyBetweenProjectsItem> items = [];

        items.AddRange(GetSystemTypeItems(sourceDocument, BuiltInCategory.OST_Levels, "Level Type"));
        items.AddRange(GetSystemTypeItems(sourceDocument, BuiltInCategory.OST_Grids, "Grid Type"));
        items.AddRange(GetFamilyItems(sourceDocument));
        items.AddRange(GetFamilyTypeItems(sourceDocument));

        return items
            .OrderBy(item => item.FilterGroup, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.SecondaryFilterGroup, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public CopyBetweenProjectsResult Copy(Document sourceDocument, Document targetDocument, IEnumerable<CopyBetweenProjectsItem> selectedItems)
    {
        List<CopyBetweenProjectsItem> items = selectedItems.ToList();
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Wybierz co najmniej jeden element do skopiowania.");
        }

        List<ElementId> elementIds = items
            .SelectMany(item => item.ElementIds)
            .Distinct()
            .ToList();

        CopyBetweenProjectsResult result = new()
        {
            RequestedItemsCount = items.Count,
            RequestedElementIdsCount = elementIds.Count,
            CopiedElementIdsCount = elementIds.Count,
            SourceDocumentTitle = sourceDocument.Title,
            TargetDocumentTitle = targetDocument.Title
        };

        using Transaction transaction = new(targetDocument, "Copy Between Projects");
        transaction.Start();

        CopyPasteOptions options = new();
        options.SetDuplicateTypeNamesHandler(new UseDestinationDuplicateTypeNamesHandler());

        ElementTransformUtils.CopyElements(
            sourceDocument,
            elementIds,
            targetDocument,
            Transform.Identity,
            options);

        transaction.Commit();

        foreach (CopyBetweenProjectsItem item in items)
        {
            result.Messages.Add($"Skopiowano: {item.FilterGroup} | {item.DisplayName}");
        }

        result.Messages.Add("Przy konfliktach nazw typow zostaly uzyte typy z projektu docelowego.");
        return result;
    }

    private static IEnumerable<CopyBetweenProjectsItem> GetSystemTypeItems(Document document, BuiltInCategory category, string label)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(ElementType))
            .Cast<ElementType>()
            .Where(type => type.Category?.Id?.Value == (long)category)
            .Select(type => new CopyBetweenProjectsItem
            {
                Key = $"{label}:{type.Id.Value}",
                DisplayName = type.Name,
                FilterGroup = label,
                SecondaryFilterGroup = type.GetType().Name,
                ElementIds = [type.Id]
            });
    }

    private static IEnumerable<CopyBetweenProjectsItem> GetFamilyItems(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(Family))
            .Cast<Family>()
            .Select(family => new
            {
                Family = family,
                SymbolIds = family.GetFamilySymbolIds().ToList()
            })
            .Where(x => x.SymbolIds.Count > 0)
            .Select(x => new CopyBetweenProjectsItem
            {
                Key = $"Family:{x.Family.Id.Value}",
                DisplayName = x.Family.Name,
                FilterGroup = "Family",
                SecondaryFilterGroup = x.Family.FamilyCategory?.Name ?? "(bez kategorii)",
                ElementIds = x.SymbolIds
            });
    }

    private static IEnumerable<CopyBetweenProjectsItem> GetFamilyTypeItems(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Select(symbol => new CopyBetweenProjectsItem
            {
                Key = $"FamilyType:{symbol.Id.Value}",
                DisplayName = $"{symbol.FamilyName} : {symbol.Name}",
                FilterGroup = "Family Type",
                SecondaryFilterGroup = symbol.FamilyName,
                ElementIds = [symbol.Id]
            });
    }

    private sealed class UseDestinationDuplicateTypeNamesHandler : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
        {
            return DuplicateTypeAction.UseDestinationTypes;
        }
    }
}
