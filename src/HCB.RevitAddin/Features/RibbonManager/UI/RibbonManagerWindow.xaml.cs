using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using HCB.RevitAddin.Features.RibbonManager.Models;
using Microsoft.Win32;

namespace HCB.RevitAddin.Features.RibbonManager.UI;

public partial class RibbonManagerWindow : Window
{
    private const string DefaultCustomTabTitle = "Moja karta";

    private readonly RibbonLayoutConfig savedConfig;
    private RibbonLayoutConfig working;
    private IReadOnlyList<RibbonTabSnapshot> snapshot = Array.Empty<RibbonTabSnapshot>();
    private bool committed;
    private bool refreshing;

    public RibbonManagerWindow()
    {
        InitializeComponent();
        savedConfig = RibbonLayoutService.Current.Clone();
        working = savedConfig.Clone();
        Closing += OnClosing;
        Refresh();
    }

    private void ApplyAndRefresh(string? status = null)
    {
        try
        {
            RibbonLayoutService.Apply(working);
            FooterBar.StatusText = status ?? string.Empty;
        }
        catch (Exception exception)
        {
            FooterBar.StatusText = "Błąd: " + exception.Message;
        }

        Refresh();
    }

    private void Refresh(string? selectTabId = null)
    {
        refreshing = true;
        try
        {
            string? selectedTabId = selectTabId ?? (TabsListBox.SelectedItem as TabItem)?.Id;
            HashSet<string> selectedPanels = PanelsListBox.SelectedItems.OfType<PanelItem>().Select(item => item.Key).ToHashSet();

            snapshot = RibbonLayoutService.Snapshot();
            List<TabItem> tabs = snapshot.Select(CreateTabItem).ToList();
            TabsListBox.ItemsSource = tabs;
            TabsListBox.SelectedItem = tabs.FirstOrDefault(tab => tab.Id == selectedTabId) ?? tabs.FirstOrDefault();

            List<TabItem> targets = tabs.Where(tab => !tab.IsHiddenByUser).ToList();
            ResetTargets(MoveTargetComboBox, targets);
            ResetTargets(MergeTargetComboBox, targets);

            ShortNamesCheckBox.IsChecked = working.ShortPanelNames;
            ShortTabNamesCheckBox.IsChecked = working.ShortTabNames;
            HideEmptyTabsCheckBox.IsChecked = working.HideEmptyTabs;

            RefreshPanels(selectedPanels);
        }
        finally
        {
            refreshing = false;
        }
    }

    private static void ResetTargets(ComboBox comboBox, List<TabItem> targets)
    {
        string? previousId = (comboBox.SelectedItem as TabItem)?.Id;
        comboBox.ItemsSource = targets;
        comboBox.SelectedItem = targets.FirstOrDefault(tab => tab.Id == previousId)
            ?? targets.FirstOrDefault(tab => tab.IsCustom);
    }

    private void RefreshPanels(ISet<string>? keepSelected = null)
    {
        TabItem? tab = TabsListBox.SelectedItem as TabItem;
        RibbonTabSnapshot? source = tab == null ? null : snapshot.FirstOrDefault(item => item.Id == tab.Id);
        List<PanelItem> panels = source?.Panels.Select(CreatePanelItem).ToList() ?? new List<PanelItem>();
        PanelsListBox.ItemsSource = panels;

        if (keepSelected != null)
        {
            foreach (PanelItem panel in panels.Where(panel => keepSelected.Contains(panel.Key)))
            {
                PanelsListBox.SelectedItems.Add(panel);
            }
        }

        TabTitleTextBox.Text = tab?.Title ?? string.Empty;
        UpdatePanelTitleBox();
    }

    private TabItem CreateTabItem(RibbonTabSnapshot tab)
    {
        List<string> tags = new();
        if (tab.IsCustom)
        {
            tags.Add("własna");
        }

        if (!string.Equals(tab.Title, tab.OriginalTitle, StringComparison.Ordinal) && !tab.IsCustom)
        {
            tags.Add("oryg. " + tab.OriginalTitle);
        }

        if (!tab.IsHidden && working.HideEmptyTabs && tab.Panels.All(panel => panel.IsHidden))
        {
            tags.Add("pusta – ukryta");
        }

        string display = tags.Count == 0 ? tab.Title : $"{tab.Title}  ({string.Join(", ", tags)})";
        return new TabItem(tab.Id, tab.Title, display, !tab.IsHidden, tab.IsHidden, tab.IsCustom);
    }

