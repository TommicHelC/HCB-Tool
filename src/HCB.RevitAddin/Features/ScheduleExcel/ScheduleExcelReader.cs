using System;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.ScheduleExcel.Models;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>
/// Reads a schedule into a flat data table: one row per element (itemized) or per group of elements
/// (not itemized, grouped by the schedule's sort/group fields, as Revit does).
/// Values come from the elements' parameters, so every row knows exactly which elements it covers.
/// The schedule view itself is read afterwards for its look (colours, bold...) and for fields the API
/// cannot evaluate (formulas, room fields). Parameters are looked up by id only - never by scanning
/// element.Parameters (see REVIT_MCP_ERRORS.md).
/// </summary>
internal sealed class ScheduleExcelReader
{
    private readonly Document _document;

    public ScheduleExcelReader(Document document)
    {
        _document = document;
    }

    public static IReadOnlyList<ViewSchedule> GetExportableSchedules(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(schedule => GetUnsupportedReason(schedule) == null)
            .OrderBy(schedule => schedule.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Null when the schedule can be exported, otherwise a short Polish reason.</summary>
    public static string? GetUnsupportedReason(ViewSchedule schedule)
    {
        if (schedule.IsTemplate)
        {
            return "Szablon zestawienia nie zawiera danych.";
        }

        if (schedule.IsTitleblockRevisionSchedule || schedule.IsInternalKeynoteSchedule)
        {
            return "Zestawienia rewizji i opisów nie są obsługiwane.";
        }

        if (schedule.Definition.IsMaterialTakeoff)
        {
            return "Zestawienia materiałów (Material Takeoff) nie są obsługiwane.";
        }

        return null;
    }

    /// <param name="expandGroups">
    /// One row per element even when the schedule groups rows ("Itemize every instance" off), so single
    /// instances can be edited.
    /// </param>
    /// <param name="addIdColumn">Adds a read-only first column with the element ids of each row.</param>
    public ScheduleExcelData Read(ViewSchedule schedule, bool expandGroups = false, bool addIdColumn = false)
    {
        ScheduleDefinition definition = schedule.Definition;
        bool itemized = definition.IsItemized || expandGroups;
        List<Element> elements = new FilteredElementCollector(_document, schedule.Id)
            .WhereElementIsNotElementType()
            .ToElements()
            .ToList();

        var fieldReaders = new Dictionary<int, FieldReader>();
        var visibleFields = new List<FieldReader>();
        for (int index = 0; index < definition.GetFieldCount(); index++)
        {
            ScheduleField field = definition.GetField(index);
            if (field.IsHidden)
            {
                continue;
            }

            FieldReader reader = CreateFieldReader(field, index, elements);
            fieldReaders[index] = reader;
            visibleFields.Add(reader);
        }

        var sortFields = new List<(FieldReader Reader, bool Descending)>();
        var groupLevelIndexes = new List<int>();
        var groupLevels = new List<ScheduleExcelGroupLevel>();
        for (int index = 0; index < definition.GetSortGroupFieldCount(); index++)
        {
            ScheduleSortGroupField sortField = definition.GetSortGroupField(index);
            int fieldIndex = definition.GetFieldIndex(sortField.FieldId);
            if (fieldIndex < 0)
            {
                continue;
            }

            if (!fieldReaders.TryGetValue(fieldIndex, out FieldReader? reader))
            {
                // Hidden fields still drive grouping and order.
                reader = CreateFieldReader(definition.GetField(fieldIndex), fieldIndex, elements);
                fieldReaders[fieldIndex] = reader;
            }

            sortFields.Add((reader, sortField.SortOrder == ScheduleSortOrder.Descending));
            if (sortField.ShowHeader || sortField.ShowFooter)
            {
                groupLevelIndexes.Add(sortFields.Count - 1);
                groupLevels.Add(new ScheduleExcelGroupLevel
                {
                    ShowHeader = sortField.ShowHeader,
                    ShowFooter = sortField.ShowFooter,
                    ShowFooterTitle = sortField.ShowFooterTitle,
                    ShowFooterCount = sortField.ShowFooterCount
                });
            }
        }

        var data = new ScheduleExcelData
        {
            ScheduleName = schedule.Name,
            Title = schedule.Name,
            ScheduleUniqueId = schedule.UniqueId,
            DocumentTitle = _document.Title,
            DocumentPath = _document.PathName ?? string.Empty,
            DocumentId = GetDocumentId(_document),
            IsItemized = itemized,
            ElementCount = elements.Count,
            ShowGrandTotal = definition.ShowGrandTotal,
            GrandTotalTitle = definition.ShowGrandTotalTitle ? definition.GrandTotalTitle ?? string.Empty : string.Empty
        };
        data.Columns.AddRange(visibleFields.Select(reader => reader.Column));
        data.GroupLevels.AddRange(groupLevels);

        List<ElementValues> elementValues = elements
            .Select(element => new ElementValues(
                element,
                visibleFields.Select(reader => reader.Read(element)).ToArray(),
                sortFields.Select(sort => sort.Reader.Read(element).Value).ToArray()))
            .ToList();

        IComparer<ElementValues> comparer = new SortKeyComparer(sortFields.Select(sort => sort.Descending).ToArray());
        IEnumerable<IReadOnlyList<ElementValues>> groups = itemized
            ? elementValues
                .OrderBy(item => item, comparer)
                .ThenBy(item => item.Element.Id.Value)
                .Select(item => (IReadOnlyList<ElementValues>)new[] { item })
            : elementValues
                .GroupBy(item => string.Join("\u001F", item.SortKey.Select(ToComparableText)))
                .Select(group => (IReadOnlyList<ElementValues>)group.OrderBy(item => item.Element.Id.Value).ToList())
                .OrderBy(group => group[0], comparer);

        int rowNumber = 0;
        foreach (IReadOnlyList<ElementValues> group in groups)
        {
            rowNumber++;
            var row = new ScheduleExcelRow
            {
                Key = $"R{rowNumber:D5}",
                Values = MergeValues(group, visibleFields),
                Styles = new ScheduleExcelCellStyle?[visibleFields.Count],
                GroupValues = groupLevelIndexes.Select(level => ToDisplayText(group[0].SortKey[level])).ToArray()
            };
            row.UniqueIds.AddRange(group.Select(item => item.Element.UniqueId));
            data.Rows.Add(row);
        }

        ResolveColumnEditability(data);

        try
        {
            if (itemized && !definition.IsItemized)
            {
                ReadScheduleViewItemized(schedule, data);
            }
            else
            {
                ReadScheduleView(schedule, data);
            }
        }
        catch (Exception exception)
        {
            // Look and formula values are extras: without them the export still holds all parameter data.
            ScheduleExcelLog.Error($"Odczyt widoku zestawienia '{schedule.Name}' nie powiódł się.", exception);
        }

        if (addIdColumn)
        {
            // Added after the schedule view was read: view columns map 1:1 to schedule fields by position.
            AddIdColumn(data);
        }

        return data;
    }

    private void AddIdColumn(ScheduleExcelData data)
    {
        data.Columns.Insert(0, new ScheduleExcelColumn
        {
            FieldIndex = -1,
            Header = "ID",
            FieldType = "ElementIds",
            ValueKind = ScheduleExcelValueKind.Text,
            IsReadOnly = true,
            ReadOnlyReason = "Identyfikator elementu w Revit (ElementId)."
        });

        foreach (ScheduleExcelRow row in data.Rows)
        {
            string ids = string.Join("; ", row.UniqueIds
                .Select(uniqueId => _document.GetElement(uniqueId)?.Id.Value.ToString(CultureInfo.InvariantCulture))
                .Where(id => id != null));
            if (ids.Length > ScheduleExcelFormat.MaxCellText)
            {
                ids = ids[..ScheduleExcelFormat.MaxCellText];
            }

            object? value = row.UniqueIds.Count == 1 && double.TryParse(ids, NumberStyles.Integer, CultureInfo.InvariantCulture, out double single)
                ? single
                : ids;
            Prepend(row, new ScheduleExcelValue(value, false, true));
        }
    }

    private static void Prepend(ScheduleExcelRow row, ScheduleExcelValue value)
    {
        ScheduleExcelValue[] values = row.Values;
        ScheduleExcelCellStyle?[] styles = row.Styles;
        var newValues = new ScheduleExcelValue[values.Length + 1];
        var newStyles = new ScheduleExcelCellStyle?[styles.Length + 1];
        newValues[0] = value;
        Array.Copy(values, 0, newValues, 1, values.Length);
        Array.Copy(styles, 0, newStyles, 1, styles.Length);
        row.Values = newValues;
        row.Styles = newStyles;
    }

    /// <summary>
    /// Expanded export of a grouped schedule: the view shows groups, so formula values per element exist only
    /// when the schedule is itemized. Itemize inside a transaction, read the table, roll back - the model and
    /// its undo history stay untouched.
    /// </summary>
    private void ReadScheduleViewItemized(ViewSchedule schedule, ScheduleExcelData data)
    {
        if (_document.IsReadOnly || _document.IsModifiable)
        {
            ReadScheduleView(schedule, data);
            return;
        }

        using var transaction = new Transaction(_document, "HCB Excel - odczyt zestawienia");
        try
        {
            transaction.Start();
            schedule.Definition.IsItemized = true;
            _document.Regenerate();
            ReadScheduleView(schedule, data);
        }
        finally
        {
            if (transaction.HasStarted() && !transaction.HasEnded())
            {
                transaction.RollBack();
            }
        }
    }

    /// <summary>
    /// A parameter can be read only for some elements and editable for others (e.g. Number of People on spaces).
    /// The column stays editable when any cell is; the locked cells are marked per row.
    /// </summary>
    private static void ResolveColumnEditability(ScheduleExcelData data)
    {
        for (int index = 0; index < data.Columns.Count; index++)
        {
            ScheduleExcelColumn column = data.Columns[index];
            bool parameterColumn = column.FieldType is "Instance" or "ElementType"
                && !column.FromScheduleText
                && column.ValueKind != ScheduleExcelValueKind.ElementId
                && !(column.ValueKind == ScheduleExcelValueKind.Text && column.StorageType != nameof(StorageType.String));
            if (!parameterColumn || data.Rows.Count == 0)
            {
                continue;
            }

            int column_ = index;
            bool anyEditable = data.Rows.Any(row => !row.Values[column_].ReadOnly);
            bool anyLocked = data.Rows.Any(row => row.Values[column_].ReadOnly);
            if (anyEditable && column.IsReadOnly)
            {
                column.IsReadOnly = false;
                column.ReadOnlyReason = string.Empty;
            }
            else if (!anyEditable && !column.IsReadOnly)
            {
                column.IsReadOnly = true;
                column.ReadOnlyReason = "Parametr tylko do odczytu dla wszystkich elementów.";
            }

            column.HasReadOnlyCells = !column.IsReadOnly && anyLocked;
        }
    }

    /// <summary>
    /// Reads the schedule as Revit draws it: title, heading and cell styles, and the display text of
    /// fields the API cannot evaluate. Body rows are matched to data rows by their text columns.
    /// </summary>
    private static void ReadScheduleView(ViewSchedule schedule, ScheduleExcelData data)
    {
        TableData table = schedule.GetTableData();
        TableSectionData header = table.GetSectionData(SectionType.Header);
        if (header != null && header.NumberOfRows > 0 && header.NumberOfColumns > 0)
        {
            string title = schedule.GetCellText(SectionType.Header, 0, 0);
            if (!string.IsNullOrWhiteSpace(title))
            {
                data.Title = title;
            }

            data.TitleStyle = ToStyle(header.GetTableCellStyle(0, 0));
        }

        TableSectionData body = table.GetSectionData(SectionType.Body);
        if (body == null)
        {
            return;
        }

        int columnCount = Math.Min(body.NumberOfColumns, data.Columns.Count);
        int headingRow = FindHeadingRow(schedule, body, data, columnCount);
        if (headingRow >= 0)
        {
            for (int column = 0; column < columnCount; column++)
            {
                data.Columns[column].HeaderStyle = ToStyle(body.GetTableCellStyle(headingRow, column));
            }
        }

        // Rows are matched on every column we know from parameters: text columns exactly, numbers within the
        // precision Revit displays. Text-only matching failed for schedules whose only text column is empty.
        int[] compareColumns = Enumerable.Range(0, columnCount)
            .Where(column => !data.Columns[column].FromScheduleText
                && data.Columns[column].ValueKind is not ScheduleExcelValueKind.YesNo)
            .ToArray();
        int[] textKeyColumns = compareColumns
            .Where(column => data.Columns[column].ValueKind is ScheduleExcelValueKind.Text or ScheduleExcelValueKind.ElementId)
            .ToArray();
        int[] numberColumns = compareColumns.Except(textKeyColumns).ToArray();
        if (compareColumns.Length == 0)
        {
            return;
        }

        var candidates = new Dictionary<string, List<ScheduleExcelRow>>();
        foreach (ScheduleExcelRow row in data.Rows)
        {
            string key = string.Join("\u001F", textKeyColumns.Select(column => row.Values[column].Mixed
                ? string.Empty
                : ToDisplayText(row.Values[column].Value)));
            if (!candidates.TryGetValue(key, out List<ScheduleExcelRow>? list))
            {
                candidates[key] = list = [];
            }

            list.Add(row);
        }

        int[] textColumns = Enumerable.Range(0, columnCount).Where(column => data.Columns[column].FromScheduleText).ToArray();
        var texts = new Dictionary<(ScheduleExcelRow Row, int Column), string>();
        int matched = 0;
        var unmatchedSamples = new List<string>();
        for (int bodyRow = headingRow + 1; bodyRow < body.NumberOfRows; bodyRow++)
        {
            int current = bodyRow;
            Dictionary<int, string> cellTexts = compareColumns.ToDictionary(column => column, column => schedule.GetCellText(SectionType.Body, current, column));
            if (cellTexts.Values.All(string.IsNullOrEmpty))
            {
                continue;
            }

            string key = string.Join("\u001F", textKeyColumns.Select(column => cellTexts[column]));
            ScheduleExcelRow? row = candidates.TryGetValue(key, out List<ScheduleExcelRow>? list)
                ? list.FirstOrDefault(candidate => numberColumns.All(column => NumberMatches(cellTexts[column], candidate.Values[column])))
                : null;
            if (row == null)
            {
                if (unmatchedSamples.Count < 5)
                {
                    unmatchedSamples.Add($"#{bodyRow}: '{string.Join("' | '", cellTexts.Values)}'");
                }

                continue;
            }

            list!.Remove(row);
            matched++;
            for (int column = 0; column < columnCount; column++)
            {
                row.Styles[column] = ToStyle(body.GetTableCellStyle(bodyRow, column));
            }

            foreach (int column in textColumns)
            {
                texts[(row, column)] = schedule.GetCellText(SectionType.Body, bodyRow, column);
            }
        }

        foreach (int column in textColumns)
        {
            ApplyScheduleText(data, column, texts);
        }

        string ourSamples = string.Join(" ; ", data.Rows.Take(3).Select(row => $"'{string.Join("' | '", compareColumns.Select(column => ToDisplayText(row.Values[column].Value)))}'"));
        ScheduleExcelLog.Info(
            $"'{schedule.Name}': body {body.NumberOfRows}x{body.NumberOfColumns}, kolumny {data.Columns.Count}, nagłówek w wierszu {headingRow}, "
            + $"kolumny dopasowania [{string.Join(", ", compareColumns.Select(column => data.Columns[column].Header))}], "
            + $"kolumny z widoku [{string.Join(", ", textColumns.Select(column => data.Columns[column].Header))}], "
            + $"dopasowano {matched}/{data.Rows.Count}. Nasze: {ourSamples}. Niedopasowane z Revit: {string.Join(" ; ", unmatchedSamples)}");
    }

    /// <summary>Revit display text vs our value: numbers equal within the precision Revit shows.</summary>
    private static bool NumberMatches(string text, ScheduleExcelValue value)
    {
        if (value.Mixed)
        {
            return true;
        }

        if (value.Value is not double number)
        {
            return string.IsNullOrWhiteSpace(text);
        }

        if (!TryParseDisplayNumber(text, out double shown, out int decimals))
        {
            return false;
        }

        double tolerance = 0.5 * Math.Pow(10, -decimals) + 1e-9 * Math.Max(1, Math.Abs(number));
        return Math.Abs(number - shown) <= tolerance;
    }

    /// <summary>Last body row whose texts match the column headings (grouped headings can take several rows).</summary>
    private static int FindHeadingRow(ViewSchedule schedule, TableSectionData body, ScheduleExcelData data, int columnCount)
    {
        int found = -1;
        for (int row = 0; row < Math.Min(body.NumberOfRows, 6); row++)
        {
            int matches = Enumerable.Range(0, columnCount)
                .Count(column => schedule.GetCellText(SectionType.Body, row, column) == data.Columns[column].Header);
            if (columnCount > 0 && matches * 2 >= columnCount)
            {
                found = row;
            }
        }

        return found;
    }

    /// <summary>Turns display texts into numbers when the whole column is numeric, else keeps the text.</summary>
    private static void ApplyScheduleText(ScheduleExcelData data, int column, Dictionary<(ScheduleExcelRow Row, int Column), string> texts)
    {
        var parsed = new Dictionary<ScheduleExcelRow, (double Number, int Decimals)>();
        bool allNumeric = true;
        foreach (ScheduleExcelRow row in data.Rows)
        {
            if (!texts.TryGetValue((row, column), out string? text) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (TryParseDisplayNumber(text, out double number, out int decimals))
            {
                parsed[row] = (number, decimals);
            }
            else
            {
                allNumeric = false;
            }
        }

        ScheduleExcelColumn target = data.Columns[column];
        bool numeric = allNumeric && parsed.Count > 0;
        if (numeric)
        {
            int decimals = parsed.Values.Max(value => value.Decimals);
            target.ValueKind = ScheduleExcelValueKind.Number;
            target.NumberFormat = decimals == 0 ? "#,##0" : "#,##0." + new string('0', decimals);
        }

        foreach (ScheduleExcelRow row in data.Rows)
        {
            if (!texts.TryGetValue((row, column), out string? text) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            object value = numeric ? parsed[row].Number : text;
            row.Values[column] = new ScheduleExcelValue(value, false, true);
        }
    }

    private static readonly Regex NumberPattern = new(@"^[-+−]?[\d\s  .,']*\d", RegexOptions.Compiled);

    /// <summary>Parses Revit display text like "1 232,5", "3.21 m" or "17,9" using the last separator as decimal.</summary>
    internal static bool TryParseDisplayNumber(string text, out double number, out int decimals)
    {
        number = 0;
        decimals = 0;
        string trimmed = text.Trim();
        Match match = NumberPattern.Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        string rest = trimmed[match.Length..].Trim();
        if (rest.Length > 0 && char.IsLetterOrDigit(rest[0]) && rest.Any(char.IsDigit))
        {
            return false;
        }

        string raw = new string(match.Value.Where(ch => !char.IsWhiteSpace(ch) && ch != '\'').ToArray()).Replace('−', '-');
        int lastSeparator = raw.LastIndexOfAny(['.', ',']);
        string normalized;
        if (lastSeparator < 0)
        {
            normalized = raw;
        }
        else
        {
            string integerPart = raw[..lastSeparator].Replace(".", string.Empty).Replace(",", string.Empty);
            string fraction = raw[(lastSeparator + 1)..];
            bool onlyGrouping = fraction.Length == 3 && raw.Count(ch => ch == raw[lastSeparator]) > 1;
            normalized = onlyGrouping ? integerPart + fraction : integerPart + "." + fraction;
            decimals = onlyGrouping ? 0 : fraction.Length;
        }

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static ScheduleExcelCellStyle? ToStyle(TableCellStyle? style)
    {
        if (style == null)
        {
            return null;
        }

        string? background = ToHex(style.BackgroundColor, ignore: (255, 255, 255));
        string? text = ToHex(style.TextColor, ignore: (0, 0, 0));
        string? alignment = style.FontHorizontalAlignment switch
        {
            HorizontalAlignmentStyle.Left => "Left",
            HorizontalAlignmentStyle.Center => "Center",
            HorizontalAlignmentStyle.Right => "Right",
            _ => null
        };

        return new ScheduleExcelCellStyle(background, text, style.IsFontBold, style.IsFontItalic, style.IsFontUnderline, alignment);
    }

    private static string? ToHex(Color? color, (byte R, byte G, byte B) ignore)
    {
        if (color == null || !color.IsValid)
        {
            return null;
        }

        return color.Red == ignore.R && color.Green == ignore.G && color.Blue == ignore.B
            ? null
            : $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }

    private static ScheduleExcelValue[] MergeValues(IReadOnlyList<ElementValues> group, IReadOnlyList<FieldReader> fields)
    {
        var merged = new ScheduleExcelValue[fields.Count];
        for (int column = 0; column < fields.Count; column++)
        {
            if (fields[column].IsCount)
            {
                merged[column] = new ScheduleExcelValue((double)group.Count, false, true);
                continue;
            }

            ScheduleExcelValue first = group[0].Values[column];
            bool readOnly = group.Any(item => item.Values[column].ReadOnly);

            // Grouped row of a "Calculate totals" field: Revit shows the group's sum/min/max. Such a cell
            // stands for many values, so it is read only.
            string total = fields[column].Column.TotalFunction;
            if (group.Count > 1 && total.Length > 0 && group.All(item => item.Values[column].Value is double or null))
            {
                double[] numbers = group.Select(item => item.Values[column].Value).OfType<double>().ToArray();
                object? aggregate = numbers.Length == 0 ? null : total switch
                {
                    "Min" => numbers.Min(),
                    "Max" => numbers.Max(),
                    _ => numbers.Sum()
                };
                merged[column] = new ScheduleExcelValue(aggregate, false, true);
                continue;
            }

            string firstText = ToComparableText(first.Value);
            bool mixed = group.Skip(1).Any(item => ToComparableText(item.Values[column].Value) != firstText);
            merged[column] = mixed
                ? new ScheduleExcelValue(null, true, readOnly)
                : new ScheduleExcelValue(first.Value, false, readOnly);
        }

        return merged;
    }

    internal static string ToComparableText(object? value)
    {
        return value switch
        {
            null => string.Empty,
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            bool flag => flag ? "1" : "0",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    internal static string ToDisplayText(object? value)
    {
        return value switch
        {
            null => string.Empty,
            double number => number.ToString("0.###", CultureInfo.CurrentCulture),
            bool flag => flag ? "Tak" : "Nie",
            _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
        };
    }

    private FieldReader CreateFieldReader(ScheduleField field, int index, IReadOnlyList<Element> elements)
    {
        ScheduleFieldType fieldType = field.FieldType;
        string header = string.IsNullOrWhiteSpace(field.ColumnHeading) ? field.GetName() : field.ColumnHeading;
        string totalFunction = field.DisplayType switch
        {
            ScheduleFieldDisplayType.Totals => "Sum",
            ScheduleFieldDisplayType.Min => "Min",
            ScheduleFieldDisplayType.Max => "Max",
            _ => string.Empty
        };
        ScheduleExcelCellStyle? bodyStyle = SafeFieldStyle(field);

        if (fieldType == ScheduleFieldType.Count)
        {
            return FieldReader.Count(new ScheduleExcelColumn
            {
                FieldIndex = index,
                Header = header,
                FieldType = fieldType.ToString(),
                ValueKind = ScheduleExcelValueKind.Integer,
                NumberFormat = "0",
                IsReadOnly = true,
                ReadOnlyReason = "Liczba elementów jest wyliczana.",
                TotalFunction = totalFunction,
                BodyStyle = bodyStyle
            });
        }

        bool supported = fieldType == ScheduleFieldType.Instance || fieldType == ScheduleFieldType.ElementType;
        ScheduleExcelParameterLookup? lookup = supported ? ScheduleExcelParameterLookup.Create(_document, field.ParameterId) : null;
        if (lookup == null)
        {
            return FieldReader.Unsupported(new ScheduleExcelColumn
            {
                FieldIndex = index,
                Header = header,
                FieldType = fieldType.ToString(),
                ParameterId = field.ParameterId.Value,
                ValueKind = ScheduleExcelValueKind.Text,
                IsReadOnly = true,
                FromScheduleText = true,
                ReadOnlyReason = "Pole wyliczane (formuła, pomieszczenie...) - wartość z widoku zestawienia.",
                TotalFunction = totalFunction,
                BodyStyle = bodyStyle
            });
        }

        bool isType = fieldType == ScheduleFieldType.ElementType;
        Parameter? sample = elements
            .Select(element => lookup.Get(ScheduleExcelParameterLookup.GetTarget(_document, element, isType)))
            .FirstOrDefault(parameter => parameter != null);

        ForgeTypeId spec = SafeSpec(field, sample);
        StorageType storage = sample?.StorageType ?? StorageType.String;
        ScheduleExcelValueKind kind = GetValueKind(storage, spec, sample);

        string unitTypeId = string.Empty;
        string unitLabel = string.Empty;
        string numberFormat = string.Empty;
        ForgeTypeId? unit = null;
        if (kind == ScheduleExcelValueKind.Number)
        {
            (unit, unitLabel, numberFormat) = GetDisplayUnit(field, spec);
            unitTypeId = unit?.TypeId ?? string.Empty;
        }
        else if (kind == ScheduleExcelValueKind.Integer)
        {
            numberFormat = "0";
        }

        List<string> choices = kind == ScheduleExcelValueKind.ElementId && sample != null
            ? CollectChoices(elements, lookup, isType)
            : [];
        bool typeChange = sample != null && ScheduleExcelReferences.IsTypeChange(sample);

        string readOnlyReason = kind switch
        {
            _ when sample == null => "Żaden element zestawienia nie ma tego parametru.",
            ScheduleExcelValueKind.ElementId when choices.Count == 0 => "Brak listy dozwolonych wartości dla tego pola.",
            _ when sample.IsReadOnly && !typeChange => "Parametr tylko do odczytu w Revit.",
            ScheduleExcelValueKind.Text when storage != StorageType.String => "Wartość z listy (wyliczeniowa) - zmiana w przygotowaniu.",
            _ => string.Empty
        };

        var column = new ScheduleExcelColumn
        {
            FieldIndex = index,
            Header = header,
            FieldType = fieldType.ToString(),
            ParameterId = field.ParameterId.Value,
            ParameterName = field.GetName(),
            ParameterGuid = lookup.Guid?.ToString() ?? string.Empty,
            IsTypeParameter = isType,
            StorageType = storage.ToString(),
            ValueKind = kind,
            SpecTypeId = spec.TypeId ?? string.Empty,
            UnitTypeId = unitTypeId,
            UnitLabel = unitLabel,
            NumberFormat = numberFormat,
            IsReadOnly = readOnlyReason.Length > 0,
            ReadOnlyReason = readOnlyReason,
            TotalFunction = totalFunction,
            BodyStyle = bodyStyle,
            Choices = choices
        };

        return new FieldReader(column, element => ScheduleExcelParameterLookup.ReadValue(lookup.Get(ScheduleExcelParameterLookup.GetTarget(_document, element, isType)), kind, unit));
    }

    /// <summary>
    /// Drop-down values for a reference column: union of what each element accepts. Valid types depend on the
    /// element's family/category, so they are collected once per current type, not per element.
    /// </summary>
    private List<string> CollectChoices(IReadOnlyList<Element> elements, ScheduleExcelParameterLookup lookup, bool isType)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var choices = new List<string>();
        var visitedKeys = new HashSet<long>();
        foreach (Element element in elements)
        {
            Element? target = ScheduleExcelParameterLookup.GetTarget(_document, element, isType);
            Parameter? parameter = lookup.Get(target);
            if (target == null || parameter == null)
            {
                continue;
            }

            long key = ScheduleExcelReferences.IsTypeChange(parameter) ? target.GetTypeId().Value : -1;
            if (!visitedKeys.Add(key))
            {
                continue;
            }

            foreach ((ElementId _, string text) in ScheduleExcelReferences.Candidates(_document, target, parameter))
            {
                if (seen.Add(text))
                {
                    choices.Add(text);
                }
            }
        }

        choices.Sort(StringComparer.CurrentCultureIgnoreCase);
        return choices;
    }

    private static ScheduleExcelCellStyle? SafeFieldStyle(ScheduleField field)
    {
        try
        {
            return ToStyle(field.GetStyle());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ForgeTypeId SafeSpec(ScheduleField field, Parameter? sample)
    {
        try
        {
            ForgeTypeId spec = field.GetSpecTypeId();
            if (spec != null && !spec.Empty())
            {
                return spec;
            }
        }
        catch (Exception)
        {
            // Some field kinds have no spec; fall back to the parameter definition below.
        }

        return sample?.Definition?.GetDataType() ?? new ForgeTypeId();
    }

    private static ScheduleExcelValueKind GetValueKind(StorageType storage, ForgeTypeId spec, Parameter? sample)
    {
        switch (storage)
        {
            case StorageType.Double:
                return ScheduleExcelValueKind.Number;
            case StorageType.ElementId:
                return ScheduleExcelValueKind.ElementId;
            case StorageType.Integer:
                if (spec == SpecTypeId.Boolean.YesNo)
                {
                    return ScheduleExcelValueKind.YesNo;
                }

                // Enum-like integers (e.g. built-in choices) display text, not the stored number.
                string? text = sample?.AsValueString();
                return sample == null || text == null || text == sample.AsInteger().ToString(CultureInfo.CurrentCulture)
                    ? ScheduleExcelValueKind.Integer
                    : ScheduleExcelValueKind.Text;
            default:
                return ScheduleExcelValueKind.Text;
        }
    }

    private (ForgeTypeId? Unit, string Label, string NumberFormat) GetDisplayUnit(ScheduleField field, ForgeTypeId spec)
    {
        try
        {
            if (spec.Empty() || !UnitUtils.IsMeasurableSpec(spec))
            {
                return (null, string.Empty, "0.###");
            }

            FormatOptions options = field.GetFormatOptions();
            if (options.UseDefault)
            {
                options = _document.GetUnits().GetFormatOptions(spec);
            }

            ForgeTypeId unit = options.GetUnitTypeId();
            return (unit, GetUnitSymbol(options, unit), ToNumberFormat(options.Accuracy));
        }
        catch (Exception)
        {
            // Unit lookup must never stop the export; write internal units instead.
            return (null, string.Empty, "0.###");
        }
    }

    /// <summary>Short symbol like "m³/h"; empty for unitless values ("Fixed", "General").</summary>
    private static string GetUnitSymbol(FormatOptions options, ForgeTypeId unit)
    {
        ForgeTypeId symbol = options.GetSymbolTypeId();
        if (symbol == null || symbol.Empty())
        {
            symbol = FormatOptions.GetValidSymbols(unit).FirstOrDefault(candidate => candidate != null && !candidate.Empty())
                ?? new ForgeTypeId();
        }

        return symbol.Empty() ? string.Empty : LabelUtils.GetLabelForSymbol(symbol).Trim();
    }

    private static string ToNumberFormat(double accuracy)
    {
        if (accuracy <= 0 || accuracy >= 1)
        {
            return "#,##0";
        }

        int decimals = Math.Clamp((int)Math.Round(-Math.Log10(accuracy)), 0, 8);
        return decimals == 0 ? "#,##0" : "#,##0." + new string('0', decimals);
    }

    internal static string GetDocumentId(Document document)
    {
        try
        {
            return ExportUtils.GetGBXMLDocumentId(document).ToString();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private sealed record ElementValues(Element Element, ScheduleExcelValue[] Values, object?[] SortKey);

    private sealed class FieldReader
    {
        private readonly Func<Element, ScheduleExcelValue> _read;

        public FieldReader(ScheduleExcelColumn column, Func<Element, ScheduleExcelValue> read, bool isCount = false)
        {
            Column = column;
            _read = read;
            IsCount = isCount;
        }

        public ScheduleExcelColumn Column { get; }

        public bool IsCount { get; }

        public ScheduleExcelValue Read(Element element) => _read(element);

        public static FieldReader Count(ScheduleExcelColumn column) =>
            new(column, _ => new ScheduleExcelValue(1d, false, true), isCount: true);

        public static FieldReader Unsupported(ScheduleExcelColumn column) =>
            new(column, _ => new ScheduleExcelValue(null, false, true));
    }

    private sealed class SortKeyComparer : IComparer<ElementValues>
    {
        private readonly bool[] _descending;

        public SortKeyComparer(bool[] descending)
        {
            _descending = descending;
        }

        public int Compare(ElementValues? x, ElementValues? y)
        {
            if (x == null || y == null)
            {
                return 0;
            }

            for (int index = 0; index < _descending.Length; index++)
            {
                int result = CompareValues(x.SortKey[index], y.SortKey[index]);
                if (result != 0)
                {
                    return _descending[index] ? -result : result;
                }
            }

            return 0;
        }

        private static int CompareValues(object? left, object? right)
        {
            if (left is double a && right is double b)
            {
                return a.CompareTo(b);
            }

            return string.Compare(
                Convert.ToString(left, CultureInfo.CurrentCulture),
                Convert.ToString(right, CultureInfo.CurrentCulture),
                StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
