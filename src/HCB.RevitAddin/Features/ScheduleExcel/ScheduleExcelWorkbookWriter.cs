using System;
using ClosedXML.Excel;
using HCB.RevitAddin.Features.ScheduleExcel.Models;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>
/// Writes the schedule workbook: a visible data sheet laid out like the Revit schedule (title, group
/// headers/footers, Revit colours, an Excel table with a native totals row), a legend and very hidden
/// metadata sheets (<see cref="ScheduleExcelFormat"/>). No Revit API calls here.
/// </summary>
internal static class ScheduleExcelWorkbookWriter
{
    private static readonly XLColor GridColor = XLColor.FromHtml("#BFBFBF");
    private static readonly XLColor ReadOnlyFont = XLColor.FromHtml("#7F7F7F");
    private static readonly XLColor EditableFont = XLColor.FromHtml("#1E7A38");
    private static readonly XLColor TypeFont = XLColor.FromHtml("#1F5FAD");
    private static readonly XLColor MixedFill = XLColor.FromHtml("#FFF4CC");
    private static readonly XLColor GroupFill = XLColor.FromHtml("#F2F2F2");

    private const double MaxColumnWidth = 45;

    public static void Write(ScheduleExcelData data, ScheduleExcelExportOptions options)
    {
        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.Worksheets.Add(ToSheetName(data.ScheduleName));
        WriteDataSheet(sheet, data, options);
        WriteLegend(workbook.Worksheets.Add(ScheduleExcelFormat.LegendSheet), data, options);
        WriteMeta(workbook.Worksheets.Add(ScheduleExcelFormat.MetaSheet), data, sheet.Name);
        WriteFields(workbook.Worksheets.Add(ScheduleExcelFormat.FieldsSheet), data);
        WriteRows(workbook.Worksheets.Add(ScheduleExcelFormat.RowsSheet), data);
        WriteSnapshot(workbook.Worksheets.Add(ScheduleExcelFormat.SnapshotSheet), data);

        foreach (string name in new[] { ScheduleExcelFormat.MetaSheet, ScheduleExcelFormat.FieldsSheet, ScheduleExcelFormat.RowsSheet, ScheduleExcelFormat.SnapshotSheet })
        {
            workbook.Worksheet(name).Visibility = XLWorksheetVisibility.VeryHidden;
        }

        sheet.SetTabActive();
        workbook.SaveAs(options.FilePath);
    }

