using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
}

public partial class ScheduleExcelExportWindow : Window
{
    private readonly IReadOnlyList<ScheduleExcelListItem> _items;

    public ScheduleExcelExportWindow(
        IReadOnlyList<ScheduleExcelListItem> items,
        long? preselectedId,
        string folder,
        bool openAfterExport,
        bool protectReadOnlyCells)
    {
        InitializeComponent();

        _items = items;
        ScheduleListBox.ItemsSource = _items;
        ScheduleListBox.SelectedItem = _items.FirstOrDefault(item => item.Id == preselectedId) ?? _items.FirstOrDefault();
        ScheduleListBox.ScrollIntoView(ScheduleListBox.SelectedItem);
        FolderTextBox.Text = folder;
        OpenAfterExportCheckBox.IsChecked = openAfterExport;
        ProtectCheckBox.IsChecked = protectReadOnlyCells;
        FooterBar.StatusText = $"Zestawień w modelu: {_items.Count}.";
    }

    public ScheduleExcelListItem? SelectedSchedule => ScheduleListBox.SelectedItem as ScheduleExcelListItem;

    public string Folder => FolderTextBox.Text.Trim();

    public bool OpenAfterExport => OpenAfterExportCheckBox.IsChecked == true;

    public bool ProtectReadOnlyCells => ProtectCheckBox.IsChecked == true;

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        string filter = SearchTextBox.Text.Trim();
        ScheduleExcelListItem? selected = SelectedSchedule;
        List<ScheduleExcelListItem> visible = filter.Length == 0
            ? _items.ToList()
            : _items.Where(item => item.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
        ScheduleListBox.ItemsSource = visible;
        ScheduleListBox.SelectedItem = visible.Contains(selected!) ? selected : visible.FirstOrDefault();
    }

    private void ScheduleListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ScheduleInfoText.Text = SelectedSchedule?.Info ?? string.Empty;
    }

    private void ScheduleListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedSchedule != null)
        {
            ExportButton_OnClick(sender, e);
        }
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
        if (SelectedSchedule == null)
        {
            FooterBar.StatusText = "Wybierz zestawienie.";
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
