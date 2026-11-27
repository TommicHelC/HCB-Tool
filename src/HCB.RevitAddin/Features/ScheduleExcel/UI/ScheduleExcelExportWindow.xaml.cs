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

public partial class ScheduleExcelExportWindow : Window
{
    private readonly IReadOnlyList<ScheduleExcelListItem> _items;

    public ScheduleExcelExportWindow(
        string title,
        string description,
        string primaryButton,
        IReadOnlyList<ScheduleExcelListItem> items,
        IReadOnlyCollection<long> preselectedIds,
        string folder,
        bool openAfterExport,
        bool protectReadOnlyCells,
        bool expandGroups)
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
