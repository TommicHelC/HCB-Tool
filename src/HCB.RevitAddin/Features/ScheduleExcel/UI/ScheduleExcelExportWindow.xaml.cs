using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace HCB.RevitAddin.Features.ScheduleExcel.UI;

public sealed class ScheduleExcelListItem
{
    public ScheduleExcelListItem(long id, string name, string info)
    {
        Id = id;
        Name = name;
        Info = info;
    }

    public long Id { get; }

    public string Name { get; }

    public string Info { get; }

    /// <summary>Ticked for export; several schedules go to one workbook, one sheet each.</summary>
    public bool IsChecked { get; set; }
}

/// <summary>Export shows file options; Sync shows editing options (the file always opens, always one file).</summary>
public enum ScheduleExcelWindowMode
{
    Export,
    Sync
}

public partial class ScheduleExcelExportWindow : Window
{
    private readonly IReadOnlyList<ScheduleExcelListItem> _items;
    private readonly ScheduleExcelWindowMode _mode;

    public ScheduleExcelExportWindow(
        ScheduleExcelWindowMode mode,
        string title,
        string description,
        string primaryButton,
        IReadOnlyList<ScheduleExcelListItem> items,
        IReadOnlyCollection<long> preselectedIds,
        string folder,
        bool openAfterExport,
        bool protectReadOnlyCells,
        bool expandGroups,
        bool addIdColumn,
        bool separateFiles)
    {
        InitializeComponent();

        Title = title;
        Header.Title = title;
        Header.Description = description;
        FooterBar.PrimaryButtonText = primaryButton;
        _items = items;
        foreach (ScheduleExcelListItem item in _items)
        {
            item.IsChecked = preselectedIds.Contains(item.Id);
        }

        ScheduleListBox.ItemsSource = _items;
        ScheduleListBox.SelectedItem = _items.FirstOrDefault(item => item.IsChecked) ?? _items.FirstOrDefault();
        if (ScheduleListBox.SelectedItem != null)
        {
            ScheduleListBox.ScrollIntoView(ScheduleListBox.SelectedItem);
        }

        FolderTextBox.Text = folder;
        OpenAfterExportCheckBox.IsChecked = openAfterExport;
        ProtectCheckBox.IsChecked = protectReadOnlyCells;
        ExpandGroupsCheckBox.IsChecked = expandGroups;
        IdColumnCheckBox.IsChecked = addIdColumn;
        SeparateFilesCheckBox.IsChecked = separateFiles;

        bool export = mode == ScheduleExcelWindowMode.Export;
        Collapse(!export, OpenAfterExportCheckBox, SeparateFilesCheckBox);
        Collapse(export, ProtectCheckBox, ExpandGroupsCheckBox, ExpandGroupsHintText);
        _mode = mode;
        UpdateFileHint();
        UpdateStatus();
    }

    /// <summary>Ticked schedules; when none is ticked, the highlighted one.</summary>
    public IReadOnlyList<ScheduleExcelListItem> SelectedSchedules
    {
        get
        {
            List<ScheduleExcelListItem> ticked = _items.Where(item => item.IsChecked).ToList();
            if (ticked.Count == 0 && ScheduleListBox.SelectedItem is ScheduleExcelListItem highlighted)
            {
                ticked.Add(highlighted);
            }

            return ticked;
        }
    }

    public string Folder => FolderTextBox.Text.Trim();

    public bool OpenAfterExport => OpenAfterExportCheckBox.IsChecked == true;

    public bool ProtectReadOnlyCells => ProtectCheckBox.IsChecked == true;

    public bool ExpandGroups => ExpandGroupsCheckBox.IsChecked == true;

    public bool AddIdColumn => IdColumnCheckBox.IsChecked == true;

    /// <summary>Export only: one workbook per ticked schedule.</summary>
    public bool SeparateFiles => _mode == ScheduleExcelWindowMode.Export && SeparateFilesCheckBox.IsChecked == true;

    private static void Collapse(bool collapse, params UIElement[] elements)
    {
        foreach (UIElement element in elements)
        {
            element.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void SeparateFilesCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateFileHint();
        UpdateStatus();
    }

    private void UpdateFileHint()
    {
        FileHintText.Text = SeparateFiles || _items.Count(item => item.IsChecked) <= 1
            ? "Nazwa pliku = nazwa zestawienia. Gdy plik już istnieje, pojawi się pytanie o nadpisanie."
            : "Kilka zestawień trafi do jednego pliku \"<model> - zestawienia.xlsx\", każde do osobnego arkusza.";
    }

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        string filter = SearchTextBox.Text.Trim();
        List<ScheduleExcelListItem> visible = filter.Length == 0
            ? _items.ToList()
            : _items.Where(item => item.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
        ScheduleListBox.ItemsSource = visible;
        ScheduleListBox.SelectedItem = visible.FirstOrDefault(item => item.IsChecked) ?? visible.FirstOrDefault();
    }

    private void ScheduleListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateStatus();
    }

    private void ScheduleCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateFileHint();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int ticked = _items.Count(item => item.IsChecked);
        ScheduleInfoText.Text = (ScheduleListBox.SelectedItem as ScheduleExcelListItem)?.Info ?? string.Empty;
        FooterBar.StatusText = ticked switch
        {
            0 => $"Zestawień w modelu: {_items.Count}. Zaznacz jedno lub kilka.",
            1 => "Zaznaczono 1 zestawienie.",
            _ when SeparateFiles => $"Zaznaczono {ticked} zestawień - każde trafi do osobnego pliku.",
            _ => $"Zaznaczono {ticked} zestawień - każde trafi do osobnego arkusza jednego pliku."
        };
    }

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Folder dla pliku Excel",
            InitialDirectory = System.IO.Directory.Exists(Folder) ? Folder : string.Empty
        };

        if (dialog.ShowDialog(this) == true)
        {
            FolderTextBox.Text = dialog.FolderName;
        }
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedSchedules.Count == 0)
        {
            FooterBar.StatusText = "Zaznacz co najmniej jedno zestawienie.";
            return;
        }

        if (Folder.Length == 0 || !System.IO.Directory.Exists(Folder))
        {
            FooterBar.StatusText = "Wybierz istniejący folder.";
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
