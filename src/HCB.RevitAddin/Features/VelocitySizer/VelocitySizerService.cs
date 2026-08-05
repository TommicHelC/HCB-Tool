using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using HCB.RevitAddin.Features.VelocitySizer.Models;

namespace HCB.RevitAddin.Features.VelocitySizer;

public sealed class VelocitySizerService
{
    private const string VelocityParameterName = "LIN_VE_VELOCITY";
    private const string FlowRateParameterName = "LIN_VE_FLOWRATE_SUM";
    private const string DimensionParameterName = "LIN_VE_DIMENSION";

    private const double RectMinMillimeters = 100d;
    private const double RectMaxMillimeters = 2000d;
    private const double RectStepMillimeters = 50d;
    private const double RectMaxAspectRatio = 2.5d;
    private const double RectMinVelocityMetersPerSecond = 0.5d;
    private const double RectMaxVelocityMetersPerSecond = 10d;

    public IReadOnlyList<VelocitySizerSelectionItem> BuildSelectionItems(Document document, ICollection<ElementId> selectedElementIds, out List<string> messages)
    {
        messages = [];
        if (selectedElementIds.Count == 0)
        {
            return Array.Empty<VelocitySizerSelectionItem>();
        }

        List<VelocitySizerSizeOption> roundDuctSizes = GetRoundDuctSizes(document);
        List<VelocitySizerSizeOption> rectangularDuctSizes = GenerateRectangularDuctSizes();
        List<VelocitySizerSelectionItem> items = [];

        foreach (ElementId elementId in selectedElementIds.Distinct())
        {
            Element? element = document.GetElement(elementId);
            if (element is Duct duct)
            {
                ConnectorProfileType shape = GetCurveShape(duct);
                switch (shape)
                {
                    case ConnectorProfileType.Round:
                        if (roundDuctSizes.Count == 0)
                        {
                            messages.Add($"Element {duct.Id.Value}: brak standardowych rozmiarow kanalow okraglych w projekcie.");
                            continue;
                        }

                        items.Add(CreateSelectionItem(document, duct, "Duct", "Round", roundDuctSizes));
                        continue;
                    case ConnectorProfileType.Rectangular:
                        if (rectangularDuctSizes.Count == 0)
                        {
                            messages.Add($"Element {duct.Id.Value}: brak prostokatnych rozmiarow kanalow znalezionych w projekcie.");
                            continue;
                        }

                        items.Add(CreateSelectionItem(document, duct, "Duct", "Rect", MergeCurrentRectangularSize(duct, rectangularDuctSizes)));
                        continue;
                    default:
                        messages.Add($"Element {duct.Id.Value}: pominieto kanal o nieobslugiwanym ksztalcie.");
                        continue;
                }
            }

            if (element is FlexDuct flexDuct)
            {
                ConnectorProfileType shape = GetCurveShape(flexDuct);
                switch (shape)
                {
                    case ConnectorProfileType.Round:
                        if (roundDuctSizes.Count == 0)
                        {
                            messages.Add($"Element {flexDuct.Id.Value}: brak standardowych rozmiarow kanalow okraglych w projekcie.");
                            continue;
                        }

                        items.Add(CreateSelectionItem(document, flexDuct, "FlexDuct", "Round", roundDuctSizes));
                        continue;
                    case ConnectorProfileType.Rectangular:
                        if (rectangularDuctSizes.Count == 0)
                        {
                            messages.Add($"Element {flexDuct.Id.Value}: brak prostokatnych rozmiarow kanalow znalezionych w projekcie.");
                            continue;
                        }

                        items.Add(CreateSelectionItem(document, flexDuct, "FlexDuct", "Rect", MergeCurrentRectangularSize(flexDuct, rectangularDuctSizes)));
                        continue;
                    default:
                        messages.Add($"Element {flexDuct.Id.Value}: pominieto kanal elastyczny o nieobslugiwanym ksztalcie.");
                        continue;
                }
            }

            if (element is Pipe pipe)
            {
                List<VelocitySizerSizeOption> pipeSizes = GetPipeSizes(pipe);
                if (pipeSizes.Count == 0)
                {
                    messages.Add($"Element {pipe.Id.Value}: brak standardowych srednic dla przypisanego segmentu rury.");
                    continue;
                }

                items.Add(CreateSelectionItem(document, pipe, "Pipe", "Round", pipeSizes));
                continue;
            }

            messages.Add($"Element {elementId.Value}: pominieto element spoza zakresu narzedzia.");
        }

        return items;
    }

