using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.PurgeAnnotations.Models;

namespace HCB.RevitAddin.Features.PurgeAnnotations;

public sealed class PurgeAnnotationsService
{
    public IReadOnlyList<ElementType> GetUnusedAnnotationTypes(Document document)
    {
        List<ElementType> annotationTypes = new FilteredElementCollector(document)
            .WhereElementIsElementType()
            .ToElements()
            .OfType<ElementType>()
            .Where(type => type.Category?.CategoryType == CategoryType.Annotation)
            .OrderBy(type => type.Category?.Name ?? string.Empty)
            .ThenBy(GetDisplayName)
            .ToList();

        // Streamed on purpose: ToElements() materialises a List<Element> of the whole model before
        // a single type id is read.
        HashSet<long> usedTypeIds = [];
        foreach (Element element in new FilteredElementCollector(document).WhereElementIsNotElementType())
        {
            ElementId typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                usedTypeIds.Add(typeId.Value);
            }
        }

        return annotationTypes
            .Where(type => !usedTypeIds.Contains(type.Id.Value))
            .ToList();
    }

    public PurgeAnnotationsResult DeleteTypes(Document document, IEnumerable<ElementType> types)
    {
        PurgeAnnotationsResult result = new();

        using Transaction transaction = new(document, "Purge Unused Annotation Types");
        transaction.Start();

        foreach (ElementType type in types)
        {
            try
            {
                document.Delete(type.Id);
                result.DeletedCount++;
                result.Messages.Add($"Usunieto: {GetDisplayName(type)}");
            }
            catch
            {
                result.FailedCount++;
                result.Messages.Add($"Nie udalo sie usunac: {GetDisplayName(type)}");
            }
        }

        transaction.Commit();
        return result;
    }

    public string GetDisplayName(ElementType type)
    {
        string? typeName = type.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_NAME)?.AsString();
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            return typeName;
        }

        if (!string.IsNullOrWhiteSpace(type.Name))
        {
            return type.Name;
        }

        return $"{type.FamilyName} [{type.Id.Value}]";
    }

    public string GetFilterableLabel(ElementType type)
    {
        string categoryName = type.Category?.Name ?? "No Category";
        string className = type.GetType().Name;
        return $"[{categoryName}] {GetDisplayName(type)} ({className})";
    }
}