    private static void WriteDataSheet(IXLWorksheet sheet, ScheduleExcelData data, ScheduleExcelExportOptions options)
    {
        int columnCount = data.Columns.Count;
        int firstColumn = ScheduleExcelFormat.FirstValueColumn;
        int lastColumn = firstColumn + Math.Max(columnCount, 1) - 1;

        WriteTitle(sheet, data, firstColumn, lastColumn);
        WriteEditState(sheet, data);
        WriteHeadings(sheet, data);

        List<int> dataRows = WriteBody(sheet, data);
        int lastRow = Math.Max(ScheduleExcelFormat.FirstDataRow, sheet.LastRowUsed()?.RowNumber() ?? ScheduleExcelFormat.FirstDataRow);

        IXLTable table = sheet.Range(ScheduleExcelFormat.HeaderRow, ScheduleExcelFormat.KeyColumn, lastRow, lastColumn)
            .CreateTable(ScheduleExcelFormat.TableName);
        table.Theme = XLTableTheme.None;
        table.ShowRowStripes = false;
        table.ShowAutoFilter = true;

        bool anyTotals = data.Columns.Any(column => column.TotalFunction.Length > 0);
        int totalsRow = 0;
        if (data.ShowGrandTotal && anyTotals)
        {
            table.ShowTotalsRow = true;
            totalsRow = table.TotalsRow().RowNumber();
            for (int index = 0; index < columnCount; index++)
            {
                ScheduleExcelColumn column = data.Columns[index];
                IXLTableField field = table.Field(index + 1);
                field.TotalsRowFunction = column.TotalFunction switch
                {
                    "Sum" => XLTotalsRowFunction.Sum,
                    "Min" => XLTotalsRowFunction.Minimum,
                    "Max" => XLTotalsRowFunction.Maximum,
                    _ => XLTotalsRowFunction.None
                };
                if (column.NumberFormat.Length > 0)
                {
                    sheet.Cell(totalsRow, firstColumn + index).Style.NumberFormat.Format = column.NumberFormat;
                }
            }

            if (columnCount > 0 && data.Columns[0].TotalFunction.Length == 0)
            {
                table.Field(1).TotalsRowLabel = data.GrandTotalTitle.Length > 0 ? data.GrandTotalTitle : "Suma";
            }

            IXLRange totals = sheet.Range(totalsRow, firstColumn, totalsRow, lastColumn);
            totals.Style.Font.Bold = true;
            totals.Style.Border.TopBorder = XLBorderStyleValues.Double;
        }

        int gridLastRow = totalsRow > 0 ? totalsRow : lastRow;
        IXLRange grid = sheet.Range(ScheduleExcelFormat.HeaderRow, firstColumn, gridLastRow, lastColumn);
        grid.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        grid.Style.Border.InsideBorderColor = GridColor;
        grid.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        grid.Style.Border.OutsideBorderColor = GridColor;

        SetColumnWidths(sheet, data, lastRow);
        sheet.SheetView.FreezeRows(ScheduleExcelFormat.HeaderRow);
        sheet.Column(ScheduleExcelFormat.KeyColumn).Hide();

        if (options.ProtectReadOnlyCells)
        {
            for (int rowIndex = 0; rowIndex < dataRows.Count; rowIndex++)
            {
                int row = dataRows[rowIndex];
                ScheduleExcelValue[] values = data.Rows[rowIndex].Values;
                for (int index = 0; index < columnCount; index++)
                {
                    sheet.Cell(row, firstColumn + index).Style.Protection.Locked = data.Columns[index].IsReadOnly || values[index].ReadOnly;
                }
            }

            sheet.Protect()
                .AllowElement(XLSheetProtectionElements.AutoFilter)
                .AllowElement(XLSheetProtectionElements.FormatColumns)
                .AllowElement(XLSheetProtectionElements.FormatRows)
                .AllowElement(XLSheetProtectionElements.SelectEverything);
        }
    }

    private static void WriteTitle(IXLWorksheet sheet, ScheduleExcelData data, int firstColumn, int lastColumn)
    {
        IXLRange title = sheet.Range(ScheduleExcelFormat.TitleRow, firstColumn, ScheduleExcelFormat.TitleRow, lastColumn);
        title.Merge();
        title.FirstCell().Value = data.Title;
        title.Style.Font.FontSize = 13;
        title.Style.Font.Bold = true;
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ApplyStyle(title.Style, data.TitleStyle, applyBold: false);
        sheet.Row(ScheduleExcelFormat.TitleRow).Height = 22;
    }

