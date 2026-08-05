using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.CopyBetweenProjects.Models;
using HCB.RevitAddin.UI.Controls;

namespace HCB.RevitAddin.Features.CopyBetweenProjects.UI;

public partial class CopyBetweenProjectsWindow : Window
{
    private const string AllFilterValue = "__ALL__";
    private readonly CopyBetweenProjectsService _service;
    private readonly HashSet<string> _currentSelection = [];
    private List<CopyBetweenProjectsItem> _allItems = [];
    private bool _isUpdatingSelection;

    public CopyBetweenProjectsWindow(IReadOnlyList<Document> documents, CopyBetweenProjectsService service)
    {
        _service = service;
        InitializeComponent();

        SourceDocumentComboBox.ItemsSource = documents;
        SourceDocumentComboBox.DisplayMemberPath = "Title";
        TargetDocumentComboBox.ItemsSource = documents;
        TargetDocumentComboBox.DisplayMemberPath = "Title";
        FooterBar.StatusText = "Wybierz projekt zrodlowy, docelowy i elementy do skopiowania.";
    }

    public Document? SourceDocument => SourceDocumentComboBox.SelectedItem as Document;

    public Document? TargetDocument => TargetDocumentComboBox.SelectedItem as Document;

    public IReadOnlyList<CopyBetweenProjectsItem> SelectedItems => _allItems
        .Where(item => _currentSelection.Contains(item.Key))
        .ToList();

    private void DocumentComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceDocument == null || TargetDocument == null || ReferenceEquals(SourceDocument, TargetDocument))
        {
            _allItems = [];
            _currentSelection.Clear();
            ItemsListBox.ItemsSource = _allItems;
            InitializeFilterGroups();
            return;
        }

        _allItems = _service.GetAvailableItems(SourceDocument).ToList();
        _currentSelection.Clear();
        InitializeFilterGroups();
        ApplyFilters();
    }

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void FilterGroupComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyFilters();
    }

    private void SecondaryFilterGroupComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyFilters();
    }

    private void SelectAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (CopyBetweenProjectsItem item in ItemsListBox.Items.Cast<CopyBetweenProjectsItem>())
        {
            _currentSelection.Add(item.Key);
        }

        ItemsListBox.SelectAll();
    }

    private void SelectNoneButton_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (CopyBetweenProjectsItem item in ItemsListBox.Items.Cast<CopyBetweenProjectsItem>())
        {
            _currentSelection.Remove(item.Key);
        }

        ItemsListBox.UnselectAll();
    }

    private void ItemsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection)
        {
            return;
        }

        foreach (CopyBetweenProjectsItem item in e.RemovedItems.Cast<CopyBetweenProjectsItem>())
        {
            _currentSelection.Remove(item.Key);
        }

        foreach (CopyBetweenProjectsItem item in e.AddedItems.Cast<CopyBetweenProjectsItem>())
        {
            _currentSelection.Add(item.Key);
        }
    }

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SourceDocument == null || TargetDocument == null)
        {
            FooterBar.StatusText = "Wybierz oba projekty.";
            return;
        }

        if (ReferenceEquals(SourceDocument, TargetDocument))
        {
            FooterBar.StatusText = "Projekt zrodlowy i docelowy musza byc rozne.";
            return;
        }

        if (_currentSelection.Count == 0)
        {
            FooterBar.StatusText = "Wybierz co najmniej jedna pozycje.";
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

    private void InitializeFilterGroups()
    {
        List<string> groups = _allItems
            .Select(item => item.FilterGroup)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (groups.Count <= 1)
        {
            PrimaryFilterPanel.Visibility = System.Windows.Visibility.Collapsed;
        }
        else
        {
            List<SelectionListItem> filterOptions = [new SelectionListItem(AllFilterValue, "Wszystkie")];
            filterOptions.AddRange(groups.Select(group => new SelectionListItem(group, group)));
            FilterGroupComboBox.ItemsSource = filterOptions;
            FilterGroupComboBox.SelectedValue = AllFilterValue;
            PrimaryFilterPanel.Visibility = System.Windows.Visibility.Visible;
        }

        List<string> secondaryGroups = _allItems
            .Select(item => item.SecondaryFilterGroup)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (secondaryGroups.Count <= 1)
        {
            SecondaryFilterPanel.Visibility = System.Windows.Visibility.Collapsed;
            return;
        }

        List<SelectionListItem> secondaryOptions = [new SelectionListItem(AllFilterValue, "Wszystkie")];
        secondaryOptions.AddRange(secondaryGroups.Select(group => new SelectionListItem(group, group)));
        SecondaryFilterGroupComboBox.ItemsSource = secondaryOptions;
        SecondaryFilterGroupComboBox.SelectedValue = AllFilterValue;
        SecondaryFilterPanel.Visibility = System.Windows.Visibility.Visible;
    }

    private void ApplyFilters()
    {
        string searchText = SearchTextBox.Text?.Trim() ?? string.Empty;
        string? selectedGroup = PrimaryFilterPanel.Visibility == System.Windows.Visibility.Visible
            ? FilterGroupComboBox.SelectedValue as string
            : null;
        string? selectedSecondaryGroup = SecondaryFilterPanel.Visibility == System.Windows.Visibility.Visible
            ? SecondaryFilterGroupComboBox.SelectedValue as string
            : null;

        List<CopyBetweenProjectsItem> filteredItems = _allItems
            .Where(item => string.IsNullOrWhiteSpace(searchText)
                || item.DisplayName.IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase) >= 0)
            .Where(item => string.IsNullOrWhiteSpace(selectedGroup)
                || string.Equals(selectedGroup, AllFilterValue, StringComparison.Ordinal)
                || string.Equals(item.FilterGroup, selectedGroup, StringComparison.CurrentCultureIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(selectedSecondaryGroup)
                || string.Equals(selectedSecondaryGroup, AllFilterValue, StringComparison.Ordinal)
                || string.Equals(item.SecondaryFilterGroup, selectedSecondaryGroup, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        ItemsListBox.ItemsSource = filteredItems;
        ApplySelectionToVisibleItems(filteredItems);
        FooterBar.StatusText = $"Pozycji do wyboru: {filteredItems.Count}";
    }

    private void ApplySelectionToVisibleItems(IEnumerable<CopyBetweenProjectsItem> visibleItems)
    {
        _isUpdatingSelection = true;
        try
        {
            ItemsListBox.SelectedItems.Clear();
            foreach (CopyBetweenProjectsItem item in visibleItems)
            {
                if (_currentSelection.Contains(item.Key))
                {
                    ItemsListBox.SelectedItems.Add(item);
                }
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }
}