    public IReadOnlyList<VelocitySizerSizeOption> BuildPreview(VelocitySizerSelectionItem item, out string? errorMessage)
    {
        errorMessage = null;
        if (!TryParseFlow(item.FlowText, out double flowCubicMetersPerHour) || flowCubicMetersPerHour <= 0d)
        {
            errorMessage = "Wpisz dodatni przeplyw w m3/h dla wybranego elementu.";
            return Array.Empty<VelocitySizerSizeOption>();
        }

        return item.AvailableSizes
            .Select(size => new VelocitySizerSizeOption
            {
                DisplayName = size.DisplayName,
                NominalDiameterInternal = size.NominalDiameterInternal,
                NominalDiameterMillimeters = size.NominalDiameterMillimeters,
                WidthInternal = size.WidthInternal,
                HeightInternal = size.HeightInternal,
                WidthMillimeters = size.WidthMillimeters,
                HeightMillimeters = size.HeightMillimeters,
                AreaSquareMeters = size.AreaSquareMeters,
                IsCurrentSize = size.IsCurrentSize,
                VelocityMetersPerSecond = CalculateVelocityMetersPerSecond(flowCubicMetersPerHour, size.AreaSquareMeters)
            })
            // dla prostokatnych ograniczamy propozycje do sensownego zakresu predkosci;
            // rozmiar odczytany z modelu zostaje zawsze (nawet nietypowy / poza zakresem)
            .Where(size => size.IsCurrentSize ||
                           size.WidthMillimeters <= 0d ||
                           (size.VelocityMetersPerSecond >= RectMinVelocityMetersPerSecond &&
                            size.VelocityMetersPerSecond <= RectMaxVelocityMetersPerSecond))
            .OrderByDescending(size => size.AreaSquareMeters)
            .ThenByDescending(size => size.WidthMillimeters > 0d ? size.WidthMillimeters : size.NominalDiameterMillimeters)
            .ThenByDescending(size => size.HeightMillimeters)
            .ToList();
    }

    public VelocitySizerSizeOption? AutoSelectSize(
        VelocitySizerSelectionItem item,
        double maxVelocityMetersPerSecond,
        out bool exceededLimit,
        out string? errorMessage)
    {
        exceededLimit = false;
        IReadOnlyList<VelocitySizerSizeOption> preview = BuildPreview(item, out errorMessage);
        if (preview.Count == 0)
        {
            return null;
        }

        List<VelocitySizerSizeOption> withinLimit = preview
            .Where(size => size.VelocityMetersPerSecond <= maxVelocityMetersPerSecond)
            .ToList();
        if (withinLimit.Count > 0)
        {
            // najmniejszy przekroj, ktory nadal utrzymuje predkosc na/ponizej limitu
            return withinLimit.OrderBy(size => size.AreaSquareMeters).First();
        }

        // nawet najwiekszy rozmiar przekracza limit - zwracamy najwiekszy (najnizsza predkosc) i oznaczamy
        exceededLimit = true;
        return preview.OrderByDescending(size => size.AreaSquareMeters).First();
    }

