using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HCB.RevitAddin.Features.ParameterCalculator.Models;

namespace HCB.RevitAddin.Features.ParameterCalculator.UI;

public partial class ParameterCalculatorWindow : Window
{
    private const string DefaultTargetParameterName = "HC_Masa";

    public ParameterCalculatorWindow(IReadOnlyList<string> availableParameterNames, IReadOnlyList<string> writableTargetParameters, bool hasSelection)
    {
        InitializeComponent();
        AvailableParameterNames = availableParameterNames;
        OperatorOptions = ["+", "-", "*", "/"];
        Operands =
        [
            new FormulaOperand(),
            new FormulaOperand { OperatorSymbol = "+" }
        ];

        DataContext = this;
        TargetParameterComboBox.ItemsSource = writableTargetParameters;
        TargetParameterComboBox.SelectedItem = writableTargetParameters.Contains(DefaultTargetParameterName)
            ? DefaultTargetParameterName
            : writableTargetParameters[0];

        ScopeTextBlock.Text = hasSelection
            ? "Zakres: wykryto zaznaczenie. Obliczenia beda wykonane tylko na zaznaczonych elementach, ktore maja wszystkie wybrane parametry."
            : "Zakres: brak zaznaczenia. Obliczenia beda wykonane na wszystkich elementach modelu, ktore maja wszystkie wybrane parametry.";

        DecimalPlacesTextBox.Text = "2";
        FooterBar.StatusText = "Wybierz parametry zrodlowe, operator i parametr docelowy.";
        UpdatePreview();
    }

    public ObservableCollection<FormulaOperand> Operands { get; }

    public IReadOnlyList<string> AvailableParameterNames { get; }

    public IReadOnlyList<string> OperatorOptions { get; }

    public ParameterCalculatorOptions Options => new()
    {
        TargetParameterName = TargetParameterComboBox.SelectedItem as string ?? string.Empty,
        DecimalPlaces = int.TryParse(DecimalPlacesTextBox.Text, out int value) ? value : 0,
        Operands = Operands.Select(operand => new FormulaOperand
        {
            OperatorSymbol = operand.OperatorSymbol,
            ParameterName = operand.ParameterName
        }).ToList()
    };

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Options.TargetParameterName))
        {
            FooterBar.StatusText = "Wybierz parametr docelowy.";
            return;
        }

        if (!int.TryParse(DecimalPlacesTextBox.Text, out int decimalPlaces) || decimalPlaces < 0 || decimalPlaces > 8)
        {
            FooterBar.StatusText = "Liczba miejsc po przecinku musi byc z zakresu 0-8.";
            return;
        }

        if (Operands.Count < 2 || Operands.Any(operand => string.IsNullOrWhiteSpace(operand.ParameterName)))
        {
            FooterBar.StatusText = "Wybierz co najmniej dwa parametry zrodlowe.";
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

    private void AddRowButton_OnClick(object sender, RoutedEventArgs e)
    {
        Operands.Add(new FormulaOperand { OperatorSymbol = "+" });
        OperandsDataGrid.Items.Refresh();
        UpdatePreview();
    }

    private void RemoveRowButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Operands.Count <= 2 || OperandsDataGrid.SelectedItem is not FormulaOperand selectedOperand)
        {
            FooterBar.StatusText = "Musza pozostac co najmniej dwa parametry zrodlowe.";
            return;
        }

        Operands.Remove(selectedOperand);
        OperandsDataGrid.Items.Refresh();
        FooterBar.StatusText = "Usunieto wiersz formuly.";
        UpdatePreview();
    }

    private void MoveUpButton_OnClick(object sender, RoutedEventArgs e)
    {
        MoveSelectedOperand(-1);
    }

    private void MoveDownButton_OnClick(object sender, RoutedEventArgs e)
    {
        MoveSelectedOperand(1);
    }

    private void MoveSelectedOperand(int direction)
    {
        if (OperandsDataGrid.SelectedItem is not FormulaOperand selectedOperand)
        {
            return;
        }

        int currentIndex = Operands.IndexOf(selectedOperand);
        int targetIndex = currentIndex + direction;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= Operands.Count)
        {
            return;
        }

        Operands.Move(currentIndex, targetIndex);
        OperandsDataGrid.SelectedItem = selectedOperand;
        OperandsDataGrid.Items.Refresh();
        UpdatePreview();
    }

    private void OperandsDataGrid_OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(UpdatePreview);
    }

    private void OptionChanged(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private void OptionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        List<string> parts = [];
        for (int index = 0; index < Operands.Count; index++)
        {
            FormulaOperand operand = Operands[index];
            string parameterName = string.IsNullOrWhiteSpace(operand.ParameterName) ? "[parametr]" : operand.ParameterName;
            parts.Add(index == 0 ? parameterName : $"{operand.OperatorSymbol} {parameterName}");
        }

        string targetParameter = TargetParameterComboBox?.SelectedItem as string ?? "[docelowy]";
        string decimalPlacesText = int.TryParse(DecimalPlacesTextBox?.Text, out int value) ? value.ToString() : "0";
        PreviewTextBlock.Text = $"{string.Join(" ", parts)} -> {targetParameter} | miejsca po przecinku: {decimalPlacesText}";
    }
}
