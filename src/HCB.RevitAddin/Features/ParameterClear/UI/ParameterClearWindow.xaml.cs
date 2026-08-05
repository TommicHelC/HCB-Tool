using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HCB.RevitAddin.Features.ParameterClear.Models;

namespace HCB.RevitAddin.Features.ParameterClear.UI;

public partial class ParameterClearWindow : Window
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _categoryParameterNames;
    private readonly IReadOnlyList<string> _modelParameterNames;
    private readonly bool _hasSelection;

    public ParameterClearWindow(IReadOnlyList<string> selectionParameterNames)
    {
        InitializeComponent();
        _hasSelection = true;
        _modelParameterNames = selectionParameterNames;
        _categoryParameterNames = new Dictionary<string, IReadOnlyList<string>>(StringComparer.CurrentCultureIgnoreCase);
        DataContext = this;

        ScopeDescription = "Zakres: wykryto zaznaczenie. Czyszczenie zostanie wykonane tylko na zaznaczonych elementach.";
        ScopeComboBox.ItemsSource = new[] { "Zaznaczone elementy" };
        ScopeComboBox.SelectedIndex = 0;
        ScopeComboBox.IsEnabled = false;
        CategoryLabelTextBlock.Visibility = Visibility.Collapsed;
        CategoryComboBox.Visibility = Visibility.Collapsed;

        SetParameterSource(_modelParameterNames);
        FooterBar.StatusText = "Wybierz parametr do wyczyszczenia.";
    }

    public ParameterClearWindow(ParameterClearCatalog catalog)
    {
        InitializeComponent();
        _hasSelection = false;
        _modelParameterNames = catalog.ModelParameterNames;
        _categoryParameterNames = catalog.CategoryParameterNames;
        DataContext = this;

        ScopeDescription = "Zakres: brak zaznaczenia. Mozesz wyczyscic parametr w jednej kategorii albo we wszystkich elementach modelu, ktore maja ten parametr.";
        ScopeComboBox.ItemsSource = new[] { "Wybrana kategoria", "Caly model" };
        ScopeComboBox.SelectedIndex = 0;
        CategoryComboBox.ItemsSource = _categoryParameterNames.Keys.ToList();
        CategoryComboBox.SelectedIndex = _categoryParameterNames.Count > 0 ? 0 : -1;

        FooterBar.StatusText = "Wybierz zakres i parametr do wyczyszczenia.";
        RefreshScopeDependentFields();
    }

    public string ScopeDescription { get; }

    public ParameterClearOptions Options => new()
    {
        ParameterName = ParameterComboBox.SelectedItem as string ?? string.Empty,
        Scope = GetCurrentScope(),
        CategoryName = GetCurrentScope() == ParameterClearScope.Category ? CategoryComboBox.SelectedItem as string : null
    };

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ParameterComboBox.SelectedItem as string))
        {
            FooterBar.StatusText = "Wybierz parametr do wyczyszczenia.";
            return;
        }

        if (!_hasSelection &&
            GetCurrentScope() == ParameterClearScope.Category &&
            string.IsNullOrWhiteSpace(CategoryComboBox.SelectedItem as string))
        {
            FooterBar.StatusText = "Wybierz kategorie.";
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

        RefreshScopeDependentFields();
    }

    private void CategoryComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (GetCurrentScope() == ParameterClearScope.Category)
        {
            RefreshScopeDependentFields();
        }
    }

    private void RefreshScopeDependentFields()
    {
        bool useCategory = !_hasSelection && GetCurrentScope() == ParameterClearScope.Category;
        CategoryLabelTextBlock.Visibility = useCategory ? Visibility.Visible : Visibility.Collapsed;
        CategoryComboBox.Visibility = useCategory ? Visibility.Visible : Visibility.Collapsed;

        IReadOnlyList<string> parameterNames = GetCurrentParameterNames();
        SetParameterSource(parameterNames);
    }

    private IReadOnlyList<string> GetCurrentParameterNames()
    {
        ParameterClearScope scope = GetCurrentScope();
        if (scope == ParameterClearScope.Category)
        {
            string? categoryName = CategoryComboBox.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(categoryName) &&
                _categoryParameterNames.TryGetValue(categoryName, out IReadOnlyList<string>? categoryParameters))
            {
                return categoryParameters;
            }

            return [];
        }

        return _modelParameterNames;
    }

    private void SetParameterSource(IReadOnlyList<string> parameterNames)
    {
        string? previouslySelected = ParameterComboBox.SelectedItem as string;
        ParameterComboBox.ItemsSource = parameterNames;
        ParameterComboBox.SelectedItem = !string.IsNullOrWhiteSpace(previouslySelected) && parameterNames.Contains(previouslySelected)
            ? previouslySelected
            : parameterNames.FirstOrDefault();
    }

    private ParameterClearScope GetCurrentScope()
    {
        if (_hasSelection)
        {
            return ParameterClearScope.Selection;
        }

        return ScopeComboBox.SelectedIndex == 1
            ? ParameterClearScope.Model
            : ParameterClearScope.Category;
    }
}
