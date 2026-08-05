using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.HvacTagging.Models;

namespace HCB.RevitAddin.Features.HvacTagging.UI;

public partial class HvacTaggingWindow : Window
{
    private readonly Document _document;
    private readonly View _activeView;
    private readonly HvacTaggingService _service = new();
    private readonly HvacTaggingOptions _options;
    private IReadOnlyList<HvacTagPreviewRow> _previewRows = [];

    public HvacTaggingWindow(Document document, View activeView)
    {
        _document = document;
        _activeView = activeView;
        _options = new HvacTaggingOptions
        {
            CategoryRules = _service.CreateDefaultRules(document).ToList()
        };

        InitializeComponent();
        RulesItemsControl.ItemsSource = _options.CategoryRules;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (!TryUpdateOptionsFromUi())
        {
            return;
        }

        _previewRows = _service.BuildPreview(_document, _activeView, _options);
        PreviewDataGrid.ItemsSource = _previewRows;

        int ready = _previewRows.Count(row => row.CanCreateTag);
        int existing = _previewRows.Count(row => row.HasExistingTag && !row.IsDuplicate);
        int duplicates = _previewRows.Count(row => row.IsDuplicate);
        int blocked = _previewRows.Count(row => row.Element != null && !row.CanCreateTag && !row.HasExistingTag && !row.IsDuplicate);

        FooterBar.StatusText = $"Do utworzenia: {ready}. Istniejace: {existing}. Duplikaty: {duplicates}. Blokady: {blocked}.";
    }

    private bool TryUpdateOptionsFromUi()
    {
        if (!IsLoaded)
        {
            return true;
        }

        if (!TryParseMillimeters(OffsetXTextBox.Text, out double offsetX))
        {
            FooterBar.StatusText = "Offset X musi byc liczba w milimetrach.";
            return false;
        }

        if (!TryParseMillimeters(OffsetYTextBox.Text, out double offsetY))
        {
            FooterBar.StatusText = "Offset Y musi byc liczba w milimetrach.";
            return false;
        }

        _options.OffsetXMillimeters = offsetX;
        _options.OffsetYMillimeters = offsetY;
        _options.UseLeader = UseLeaderCheckBox.IsChecked == true;
        return true;
    }

    private static bool TryParseMillimeters(string text, out double value)
    {
        string normalized = (text ?? string.Empty).Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void CreateTagsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryUpdateOptionsFromUi())
        {
            return;
        }

        int ready = _previewRows.Count(row => row.CanCreateTag);
        if (ready == 0)
        {
            FooterBar.StatusText = "Brak tagow do utworzenia.";
            return;
        }

        try
        {
            HvacTaggingResult result = _service.Apply(_document, _previewRows, _options);
            ShowResult(result);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            TaskDialog.Show("HVAC Tags", $"Nie udalo sie utworzyc tagow.\n\n{ex.Message}");
        }
    }

    private static void ShowResult(HvacTaggingResult result)
    {
        string details = string.Join(Environment.NewLine, result.Messages.Take(12));
        string content =
            $"Widoki: {result.ViewsCount}{Environment.NewLine}" +
            $"Elementy: {result.CandidatesCount}{Environment.NewLine}" +
            $"Utworzone tagi: {result.CreatedCount}{Environment.NewLine}" +
            $"Istniejace tagi: {result.ExistingCount}{Environment.NewLine}" +
            $"Duplikaty: {result.DuplicateCount}{Environment.NewLine}" +
            $"Blokady: {result.BlockedCount}{Environment.NewLine}" +
            $"Bledy: {result.FailedCount}";

        if (!string.IsNullOrWhiteSpace(details))
        {
            content += $"{Environment.NewLine}{Environment.NewLine}Szczegoly:{Environment.NewLine}{details}";
            if (result.Messages.Count > 12)
            {
                content += $"{Environment.NewLine}...";
            }
        }

        TaskDialog.Show("HVAC Tags", content);
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Options_OnChanged(object sender, RoutedEventArgs e)
    {
        RefreshPreview();
    }

    private void TagTypeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshPreview();
    }
}