    public VelocitySizerResult Apply(
        Document document,
        IReadOnlyList<VelocitySizerSelectionItem> items,
        bool overwriteExistingValues,
        bool changeGeometry)
    {
        VelocitySizerResult result = new();
        if (items.Count == 0)
        {
            return result;
        }

        using Transaction transaction = new(document, changeGeometry ? "Velocity Sizer" : "Velocity Sizer - parametry");
        transaction.Start();

        foreach (VelocitySizerSelectionItem item in items)
        {
            Element? element = document.GetElement(new ElementId(item.ElementId));
            if (element is not MEPCurve curve || item.SelectedSize == null)
            {
                result.FailedCount++;
                result.Messages.Add($"Element {item.ElementId}: brak wybranego rozmiaru lub element nie istnieje.");
                continue;
            }

            if (!TryParseFlow(item.FlowText, out double flowCubicMetersPerHour) || flowCubicMetersPerHour <= 0d)
            {
                result.FailedCount++;
                result.Messages.Add($"Element {item.ElementId}: nieprawidlowy przeplyw.");
                continue;
            }

            Parameter? velocityParameter = curve.LookupParameter(VelocityParameterName);
            Parameter? flowParameter = curve.LookupParameter(FlowRateParameterName);
            if (!IsWritableTarget(velocityParameter) || !IsWritableTarget(flowParameter))
            {
                result.MissingParameterCount++;
                result.Messages.Add($"Element {item.ElementId}: brak zapisywalnych parametrow {VelocityParameterName} i/lub {FlowRateParameterName}.");
                continue;
            }

            bool hasExistingValue = HasValue(velocityParameter) || HasValue(flowParameter);
            if (hasExistingValue && !overwriteExistingValues)
            {
                result.SkippedExistingValuesCount++;
                result.Messages.Add($"Element {item.ElementId}: pominieto z powodu istniejacych wartosci docelowych.");
                continue;
            }

            try
            {
                if (changeGeometry)
                {
                    SetCurveSize(curve, item.SelectedSize);
                }

                double velocityMetersPerSecond = CalculateVelocityMetersPerSecond(flowCubicMetersPerHour, item.SelectedSize.AreaSquareMeters);
                SetNumericParameterValue(velocityParameter!, velocityMetersPerSecond, UnitTypeId.MetersPerSecond);
                SetNumericParameterValue(flowParameter!, flowCubicMetersPerHour, UnitTypeId.CubicMetersPerHour);
                TrySetDimensionParameter(curve, item.SelectedSize);

                if (changeGeometry)
                {
                    TryResizeConnectedFittings(document, curve, item.SelectedSize);
                }

                result.UpdatedCount++;
                result.Messages.Add($"Element {item.ElementId}: ustawiono {item.SelectedSize.DisplayName}, v={velocityMetersPerSecond:0.##} m/s, q={flowCubicMetersPerHour:0.##} m3/h.");
            }
            catch (Exception exception)
            {
                result.FailedCount++;
                result.Messages.Add($"Element {item.ElementId}: {exception.Message}");
            }
        }

        if (result.UpdatedCount > 0)
        {
            if (changeGeometry)
            {
                // wymuszenie przeliczenia, aby podlaczone ksztaltki mialy szanse sie dopasowac
                document.Regenerate();
            }

            transaction.Commit();
        }
        else
        {
            transaction.RollBack();
        }

        return result;
    }

    // przelicza predkosc dla danego elementu i rozmiaru na podstawie jego wlasnego przeplywu
    public double? ComputeVelocity(VelocitySizerSelectionItem item, VelocitySizerSizeOption option)
    {
        if (!TryParseFlow(item.FlowText, out double flowCubicMetersPerHour) || flowCubicMetersPerHour <= 0d)
        {
            return null;
        }

        return CalculateVelocityMetersPerSecond(flowCubicMetersPerHour, option.AreaSquareMeters);
    }

    public bool HasAnyExistingTargetValues(IEnumerable<VelocitySizerSelectionItem> items)
    {
        return items.Any(item => item.HasExistingFlowValue || item.HasExistingVelocityValue);
    }

    private static VelocitySizerSelectionItem CreateSelectionItem(
        Document document,
        MEPCurve curve,
        string elementTypeLabel,
        string supportedShapeLabel,
        IReadOnlyList<VelocitySizerSizeOption> availableSizes)
    {
        Parameter? velocityParameter = curve.LookupParameter(VelocityParameterName);
        Parameter? flowParameter = curve.LookupParameter(FlowRateParameterName);

        double? defaultFlow = TryReadFlowFromTarget(flowParameter) ?? TryReadBuiltInFlow(curve);
        string flowText = defaultFlow.HasValue
            ? defaultFlow.Value.ToString("0.##", CultureInfo.CurrentCulture)
            : string.Empty;

        string? section = ReadSection(curve);
        long systemId = curve.MEPSystem?.Id.Value ?? -1L;
        string sectionKey = section != null ? $"{systemId}:{section}" : $"__solo_{curve.Id.Value}";
        string sectionLabel = section ?? string.Empty;

        return new VelocitySizerSelectionItem
        {
            ElementId = curve.Id.Value,
            ElementTypeLabel = elementTypeLabel,
            CurrentSizeDisplay = GetCurrentSizeDisplay(document, curve),
            SupportedShapeLabel = supportedShapeLabel,
            AvailableSizes = availableSizes,
            SectionKey = sectionKey,
            SectionLabel = sectionLabel,
            FlowText = flowText,
            HasWritableTargets = IsWritableTarget(velocityParameter) && IsWritableTarget(flowParameter),
            HasExistingFlowValue = HasValue(flowParameter),
            HasExistingVelocityValue = HasValue(velocityParameter)
        };
    }

