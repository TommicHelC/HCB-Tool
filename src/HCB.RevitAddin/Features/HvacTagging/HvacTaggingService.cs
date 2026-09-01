using System;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.HvacTagging.Models;

namespace HCB.RevitAddin.Features.HvacTagging;

public sealed class HvacTaggingService
{
    private static readonly HvacTagCategoryRule[] SupportedCategoryTemplates =
    [
        new(BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctTags, "Ducts"),
        new(BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_DuctTags, "Flex Ducts"),
        new(BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctFittingTags, "Duct Fittings"),
        new(BuiltInCategory.OST_DuctAccessory, BuiltInCategory.OST_DuctAccessoryTags, "Duct Accessories"),
        new(BuiltInCategory.OST_DuctTerminal, BuiltInCategory.OST_DuctTerminalTags, "Duct Terminals")
    ];

    private static readonly HashSet<long> SupportedCategoryIds = SupportedCategoryTemplates
        .Select(rule => (long)rule.ModelCategory)
        .ToHashSet();

    private static readonly BuiltInCategory[] SupportedModelCategories = SupportedCategoryTemplates
        .Select(rule => rule.ModelCategory)
        .Distinct()
        .ToArray();

    public IReadOnlyList<HvacTagCategoryRule> CreateDefaultRules(Document document)
    {
        List<FamilySymbol> multiCategoryTags = GetTagTypes(document, BuiltInCategory.OST_MultiCategoryTags);

        return SupportedCategoryTemplates
            .Select(template =>
            {
                List<FamilySymbol> availableTagTypes = GetTagTypes(document, template.TagCategory)
                    .Concat(multiCategoryTags)
                    .GroupBy(symbol => symbol.Id.Value)
                    .Select(group => group.First())
                    .OrderBy(symbol => symbol.FamilyName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(symbol => symbol.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                return new HvacTagCategoryRule(template.ModelCategory, template.TagCategory, template.DisplayName)
                {
                    AvailableTagTypes = availableTagTypes,
                    SelectedTagType = availableTagTypes.FirstOrDefault()
                };
            })
            .ToList();
    }

    public IReadOnlyList<View> ResolveTargetViews(Document document, View activeView)
    {
        if (activeView is ViewSheet sheet)
        {
            return sheet.GetAllViewports()
                .Select(id => document.GetElement(id))
                .OfType<Viewport>()
                .Select(viewport => document.GetElement(viewport.ViewId))
                .OfType<View>()
                .Where(IsSupportedAnnotationView)
                .ToList();
        }

        return IsSupportedAnnotationView(activeView)
            ? [activeView]
            : [];
    }

    public IReadOnlyList<HvacTagPreviewRow> BuildPreview(Document document, View activeView, HvacTaggingOptions options)
    {
        IReadOnlyList<View> targetViews = ResolveTargetViews(document, activeView);
        if (targetViews.Count == 0)
        {
            return
            [
                new HvacTagPreviewRow
                {
                    ViewName = activeView.Name,
                    Status = "Blocked",
                    Message = GetUnsupportedViewMessage(activeView)
                }
            ];
        }

        List<HvacTagPreviewRow> rows = [];
        foreach (View view in targetViews)
        {
            List<Element> candidates = CollectCandidates(document, view);
            if (candidates.Count == 0)
            {
                rows.Add(new HvacTagPreviewRow
                {
                    View = view,
                    ViewName = view.Name,
                    Status = "Info",
                    Message = "Brak obslugiwanych elementow HVAC w widoku."
                });
                continue;
            }

            Dictionary<ElementId, int> existingTagCounts = CountExistingTagsByElement(document, view);
            foreach (Element element in candidates)
            {
                BuiltInCategory? category = GetBuiltInCategory(element);
                ElementId? tagTypeId = category.HasValue ? options.GetTagTypeId(category.Value) : null;
                bool hasExistingTag = existingTagCounts.TryGetValue(element.Id, out int tagCount) && tagCount > 0;
                bool duplicate = tagCount > 1;
                XYZ? point = TryGetTagPoint(view, element, options);
                string categoryName = element.Category?.Name ?? category?.ToString() ?? "Unknown";
                string elementName = GetElementDisplayName(document, element);

                if (duplicate)
                {
                    rows.Add(CreateElementRow(view, element, category, categoryName, elementName, point, "Duplicate", "Element ma wiecej niz jeden tag w tym widoku.", false, hasExistingTag, true));
                }
                else if (hasExistingTag)
                {
                    rows.Add(CreateElementRow(view, element, category, categoryName, elementName, point, "Existing", "Tag juz istnieje w tym widoku.", false, true, false));
                }
                else if (tagTypeId == null)
                {
                    rows.Add(CreateElementRow(view, element, category, categoryName, elementName, point, "Blocked", "Brak wybranego typu taga dla tej kategorii.", false, false, false));
                }
                else if (point == null)
                {
                    rows.Add(CreateElementRow(view, element, category, categoryName, elementName, point, "Blocked", "Nie udalo sie wyznaczyc punktu wstawienia taga.", false, false, false));
                }
                else
                {
                    rows.Add(CreateElementRow(view, element, category, categoryName, elementName, point, "Ready", "Tag zostanie utworzony.", true, false, false));
                }
            }
        }

        return rows;
    }

    public HvacTaggingResult Apply(Document document, IReadOnlyList<HvacTagPreviewRow> previewRows, HvacTaggingOptions options)
    {
        HvacTaggingResult result = new()
        {
            ViewsCount = previewRows
                .Where(row => row.View != null)
                .Select(row => row.View!.Id.Value)
                .Distinct()
                .Count(),
            CandidatesCount = previewRows.Count(row => row.Element != null),
            ExistingCount = previewRows.Count(row => row.HasExistingTag && !row.IsDuplicate),
            DuplicateCount = previewRows.Count(row => row.IsDuplicate),
            BlockedCount = previewRows.Count(row => row.Element != null && !row.CanCreateTag && !row.HasExistingTag && !row.IsDuplicate)
        };

        List<HvacTagPreviewRow> rowsToCreate = previewRows
            .Where(row => row.CanCreateTag && row.View != null && row.Element != null && row.TagPoint != null && row.ModelCategory.HasValue)
            .ToList();

        if (rowsToCreate.Count == 0)
        {
            result.Messages.Add("Brak tagow do utworzenia.");
            return result;
        }

        List<IndependentTag> createdTags = [];
        using Transaction transaction = new(document, "HVAC Tagging");
        transaction.Start();

        foreach (HvacTagPreviewRow row in rowsToCreate)
        {
            IndependentTag? createdTag = null;
            try
            {
                ElementId? tagTypeId = options.GetTagTypeId(row.ModelCategory!.Value);
                if (tagTypeId == null)
                {
                    result.FailedCount++;
                    result.Messages.Add($"{row.ViewName}: {row.ElementIdText} - brak typu taga.");
                    continue;
                }

                if (document.GetElement(tagTypeId) is FamilySymbol symbol && !symbol.IsActive)
                {
                    symbol.Activate();
                    document.Regenerate();
                }

                createdTag = IndependentTag.Create(
                    document,
                    row.View!.Id,
                    new Reference(row.Element),
                    options.UseLeader,
                    TagMode.TM_ADDBY_CATEGORY,
                    TagOrientation.Horizontal,
                    row.TagPoint!);

                if (createdTag.GetTypeId().Value != tagTypeId.Value)
                {
                    createdTag.ChangeTypeId(tagTypeId);
                }

                createdTags.Add(createdTag);
                result.CreatedCount++;
            }
            catch (Exception ex)
            {
                if (createdTag?.IsValidObject == true)
                {
                    document.Delete(createdTag.Id);
                }

                result.FailedCount++;
                result.Messages.Add($"{row.ViewName}: {row.ElementIdText} - {ex.Message}");
            }
        }

        document.Regenerate();
        AddCollisionMessages(document, createdTags, result);
        transaction.Commit();

        return result;
    }

    private static HvacTagPreviewRow CreateElementRow(
        View view,
        Element element,
        BuiltInCategory? category,
        string categoryName,
        string elementName,
        XYZ? point,
        string status,
        string message,
        bool canCreateTag,
        bool hasExistingTag,
        bool duplicate)
    {
        return new HvacTagPreviewRow
        {
            View = view,
            Element = element,
            ModelCategory = category,
            ViewName = view.Name,
            CategoryName = categoryName,
            ElementName = elementName,
            ElementIdText = element.Id.Value.ToString(),
            TagPoint = point,
            Status = status,
            Message = message,
            CanCreateTag = canCreateTag,
            HasExistingTag = hasExistingTag,
            IsDuplicate = duplicate
        };
    }

    private static List<FamilySymbol> GetTagTypes(Document document, BuiltInCategory tagCategory)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(tagCategory)
            .WhereElementIsElementType()
            .OfType<FamilySymbol>()
            .ToList();
    }

    private static List<Element> CollectCandidates(Document document, View view)
    {
        return new FilteredElementCollector(document, view.Id)
            .WherePasses(new ElementMulticategoryFilter(SupportedModelCategories))
            .WhereElementIsNotElementType()
            .ToList();
    }

    private static Dictionary<ElementId, int> CountExistingTagsByElement(Document document, View view)
    {
        Dictionary<ElementId, int> counts = [];
        foreach (IndependentTag tag in new FilteredElementCollector(document, view.Id)
                     .OfClass(typeof(IndependentTag))
                     .WhereElementIsNotElementType()
                     .OfType<IndependentTag>())
        {
            foreach (LinkElementId taggedId in tag.GetTaggedElementIds())
            {
                if (taggedId.HostElementId == ElementId.InvalidElementId || taggedId.LinkInstanceId != ElementId.InvalidElementId)
                {
                    continue;
                }

                counts.TryGetValue(taggedId.HostElementId, out int count);
                counts[taggedId.HostElementId] = count + 1;
            }
        }

        return counts;
    }

    private static BuiltInCategory? GetBuiltInCategory(Element element)
    {
        long? categoryId = element.Category?.Id.Value;
        if (categoryId == null || !SupportedCategoryIds.Contains(categoryId.Value))
        {
            return null;
        }

        return (BuiltInCategory)categoryId.Value;
    }

    private static XYZ? TryGetTagPoint(View view, Element element, HvacTaggingOptions options)
    {
        XYZ? anchor = TryGetBoundingBoxCenter(view, element) ?? TryGetLocationPoint(element);
        if (anchor == null)
        {
            return null;
        }

        double offsetX = UnitUtils.ConvertToInternalUnits(options.OffsetXMillimeters, UnitTypeId.Millimeters);
        double offsetY = UnitUtils.ConvertToInternalUnits(options.OffsetYMillimeters, UnitTypeId.Millimeters);
        XYZ offset = (view.RightDirection * offsetX) + (view.UpDirection * offsetY);
        return anchor + offset;
    }

    private static XYZ? TryGetBoundingBoxCenter(View view, Element element)
    {
        BoundingBoxXYZ? box = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
        return box == null ? null : (box.Min + box.Max) / 2.0;
    }

    private static XYZ? TryGetLocationPoint(Element element)
    {
        return element.Location switch
        {
            LocationCurve locationCurve => locationCurve.Curve.Evaluate(0.5, true),
            LocationPoint locationPoint => locationPoint.Point,
            _ => null
        };
    }

    private static bool IsSupportedAnnotationView(View view)
    {
        if (view.IsTemplate || view is ViewSheet)
        {
            return false;
        }

        if (view is View3D)
        {
            return false;
        }

        return view.ViewType is ViewType.FloorPlan
            or ViewType.CeilingPlan
            or ViewType.EngineeringPlan
            or ViewType.Section
            or ViewType.Elevation
            or ViewType.Detail
            or ViewType.DraftingView
            or ViewType.AreaPlan;
    }

    private static string GetUnsupportedViewMessage(View view)
    {
        if (view is ViewSheet)
        {
            return "Arkusz nie ma obslugiwanych viewportow do tagowania.";
        }

        if (view.IsTemplate)
        {
            return "View template nie obsluguje tagowania elementow.";
        }

        return $"Widok typu {view.ViewType} nie jest obslugiwany w V1.";
    }

    private static string GetElementDisplayName(Document document, Element element)
    {
        Element? type = document.GetElement(element.GetTypeId());
        string typeName = type?.Name ?? element.Name ?? string.Empty;
        return string.IsNullOrWhiteSpace(typeName) ? element.Id.Value.ToString() : typeName.Trim();
    }

    private static void AddCollisionMessages(Document document, IReadOnlyList<IndependentTag> createdTags, HvacTaggingResult result)
    {
        foreach (IGrouping<ElementId, IndependentTag> tagsByView in createdTags.GroupBy(tag => tag.OwnerViewId))
        {
            View? view = document.GetElement(tagsByView.Key) as View;
            if (view == null)
            {
                continue;
            }

            List<IndependentTag> allTags = new FilteredElementCollector(document, view.Id)
                .OfClass(typeof(IndependentTag))
                .WhereElementIsNotElementType()
                .OfType<IndependentTag>()
                .ToList();

            foreach (IndependentTag createdTag in tagsByView)
            {
                BoundingBoxXYZ? createdBox = createdTag.get_BoundingBox(view);
                if (createdBox == null)
                {
                    continue;
                }

                bool intersectsExisting = allTags
                    .Where(tag => tag.Id.Value != createdTag.Id.Value)
                    .Select(tag => tag.get_BoundingBox(view))
                    .Any(box => box != null && Intersects(createdBox, box));

                if (intersectsExisting)
                {
                    result.Messages.Add($"{view.Name}: tag {createdTag.Id.Value} moze nachodzic na istniejacy tag.");
                }
            }
        }
    }

    private static bool Intersects(BoundingBoxXYZ first, BoundingBoxXYZ second)
    {
        return first.Min.X <= second.Max.X && first.Max.X >= second.Min.X
            && first.Min.Y <= second.Max.Y && first.Max.Y >= second.Min.Y
            && first.Min.Z <= second.Max.Z && first.Max.Z >= second.Min.Z;
    }
}