    private static void WriteEditState(IXLWorksheet sheet, ScheduleExcelData data)
    {
        for (int index = 0; index < data.Columns.Count; index++)
        {
            ScheduleExcelColumn column = data.Columns[index];
            IXLCell cell = sheet.Cell(ScheduleExcelFormat.EditStateRow, ScheduleExcelFormat.FirstValueColumn + index);
            (string text, XLColor color) = column.IsReadOnly
                ? ("tylko odczyt", ReadOnlyFont)
                : column.IsTypeParameter ? ("edycja: typ", TypeFont) : ("edycja", EditableFont);
            cell.Value = text;
            cell.Style.Font.FontColor = color;
            cell.Style.Font.FontSize = 8;
            cell.Style.Font.Italic = column.IsReadOnly;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    private static void WriteHeadings(IXLWorksheet sheet, ScheduleExcelData data)
    {
        sheet.Cell(ScheduleExcelFormat.HeaderRow, ScheduleExcelFormat.KeyColumn).Value = ScheduleExcelFormat.KeyHeader;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ScheduleExcelFormat.KeyHeader };
        for (int index = 0; index < data.Columns.Count; index++)
        {
            ScheduleExcelColumn column = data.Columns[index];
            IXLCell cell = sheet.Cell(ScheduleExcelFormat.HeaderRow, ScheduleExcelFormat.FirstValueColumn + index);

            // Excel tables need unique, non-empty headings. [ ] # ' are special in structured references
            // (the totals row formulas), so they are replaced rather than escaped.
            string text = column.UnitLabel.Length > 0 && !column.Header.Contains(column.UnitLabel, StringComparison.Ordinal)
                ? $"{column.Header} ({column.UnitLabel})"
                : column.Header;
            text = text.Replace('[', '(').Replace(']', ')').Replace('#', '№').Replace('\'', '’');
            text = string.IsNullOrWhiteSpace(text) ? $"Kolumna {index + 1}" : text.Trim();
            string unique = text;
            for (int suffix = 2; !used.Add(unique); suffix++)
            {
                unique = $"{text} ({suffix})";
            }

            cell.Value = unique;
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ApplyStyle(cell.Style, column.HeaderStyle ?? column.BodyStyle, applyBold: false);

            string note = BuildHeaderNote(column);
            if (note.Length > 0)
            {
                cell.CreateComment().AddText(note);
            }
        }
    }

    /// <summary>Writes data rows with group headers/footers. Returns the sheet row of each data row, in data order.</summary>
    private static List<int> WriteBody(IXLWorksheet sheet, ScheduleExcelData data)
    {
        int firstColumn = ScheduleExcelFormat.FirstValueColumn;
        int levels = data.GroupLevels.Count;
        var groupStart = new int[levels];
        var groupCount = new int[levels];
        var groupValue = new string[levels];
        var dataRows = new List<int>(data.Rows.Count);
        int row = ScheduleExcelFormat.FirstDataRow;
        ScheduleExcelRow? previous = null;

        foreach (ScheduleExcelRow current in data.Rows)
        {
            int changed = previous == null ? 0 : FirstChangedLevel(previous, current, levels);
            if (previous != null)
            {
                for (int level = levels - 1; level >= changed; level--)
                {
                    row = WriteFooter(sheet, data, level, groupValue[level], groupCount[level], groupStart[level], row);
                }
            }

            for (int level = changed; level < levels; level++)
            {
                groupValue[level] = current.GroupValues[level];
                groupCount[level] = 0;
                if (data.GroupLevels[level].ShowHeader)
                {
                    IXLCell header = sheet.Cell(row, firstColumn);
                    header.Value = groupValue[level];
                    IXLRange band = sheet.Range(row, firstColumn, row, firstColumn + data.Columns.Count - 1);
                    band.Style.Font.Bold = true;
                    band.Style.Fill.BackgroundColor = GroupFill;
                    row++;
                }

                groupStart[level] = row;
            }

            WriteDataRow(sheet, data, current, row);
            dataRows.Add(row);
            for (int level = 0; level < levels; level++)
            {
                groupCount[level] += current.UniqueIds.Count;
            }

            row++;
            previous = current;
        }

        if (previous != null)
        {
            for (int level = levels - 1; level >= 0; level--)
            {
                row = WriteFooter(sheet, data, level, groupValue[level], groupCount[level], groupStart[level], row);
            }
        }

        return dataRows;
    }

    private static int FirstChangedLevel(ScheduleExcelRow previous, ScheduleExcelRow current, int levels)
    {
        for (int level = 0; level < levels; level++)
        {
            if (!string.Equals(previous.GroupValues[level], current.GroupValues[level], StringComparison.Ordinal))
            {
                return level;
            }
        }

        return levels;
    }

    /// <summary>Group footer with SUBTOTAL formulas; Excel's own SUBTOTALs skip nested SUBTOTAL cells, so totals stay correct.</summary>
    private static int WriteFooter(IXLWorksheet sheet, ScheduleExcelData data, int level, string value, int count, int start, int row)
    {
        ScheduleExcelGroupLevel group = data.GroupLevels[level];
        if (!group.ShowFooter)
        {
            return row;
        }

        int firstColumn = ScheduleExcelFormat.FirstValueColumn;
        int end = row - 1;
        string label = (group.ShowFooterTitle ? value : string.Empty)
            + (group.ShowFooterCount ? $"{(group.ShowFooterTitle ? ": " : string.Empty)}{count}" : string.Empty);
        bool labelWritten = false;
        for (int index = 0; index < data.Columns.Count; index++)
        {
            ScheduleExcelColumn column = data.Columns[index];
            IXLCell cell = sheet.Cell(row, firstColumn + index);
            int function = column.TotalFunction switch { "Sum" => 9, "Min" => 5, "Max" => 4, _ => 0 };
            if (function > 0 && end >= start)
            {
                string letter = cell.WorksheetColumn().ColumnLetter();
                cell.FormulaA1 = $"SUBTOTAL({function},{letter}{start}:{letter}{end})";
                if (column.NumberFormat.Length > 0)
                {
                    cell.Style.NumberFormat.Format = column.NumberFormat;
                }
            }
            else if (!labelWritten && label.Length > 0)
            {
                cell.Value = label;
                labelWritten = true;
            }
        }

        IXLRange band = sheet.Range(row, firstColumn, row, firstColumn + data.Columns.Count - 1);
        band.Style.Font.Bold = true;
        band.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        return row + 1;
    }

    private static void WriteDataRow(IXLWorksheet sheet, ScheduleExcelData data, ScheduleExcelRow dataRow, int row)
    {
        sheet.Cell(row, ScheduleExcelFormat.KeyColumn).Value = dataRow.Key;
        for (int index = 0; index < data.Columns.Count; index++)
        {
            ScheduleExcelColumn column = data.Columns[index];
            ScheduleExcelValue value = dataRow.Values[index];
            IXLCell cell = sheet.Cell(row, ScheduleExcelFormat.FirstValueColumn + index);
            cell.Value = ToCellValue(value.Value);
            if (column.NumberFormat.Length > 0)
            {
                cell.Style.NumberFormat.Format = column.NumberFormat;
            }

            if (value.Value is bool)
            {
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            ScheduleExcelCellStyle? style = dataRow.Styles.Length > index ? dataRow.Styles[index] : null;
            ApplyStyle(cell.Style, style ?? column.BodyStyle, applyBold: true);

            if (value.Mixed && (style ?? column.BodyStyle)?.Background == null)
            {
                cell.Style.Fill.BackgroundColor = MixedFill;
            }

            // A single read-only cell in an editable column (an element without the parameter).
            if (value.ReadOnly && !column.IsReadOnly && (style ?? column.BodyStyle)?.TextColor == null)
            {
                cell.Style.Font.FontColor = ReadOnlyFont;
                cell.Style.Font.Italic = true;
            }
        }
    }

    private static void ApplyStyle(IXLStyle target, ScheduleExcelCellStyle? style, bool applyBold)
    {
        if (style == null)
        {
            return;
        }

        if (style.Background != null)
        {
            target.Fill.BackgroundColor = XLColor.FromHtml(style.Background);
        }

        if (style.TextColor != null)
        {
            target.Font.FontColor = XLColor.FromHtml(style.TextColor);
        }

        if (applyBold || style.Bold)
        {
            target.Font.Bold = style.Bold;
        }

        target.Font.Italic = style.Italic;
        if (style.Underline)
        {
            target.Font.Underline = XLFontUnderlineValues.Single;
        }

        target.Alignment.Horizontal = style.HorizontalAlignment switch
        {
            "Left" => XLAlignmentHorizontalValues.Left,
            "Center" => XLAlignmentHorizontalValues.Center,
            "Right" => XLAlignmentHorizontalValues.Right,
            _ => target.Alignment.Horizontal
        };
    }

    private static void SetColumnWidths(IXLWorksheet sheet, ScheduleExcelData data, int lastRow)
    {
        for (int index = 0; index < data.Columns.Count; index++)
        {
            IXLColumn column = sheet.Column(ScheduleExcelFormat.FirstValueColumn + index);
            column.AdjustToContents(ScheduleExcelFormat.FirstDataRow, Math.Min(lastRow, 300));

            // Headings wrap; give them room for their longest word only.
            string heading = sheet.Cell(ScheduleExcelFormat.HeaderRow, column.ColumnNumber()).GetString();
            int longestWord = heading.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(word => word.Length).DefaultIfEmpty(0).Max();
            column.Width = Math.Clamp(Math.Max(column.Width, longestWord + 3) + 1, 9, MaxColumnWidth);
        }
    }

    private static string BuildHeaderNote(ScheduleExcelColumn column)
    {
        var lines = new List<string>();
        if (column.ParameterName.Length > 0 && column.ParameterName != column.Header)
        {
            lines.Add($"Parametr: {column.ParameterName}");
        }

        if (column.IsTypeParameter)
        {
            lines.Add("Parametr typu: zmiana dotyczy wszystkich elementów tego typu.");
        }

        if (column.IsReadOnly)
        {
            lines.Add($"Tylko do odczytu. {column.ReadOnlyReason}".TrimEnd());
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void WriteLegend(IXLWorksheet sheet, ScheduleExcelData data, ScheduleExcelExportOptions options)
    {
        int row = 1;
        sheet.Cell(row, 1).Value = "Eksport zestawienia HCB";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 14;
        row += 2;

        foreach ((string label, string value) in new[]
        {
            ("Zestawienie", data.ScheduleName),
            ("Model", data.DocumentTitle),
            ("Data eksportu", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            ("Wiersze / elementy", $"{data.Rows.Count} / {data.ElementCount}"),
            ("Tryb", data.IsItemized ? "każdy element w osobnym wierszu" : "wiersze grupowane jak w zestawieniu")
        })
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 2).Value = value;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            row++;
        }

        row++;
        sheet.Cell(row, 1).Value = "Wiersz nad nagłówkami";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        AddLegendRow(sheet, row++, "edycja", "Parametr egzemplarza - zmiana wróci do elementu w Revit.", EditableFont, null);
        AddLegendRow(sheet, row++, "edycja: typ", "Parametr typu - zmiana dotyczy wszystkich elementów tego typu.", TypeFont, null);
        AddLegendRow(sheet, row++, "tylko odczyt", "Wartość informacyjna (formuła, pole wyliczane, parametr tylko do odczytu).", ReadOnlyFont, null);
        AddLegendRow(sheet, row++, "Różne wartości", "Elementy w grupowanym wierszu mają różne wartości.", null, MixedFill);

        row++;
        sheet.Cell(row++, 1).Value = "Kolory, pogrubienia i wyrównanie pochodzą z wyglądu zestawienia w Revit.";
        sheet.Cell(row++, 1).Value = "Sumy liczy Excel (wiersz sum tabeli i SUMY.CZĘŚCIOWE w stopkach grup) - uwzględniają filtr.";
        sheet.Cell(row++, 1).Value = "Nie usuwaj ukrytej kolumny A ani ukrytych arkuszy - łączą wiersze z elementami w Revit.";
        if (options.ProtectReadOnlyCells)
        {
            sheet.Cell(row++, 1).Value = "Arkusz jest chroniony (bez hasła): komórki tylko do odczytu są zablokowane, sortowanie wyłączone.";
        }

        sheet.Column(1).Width = 24;
        sheet.Column(2).Width = 80;
    }

    private static void AddLegendRow(IXLWorksheet sheet, int row, string label, string description, XLColor? font, XLColor? fill)
    {
        IXLCell sample = sheet.Cell(row, 1);
        sample.Value = label;
        if (font != null)
        {
            sample.Style.Font.FontColor = font;
        }

        if (fill != null)
        {
            sample.Style.Fill.BackgroundColor = fill;
        }

        sample.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sheet.Cell(row, 2).Value = description;
    }

    private static void WriteMeta(IXLWorksheet sheet, ScheduleExcelData data, string dataSheetName)
    {
        var entries = new (string Key, string Value)[]
        {
            ("Tool", ScheduleExcelFormat.ToolId),
            ("FormatVersion", ScheduleExcelFormat.FormatVersion.ToString(CultureInfo.InvariantCulture)),
            ("DataSheet", dataSheetName),
            ("TableName", ScheduleExcelFormat.TableName),
            ("ScheduleName", data.ScheduleName),
            ("ScheduleUniqueId", data.ScheduleUniqueId),
            ("DocumentTitle", data.DocumentTitle),
            ("DocumentPath", data.DocumentPath),
            ("DocumentId", data.DocumentId),
            ("IsItemized", data.IsItemized ? "1" : "0"),
            ("RowCount", data.Rows.Count.ToString(CultureInfo.InvariantCulture)),
            ("ElementCount", data.ElementCount.ToString(CultureInfo.InvariantCulture)),
            ("ExportedAtUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
        };

        for (int index = 0; index < entries.Length; index++)
        {
            sheet.Cell(index + 1, 1).Value = entries[index].Key;
            sheet.Cell(index + 1, 2).Value = Truncate(entries[index].Value);
        }
    }

    private static void WriteFields(IXLWorksheet sheet, ScheduleExcelData data)
    {
        string[] headers =
        [
            "Column", "Header", "FieldIndex", "FieldType", "ParameterId", "ParameterName", "ParameterGuid",
            "IsTypeParameter", "StorageType", "ValueKind", "SpecTypeId", "UnitTypeId", "UnitLabel", "ReadOnly",
            "ReadOnlyReason", "FromScheduleText", "TotalFunction"
        ];
        for (int index = 0; index < headers.Length; index++)
        {
            sheet.Cell(1, index + 1).Value = headers[index];
        }

        for (int index = 0; index < data.Columns.Count; index++)
        {
            ScheduleExcelColumn column = data.Columns[index];
            int row = index + 2;
            object[] values =
            [
                ScheduleExcelFormat.FirstValueColumn + index, column.Header, column.FieldIndex, column.FieldType,
                column.ParameterId.ToString(CultureInfo.InvariantCulture), column.ParameterName, column.ParameterGuid,
                column.IsTypeParameter ? "1" : "0", column.StorageType, column.ValueKind.ToString(), column.SpecTypeId,
                column.UnitTypeId, column.UnitLabel, column.IsReadOnly ? "1" : "0", column.ReadOnlyReason,
                column.FromScheduleText ? "1" : "0", column.TotalFunction
            ];
            for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
            {
                sheet.Cell(row, valueIndex + 1).Value = values[valueIndex] is int number
                    ? number
                    : Convert.ToString(values[valueIndex], CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }
    }

    /// <summary>One line per element: grouped rows can hold thousands of ids, more than one cell can take.</summary>
    private static void WriteRows(IXLWorksheet sheet, ScheduleExcelData data)
    {
        sheet.Cell(1, 1).Value = "RowKey";
        sheet.Cell(1, 2).Value = "UniqueId";
        int row = 2;
        foreach (ScheduleExcelRow dataRow in data.Rows)
        {
            foreach (string uniqueId in dataRow.UniqueIds)
            {
                sheet.Cell(row, 1).Value = dataRow.Key;
                sheet.Cell(row, 2).Value = uniqueId;
                row++;
            }
        }
    }

    /// <summary>Values as exported, keyed by row; stage 2 diffs against it to find user edits and conflicts.</summary>
    private static void WriteSnapshot(IXLWorksheet sheet, ScheduleExcelData data)
    {
        sheet.Cell(1, ScheduleExcelFormat.KeyColumn).Value = ScheduleExcelFormat.KeyHeader;
        for (int index = 0; index < data.Columns.Count; index++)
        {
            sheet.Cell(1, ScheduleExcelFormat.FirstValueColumn + index).Value = data.Columns[index].FieldIndex;
        }

        for (int rowIndex = 0; rowIndex < data.Rows.Count; rowIndex++)
        {
            ScheduleExcelRow row = data.Rows[rowIndex];
            int excelRow = rowIndex + 2;
            sheet.Cell(excelRow, ScheduleExcelFormat.KeyColumn).Value = row.Key;
            for (int index = 0; index < row.Values.Length; index++)
            {
                ScheduleExcelValue value = row.Values[index];
                sheet.Cell(excelRow, ScheduleExcelFormat.FirstValueColumn + index).Value = value.Mixed
                    ? ScheduleExcelFormat.MixedMarker
                    : Truncate(ScheduleExcelReader.ToComparableText(value.Value));
            }
        }
    }

    private static XLCellValue ToCellValue(object? value)
    {
        return value switch
        {
            null => Blank.Value,
            double number => number,
            bool flag => flag,
            _ => Truncate(Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty)
        };
    }

    private static string Truncate(string text)
    {
        return text.Length <= ScheduleExcelFormat.MaxCellText ? text : text[..ScheduleExcelFormat.MaxCellText];
    }

    private static string ToSheetName(string scheduleName)
    {
        char[] invalid = [':', '\\', '/', '?', '*', '[', ']'];
        string name = new string(scheduleName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim('\'').Trim();
        if (name.Length == 0 || name.StartsWith("_hcb", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, ScheduleExcelFormat.LegendSheet, StringComparison.OrdinalIgnoreCase))
        {
            name = "Zestawienie";
        }

        return name.Length <= 31 ? name : name[..31];
    }
}