    private static PanelItem CreatePanelItem(RibbonPanelSnapshot panel)
    {
        List<string> details = new();
        if (!string.Equals(panel.Title, panel.OriginalTitle, StringComparison.Ordinal))
        {
            details.Add(panel.OriginalTitle);
        }

        details.Add(panel.IsMovable ? "z: " + panel.OriginalTabTitle : "natywny");
        if (panel.HiddenItems.Count > 0)
        {
            details.Add($"ukryte narzędzia: {panel.HiddenItems.Count}");
        }

        return new PanelItem(panel.Key, panel.Title, string.Join(" · ", details), !panel.IsHidden, panel.IsMovable);
    }

    private void UpdatePanelTitleBox()
    {
        PanelTitleTextBox.Text = PanelsListBox.SelectedItems.Count == 1 && PanelsListBox.SelectedItem is PanelItem panel
            ? panel.Title
            : string.Empty;
    }

    private void TabsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing)
        {
            RefreshPanels();
        }
    }

    private void PanelsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePanelTitleBox();
    }

    private void TabVisibleCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: TabItem tab })
        {
            return;
        }

        Toggle(working.HiddenTabs, tab.Id, hide: tab.IsVisible);
        TabsListBox.SelectedItem = tab;
        ApplyAndRefresh();
    }

    private void PanelVisibleCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: PanelItem panel })
        {
            return;
        }

        Toggle(working.HiddenPanels, panel.Key, hide: panel.IsVisible);
        ApplyAndRefresh();
    }

    private void RenameTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TabsListBox.SelectedItem is not TabItem tab)
        {
            return;
        }

        string title = TabTitleTextBox.Text.Trim();
        RibbonCustomTab? custom = working.CustomTabs.FirstOrDefault(item => item.Id == tab.Id);
        if (custom != null)
        {
            if (title.Length > 0)
            {
                custom.Title = title;
            }
        }
        else
        {
            RibbonTabSnapshot? source = snapshot.FirstOrDefault(item => item.Id == tab.Id);
            SetOrRemove(working.TabTitles, tab.Id, title.Length == 0 ? null : title);
        }

        ApplyAndRefresh("Zmieniono nazwę karty.");
    }

    private void NewTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        string title = TabTitleTextBox.Text.Trim();
        if (title.Length == 0 || snapshot.Any(tab => tab.Title == title))
        {
            title = DefaultCustomTabTitle;
        }

        RibbonCustomTab custom = new() { Id = RibbonLayoutService.CreateCustomTabId(), Title = title };
        working.CustomTabs.Add(custom);
        ApplyAndRefresh("Dodano kartę. Przenieś do niej panele (pusta karta jest ukryta).");
        Refresh(custom.Id);
    }

    private void DeleteTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TabsListBox.SelectedItem is not TabItem { IsCustom: true } tab)
        {
            FooterBar.StatusText = "Usunąć można tylko własną kartę.";
            return;
        }

        working.CustomTabs.RemoveAll(item => item.Id == tab.Id);
        foreach (string key in working.PanelMoves.Where(pair => pair.Value == tab.Id).Select(pair => pair.Key).ToList())
        {
            working.PanelMoves.Remove(key);
        }

        working.TabTitles.Remove(tab.Id);
        working.HiddenTabs.Remove(tab.Id);
        ApplyAndRefresh("Usunięto kartę, panele wróciły na miejsce.");
    }

    private void MergeTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TabsListBox.SelectedItem is not TabItem source || MergeTargetComboBox.SelectedItem is not TabItem target || source.Id == target.Id)
        {
            FooterBar.StatusText = "Wybierz kartę źródłową na liście i inną kartę docelową.";
            return;
        }

        RibbonTabSnapshot? tab = snapshot.FirstOrDefault(item => item.Id == source.Id);
        if (tab == null)
        {
            return;
        }

        int moved = MovePanels(tab.Panels.Where(panel => panel.IsMovable).Select(panel => panel.Key), target.Id);
        int skipped = tab.Panels.Count(panel => !panel.IsMovable);
        ApplyAndRefresh(skipped > 0
            ? $"Przeniesiono {moved} paneli; pominięto natywne: {skipped}."
            : $"Przeniesiono {moved} paneli do „{target.Title}”.");
    }

    private void MovePanelsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (MoveTargetComboBox.SelectedItem is not TabItem target)
        {
            FooterBar.StatusText = "Wybierz kartę docelową.";
            return;
        }

        List<PanelItem> selected = PanelsListBox.SelectedItems.OfType<PanelItem>().ToList();
        if (selected.Count == 0)
        {
            FooterBar.StatusText = "Zaznacz panele do przeniesienia.";
            return;
        }

        int moved = MovePanels(selected.Where(panel => panel.IsMovable).Select(panel => panel.Key), target.Id);
        int skipped = selected.Count(panel => !panel.IsMovable);
        ApplyAndRefresh(skipped > 0
            ? $"Przeniesiono {moved}; panele natywne nie są przenoszone ({skipped})."
            : $"Przeniesiono {moved} do „{target.Title}”.");
    }

    private int MovePanels(IEnumerable<string> panelKeys, string targetTabId)
    {
        int count = 0;
        foreach (string key in panelKeys)
        {
            string? originalTabId = RibbonLayoutService.GetOriginalTabId(key);
            SetOrRemove(working.PanelMoves, key, originalTabId == targetTabId ? null : targetTabId);
            count++;
        }

        return count;
    }

    private void ShowHiddenItemsButton_OnClick(object sender, RoutedEventArgs e)
    {
        HashSet<string> keys = PanelsListBox.SelectedItems.OfType<PanelItem>().Select(panel => panel.Key).ToHashSet();
        if (keys.Count == 0)
        {
            FooterBar.StatusText = "Zaznacz panele.";
            return;
        }

        HashSet<string> ids = snapshot
            .SelectMany(tab => tab.Panels)
            .Where(panel => keys.Contains(panel.Key))
            .SelectMany(panel => panel.HiddenItems)
            .Select(item => item.Id)
            .ToHashSet();

        int removed = working.HiddenItems.RemoveAll(ids.Contains);
        ApplyAndRefresh(removed == 0 ? "W zaznaczonych panelach nie ma ukrytych narzędzi." : $"Przywrócono narzędzia: {removed}.");
    }

    private void RenamePanelButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (PanelsListBox.SelectedItems.Count != 1 || PanelsListBox.SelectedItem is not PanelItem panel)
        {
            FooterBar.StatusText = "Zaznacz jeden panel.";
            return;
        }

        string title = PanelTitleTextBox.Text.Trim();
        SetOrRemove(working.PanelTitles, panel.Key, title.Length == 0 ? null : title);
        ApplyAndRefresh("Zmieniono nazwę panelu.");
    }

    private void OptionCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        working.ShortPanelNames = ShortNamesCheckBox.IsChecked == true;
        working.ShortTabNames = ShortTabNamesCheckBox.IsChecked == true;
        working.HideEmptyTabs = HideEmptyTabsCheckBox.IsChecked == true;
        ApplyAndRefresh();
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Filter = "Układ wstążki (*.json)|*.json",
            FileName = "HCB_RibbonLayout.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            RibbonLayoutStore.Write(dialog.FileName, working);
            FooterBar.StatusText = "Wyeksportowano układ.";
        }
        catch (Exception exception)
        {
            FooterBar.StatusText = "Błąd eksportu: " + exception.Message;
        }
    }

    private void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = "Układ wstążki (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            working = RibbonLayoutStore.Read(dialog.FileName);
            ApplyAndRefresh("Zaimportowano układ. Zapisz, aby go zachować.");
        }
        catch (Exception exception)
        {
            FooterBar.StatusText = "Błąd importu: " + exception.Message;
        }
    }

    private void ResetButton_OnClick(object sender, RoutedEventArgs e)
    {
        working = new RibbonLayoutConfig();
        ApplyAndRefresh("Przywrócono oryginalny układ. Zapisz, aby go zachować.");
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            RibbonLayoutStore.Save(working);
            committed = true;
            Close();
        }
        catch (Exception exception)
        {
            FooterBar.StatusText = "Błąd zapisu: " + exception.Message;
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!committed)
        {
            RibbonLayoutService.TryApply(savedConfig);
        }
    }

    private static void Toggle(List<string> list, string key, bool hide)
    {
        list.Remove(key);
        if (hide)
        {
            list.Add(key);
        }
    }

    private static void SetOrRemove(Dictionary<string, string> map, string key, string? value)
    {
        if (value == null)
        {
            map.Remove(key);
        }
        else
        {
            map[key] = value;
        }
    }

    public sealed record TabItem(string Id, string Title, string Display, bool IsVisible, bool IsHiddenByUser, bool IsCustom);

    public sealed record PanelItem(string Key, string Title, string Details, bool IsVisible, bool IsMovable);
}
