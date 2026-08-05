using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HCB.RevitAddin.Features.ParameterCopy.Models;

namespace HCB.RevitAddin.Features.ParameterCopy.UI;

public partial class ParameterCopyWindow : Window
{
    private readonly ParameterCopyCatalog _allCatalog;
    private readonly ParameterCopyCatalog? _selectedCatalog;

    public ParameterCopyWindow(ParameterCopyCatalog allCatalog, ParameterCopyCatalog? selectedCatalog)
    {
        InitializeComponent();

        _allCatalog = allCatalog;
        _selectedCatalog = selectedCatalog;

        if (_selectedCatalog != null)
        {
            ScopeComboBox.ItemsSource = new[] { "Zaznaczone elementy instalacji", "Wszystkie elementy instalacji" };
            ScopeComboBox.SelectedIndex = 0;
            FooterBar.StatusText = "Wybierz zakres oraz parametry do skopiowania.";
        }
        else
        {
            ScopeComboBox.ItemsSource = new[] { "Wszystkie elementy instalacji" };
            ScopeComboBox.SelectedIndex = 0;
            ScopeComboBox.IsEnabled = false;
            FooterBar.StatusText = "Wybierz parametry do skopiowania dla wszystkich elementow instalacji.";
        }

        RefreshParameterSources();
        UpdatePreview();
    }

    public ParameterCopyOptions Options => new()
    {
        SourceParameterName = SourceParameterComboBox.SelectedItem as string ?? string.Empty,
        TargetParameterName = TargetParameterComboBox.SelectedItem as string ?? string.Empty,
        Scope = GetCurrentScope()
    };

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceParameterComboBox.SelectedItem as string))
        {
            FooterBar.StatusText = "Wybierz parametr zrodlowy.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetParameterComboBox.SelectedItem as string))
        {
            FooterBar.StatusText = "Wybierz parametr docelowy.";
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

    private void ScopeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RefreshParameterSources();
        UpdatePreview();
    }

    private void SourceParameterComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RefreshTargetParameters();
        UpdatePreview();
    }

    private void TargetParameterComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        UpdatePreview();
    }

    private void RefreshParameterSources()
    {
        ParameterCopyCatalog catalog = GetCurrentCatalog();
        string? previousSource = SourceParameterComboBox.SelectedItem as string;

        SourceParameterComboBox.ItemsSource = catalog.SourceParameterNames;
        SourceParameterComboBox.SelectedItem = !string.IsNullOrWhiteSpace(previousSource) && catalog.SourceParameterNames.Contains(previousSource)
            ? previousSource
            : catalog.SourceParameterNames.FirstOrDefault();

        RefreshTargetParameters();
    }

    private void RefreshTargetParameters()
    {
        ParameterCopyCatalog catalog = GetCurrentCatalog();
        string sourceName = SourceParameterComboBox.SelectedItem as string ?? string.Empty;
        string? previousTarget = TargetParameterComboBox.SelectedItem as string;

        IReadOnlyList<string> compatibleTargets =
            !string.IsNullOrWhiteSpace(sourceName) &&
            catalog.CompatibleTargetNamesBySource.TryGetValue(sourceName, out IReadOnlyList<string>? targetNames)
                ? targetNames
                : [];

        TargetParameterComboBox.ItemsSource = compatibleTargets;
        TargetParameterComboBox.SelectedItem = !string.IsNullOrWhiteSpace(previousTarget) && compatibleTargets.Contains(previousTarget)
            ? previousTarget
            : compatibleTargets.FirstOrDefault();
    }

    private ParameterCopyCatalog GetCurrentCatalog()
    {
        return GetCurrentScope() == ParameterCopyScope.SelectedEquipment && _selectedCatalog != null
            ? _selectedCatalog
            : _allCatalog;
    }

    private ParameterCopyScope GetCurrentScope()
    {
        if (_selectedCatalog != null && ScopeComboBox.SelectedIndex == 0)
        {
            return ParameterCopyScope.SelectedEquipment;
        }

        return ParameterCopyScope.AllEquipment;
    }

    private void UpdatePreview()
    {
        ParameterCopyCatalog catalog = GetCurrentCatalog();
        string sourceName = SourceParameterComboBox.SelectedItem as string ?? "[parametr zrodlowy]";
        string targetName = TargetParameterComboBox.SelectedItem as string ?? "[parametr docelowy]";
        string scopeText = GetCurrentScope() == ParameterCopyScope.SelectedEquipment
            ? $"dla zaznaczonych elementow instalacji ({catalog.ElementCount})"
            : $"dla wszystkich elementow instalacji ({catalog.ElementCount})";

        PreviewTextBlock.Text = $"Skopiowana zostanie wartosc z parametru \"{sourceName}\" do parametru \"{targetName}\" {scopeText}.";
    }
}