    // numer sekcji to wartosc wyswietlana (AsValueString); AsInteger na tym parametrze potrafi zwracac 0
    private static string? ReadSection(MEPCurve curve)
    {
        Parameter? parameter = curve.get_Parameter(BuiltInParameter.RBS_SECTION);
        if (parameter is not { HasValue: true })
        {
            return null;
        }

        string? valueString = parameter.AsValueString();
        if (!string.IsNullOrWhiteSpace(valueString) && valueString.Trim() != "0")
        {
            return valueString.Trim();
        }

        int integerValue = parameter.AsInteger();
        return integerValue > 0 ? integerValue.ToString(CultureInfo.InvariantCulture) : null;
    }

    private static List<VelocitySizerSizeOption> GetRoundDuctSizes(Document document)
    {
        try
        {
            DuctSizeSettings settings = DuctSizeSettings.GetDuctSizeSettings(document);
            DuctSizes sizes = settings[DuctShape.Round];
            return sizes
                .Cast<MEPSize>()
                .Where(size => size.UsedInSizeLists || size.UsedInSizing)
                .Select(size => CreateRoundSizeOption(size.NominalDiameter))
                .DistinctBy(size => Math.Round(size.NominalDiameterMillimeters, 3))
                .OrderBy(size => size.NominalDiameterMillimeters)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static List<VelocitySizerSizeOption> GenerateRectangularDuctSizes()
    {
        List<VelocitySizerSizeOption> options = [];
        for (double height = RectMinMillimeters; height <= RectMaxMillimeters; height += RectStepMillimeters)
        {
            double maxWidth = Math.Min(RectMaxMillimeters, height * RectMaxAspectRatio);
            for (double width = height; width <= maxWidth + 1e-6; width += RectStepMillimeters)
            {
                options.Add(CreateRectangularSizeOption(width, height));
            }
        }

        return options
            .OrderByDescending(option => option.AreaSquareMeters)
            .ThenByDescending(option => option.WidthMillimeters)
            .ThenByDescending(option => option.HeightMillimeters)
            .ToList();
    }

    // dolacza biezacy (potencjalnie nietypowy) rozmiar prostokatny odczytany z modelu na poczatek listy
    private static IReadOnlyList<VelocitySizerSizeOption> MergeCurrentRectangularSize(MEPCurve curve, List<VelocitySizerSizeOption> baseSizes)
    {
        double widthMillimeters = UnitUtils.ConvertFromInternalUnits(curve.Width, UnitTypeId.Millimeters);
        double heightMillimeters = UnitUtils.ConvertFromInternalUnits(curve.Height, UnitTypeId.Millimeters);
        if (widthMillimeters <= 0d || heightMillimeters <= 0d)
        {
            return baseSizes;
        }

        double displayWidth = Math.Max(widthMillimeters, heightMillimeters);
        double displayHeight = Math.Min(widthMillimeters, heightMillimeters);

        bool alreadyListed = baseSizes.Any(size =>
            Math.Abs(size.WidthMillimeters - displayWidth) < 0.5d &&
            Math.Abs(size.HeightMillimeters - displayHeight) < 0.5d);
        if (alreadyListed)
        {
            return baseSizes;
        }

        VelocitySizerSizeOption current = CreateRectangularSizeOption(displayWidth, displayHeight, isCurrentSize: true);

        List<VelocitySizerSizeOption> merged = [current];
        merged.AddRange(baseSizes);
        return merged;
    }

    private static List<VelocitySizerSizeOption> GetPipeSizes(Pipe pipe)
    {
        try
        {
            Segment? segment = pipe.PipeSegment;
            if (segment == null)
            {
                return [];
            }

            return segment.GetSizes()
                .Cast<MEPSize>()
                .Where(size => size.UsedInSizeLists || size.UsedInSizing)
                .Select(size => CreateRoundSizeOption(size.NominalDiameter))
                .DistinctBy(size => Math.Round(size.NominalDiameterMillimeters, 3))
                .OrderBy(size => size.NominalDiameterMillimeters)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static VelocitySizerSizeOption CreateRoundSizeOption(double nominalDiameterInternal)
    {
        double diameterMillimeters = UnitUtils.ConvertFromInternalUnits(nominalDiameterInternal, UnitTypeId.Millimeters);
        double diameterMeters = diameterMillimeters / 1000d;
        double area = Math.PI * diameterMeters * diameterMeters / 4d;

        return new VelocitySizerSizeOption
        {
            DisplayName = $"O {Math.Round(diameterMillimeters, 0):0}",
            NominalDiameterInternal = nominalDiameterInternal,
            NominalDiameterMillimeters = diameterMillimeters,
            AreaSquareMeters = area
        };
    }

    private static VelocitySizerSizeOption CreateRectangularSizeOption(double widthMillimeters, double heightMillimeters, bool isCurrentSize = false)
    {
        double widthInternal = UnitUtils.ConvertToInternalUnits(widthMillimeters, UnitTypeId.Millimeters);
        double heightInternal = UnitUtils.ConvertToInternalUnits(heightMillimeters, UnitTypeId.Millimeters);
        double area = (widthMillimeters / 1000d) * (heightMillimeters / 1000d);

        return new VelocitySizerSizeOption
        {
            DisplayName = $"{Math.Round(widthMillimeters, 0):0}x{Math.Round(heightMillimeters, 0):0}",
            WidthInternal = widthInternal,
            HeightInternal = heightInternal,
            WidthMillimeters = widthMillimeters,
            HeightMillimeters = heightMillimeters,
            AreaSquareMeters = area,
            IsCurrentSize = isCurrentSize
        };
    }

    private static ConnectorProfileType GetCurveShape(MEPCurve curve)
    {
        foreach (Connector connector in curve.ConnectorManager.Connectors)
        {
            return connector.Shape;
        }

        return ConnectorProfileType.Invalid;
    }

    private static string GetCurrentSizeDisplay(Document document, MEPCurve curve)
    {
        ConnectorProfileType shape = GetCurveShape(curve);
        if (shape == ConnectorProfileType.Round)
        {
            Parameter? diameter = curve.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
            if (diameter is { HasValue: true })
            {
                double millimeters = UnitUtils.ConvertFromInternalUnits(diameter.AsDouble(), UnitTypeId.Millimeters);
                return $"O {Math.Round(millimeters, 0):0}";
            }
        }
        else if (shape == ConnectorProfileType.Rectangular)
        {
            double widthMillimeters = UnitUtils.ConvertFromInternalUnits(curve.Width, UnitTypeId.Millimeters);
            double heightMillimeters = UnitUtils.ConvertFromInternalUnits(curve.Height, UnitTypeId.Millimeters);
            double displayWidth = Math.Max(widthMillimeters, heightMillimeters);
            double displayHeight = Math.Min(widthMillimeters, heightMillimeters);
            return $"{Math.Round(displayWidth, 0):0}x{Math.Round(displayHeight, 0):0}";
        }

        string? sizeText = curve.LookupParameter("Size")?.AsString() ?? curve.LookupParameter("Size")?.AsValueString();
        return string.IsNullOrWhiteSpace(sizeText)
            ? document.GetElement(curve.GetTypeId())?.Name ?? string.Empty
            : sizeText.Trim();
    }

    private static double CalculateVelocityMetersPerSecond(double flowCubicMetersPerHour, double areaSquareMeters)
    {
        double flowCubicMetersPerSecond = flowCubicMetersPerHour / 3600d;
        return areaSquareMeters <= 0d ? 0d : flowCubicMetersPerSecond / areaSquareMeters;
    }

    private static bool TryParseFlow(string text, out double value)
    {
        string normalized = (text ?? string.Empty).Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsWritableTarget(Parameter? parameter)
    {
        return parameter is { IsReadOnly: false } &&
               parameter.StorageType is StorageType.String or StorageType.Double or StorageType.Integer;
    }

    private static bool HasValue(Parameter? parameter)
    {
        if (parameter == null)
        {
            return false;
        }

        return parameter.StorageType switch
        {
            StorageType.String => !string.IsNullOrWhiteSpace(parameter.AsString()),
            StorageType.Double => parameter.HasValue && Math.Abs(parameter.AsDouble()) > 1e-9,
            StorageType.Integer => parameter.HasValue && parameter.AsInteger() != 0,
            _ => false
        };
    }

    private static double? TryReadFlowFromTarget(Parameter? parameter)
    {
        if (parameter == null || !parameter.HasValue)
        {
            return null;
        }

        try
        {
            return parameter.StorageType switch
            {
                StorageType.Double => ConvertDoubleParameterToDisplayValue(parameter, UnitTypeId.CubicMetersPerHour),
                StorageType.Integer => parameter.AsInteger(),
                StorageType.String when TryParseFlow(parameter.AsString() ?? string.Empty, out double parsed) => parsed,
                StorageType.String when TryParseFlow(parameter.AsValueString() ?? string.Empty, out double parsedDisplay) => parsedDisplay,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static double? TryReadBuiltInFlow(MEPCurve curve)
    {
        try
        {
            if (curve is Duct or FlexDuct)
            {
                Parameter? parameter = curve.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                return parameter is { HasValue: true }
                    ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.CubicMetersPerHour)
                    : null;
            }

            if (curve is Pipe)
            {
                Parameter? parameter = curve.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM);
                return parameter is { HasValue: true }
                    ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.CubicMetersPerHour)
                    : null;
            }
        }
        catch
        {
        }

        return null;
    }

    private static void TrySetDimensionParameter(MEPCurve curve, VelocitySizerSizeOption option)
    {
        Parameter? dimension = curve.LookupParameter(DimensionParameterName);
        if (dimension == null || dimension.IsReadOnly)
        {
            return;
        }

        string text = BuildBareDimension(option);
        switch (dimension.StorageType)
        {
            case StorageType.String:
                dimension.Set(text);
                return;
            case StorageType.Double when option.WidthMillimeters <= 0d:
                // pojedyncza srednica okragla - zapis liczbowy w mm
                dimension.Set(UnitUtils.ConvertToInternalUnits(option.NominalDiameterMillimeters, UnitTypeId.Millimeters));
                return;
            case StorageType.Integer when option.WidthMillimeters <= 0d:
                dimension.Set((int)Math.Round(option.NominalDiameterMillimeters));
                return;
        }
    }

    private static string BuildBareDimension(VelocitySizerSizeOption option)
    {
        if (option.WidthMillimeters > 0d && option.HeightMillimeters > 0d)
        {
            return $"{Math.Round(option.WidthMillimeters, 0):0}x{Math.Round(option.HeightMillimeters, 0):0}";
        }

        return $"{Math.Round(option.NominalDiameterMillimeters, 0):0}";
    }

    private static void TryResizeConnectedFittings(Document document, MEPCurve curve, VelocitySizerSizeOption option)
    {
        try
        {
            HashSet<long> visited = [];
            foreach (Connector connector in curve.ConnectorManager.Connectors)
            {
                foreach (Connector reference in connector.AllRefs.Cast<Connector>())
                {
                    Element? owner = reference.Owner;
                    if (owner is FamilyInstance fitting &&
                        owner.Id != curve.Id &&
                        visited.Add(owner.Id.Value))
                    {
                        TrySetFittingSize(fitting, option);
                    }
                }
            }
        }
        catch
        {
            // best-effort - jesli ksztaltka nie da sie sparametryzowac, pomijamy
        }
    }

    private static void TrySetFittingSize(FamilyInstance fitting, VelocitySizerSizeOption option)
    {
        bool isRound = option.WidthMillimeters <= 0d;

        foreach (Parameter parameter in fitting.Parameters)
        {
            if (parameter is not { IsReadOnly: false, StorageType: StorageType.Double })
            {
                continue;
            }

            string name = parameter.Definition?.Name?.ToLowerInvariant() ?? string.Empty;
            if (name.Length == 0 || IsExcludedFittingParameter(name))
            {
                continue;
            }

            try
            {
                if (isRound)
                {
                    if (name.Contains("radius") || name.Contains("promie"))
                    {
                        parameter.Set(option.NominalDiameterInternal / 2d);
                    }
                    else if (name.Contains("diameter") || name.Contains("srednic") || name.Contains("średnic"))
                    {
                        parameter.Set(option.NominalDiameterInternal);
                    }
                }
                else if (name.Contains("width") || name.Contains("szer"))
                {
                    parameter.Set(option.WidthInternal);
                }
                else if (name.Contains("height") || name.Contains("wys"))
                {
                    parameter.Set(option.HeightInternal);
                }
            }
            catch
            {
                // ignorujemy pojedyncze nieudane parametry
            }
        }
    }

    private static bool IsExcludedFittingParameter(string lowerName)
    {
        // pomijamy parametry, ktore nie sa wymiarem nominalnym (izolacja, parametry LIN_*, dlugosci, katy, offsety)
        return lowerName.Contains("insul") ||
               lowerName.Contains("lin") ||
               lowerName.Contains("offset") ||
               lowerName.Contains("length") ||
               lowerName.Contains("dlug") ||
               lowerName.Contains("ług") ||
               lowerName.Contains("angle") ||
               lowerName.Contains("kat") ||
               lowerName.Contains("thick") ||
               lowerName.Contains("grub");
    }

    private static void SetCurveSize(MEPCurve curve, VelocitySizerSizeOption option)
    {
        ConnectorProfileType shape = GetCurveShape(curve);
        if (shape == ConnectorProfileType.Round)
        {
            Parameter? diameterParameter = curve.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
            if (diameterParameter == null || diameterParameter.IsReadOnly || !diameterParameter.Set(option.NominalDiameterInternal))
            {
                throw new InvalidOperationException("Nie udalo sie ustawic srednicy elementu.");
            }

            return;
        }

        if (shape == ConnectorProfileType.Rectangular)
        {
            Parameter? widthParameter = curve.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
            Parameter? heightParameter = curve.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
            if (widthParameter == null || heightParameter == null || widthParameter.IsReadOnly || heightParameter.IsReadOnly)
            {
                throw new InvalidOperationException("Nie udalo sie ustawic szerokosci i wysokosci elementu.");
            }

            if (!widthParameter.Set(option.WidthInternal) || !heightParameter.Set(option.HeightInternal))
            {
                throw new InvalidOperationException("Nie udalo sie ustawic szerokosci i wysokosci elementu.");
            }

            return;
        }

        throw new InvalidOperationException("Ksztalt elementu nie jest obslugiwany.");
    }

    private static void SetNumericParameterValue(Parameter parameter, double displayValue, ForgeTypeId displayUnitTypeId)
    {
        switch (parameter.StorageType)
        {
            case StorageType.String:
                parameter.Set(displayValue.ToString("0.##", CultureInfo.CurrentCulture));
                return;
            case StorageType.Integer:
                parameter.Set((int)Math.Round(displayValue));
                return;
            case StorageType.Double:
            {
                ForgeTypeId dataType = parameter.Definition.GetDataType();
                string typeId = dataType.TypeId ?? string.Empty;
                double valueToStore = typeId.IndexOf("velocity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      typeId.IndexOf("flow", StringComparison.OrdinalIgnoreCase) >= 0
                    ? UnitUtils.ConvertToInternalUnits(displayValue, displayUnitTypeId)
                    : displayValue;

                parameter.Set(valueToStore);
                return;
            }
        }

        throw new InvalidOperationException($"Nieobslugiwany typ parametru: {parameter.StorageType}.");
    }

    private static double ConvertDoubleParameterToDisplayValue(Parameter parameter, ForgeTypeId preferredDisplayUnit)
    {
        ForgeTypeId dataType = parameter.Definition.GetDataType();
        string typeId = dataType.TypeId ?? string.Empty;
        return typeId.IndexOf("flow", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeId.IndexOf("velocity", StringComparison.OrdinalIgnoreCase) >= 0
            ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), preferredDisplayUnit)
            : parameter.AsDouble();
    }
}
