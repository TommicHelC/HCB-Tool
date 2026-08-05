using System.Windows;
using System.Windows.Controls;
using HCB.RevitAddin.Features.AccessoryTerminalNumbering.Models;

namespace HCB.RevitAddin.Features.AccessoryTerminalNumbering.UI;

public partial class AccessoryTerminalNumberingWindow : Window
{
    private const string DefaultTargetParameterName = "LIN_POSITION_NUMBER_A";

    public AccessoryTerminalNumberingWindow(IReadOnlyList<string> availableTargetParameters)
    {
        InitializeComponent();
        TargetParameterComboBox.ItemsSource = availableTargetParameters;
        TargetParameterComboBox.SelectedItem = availableTargetParameters.Contains(DefaultTargetParameterName)
            ? DefaultTargetParameterName
            : availableTargetParameters.Count > 0 ? availableTargetParameters[0] : null;

        FooterBar.StatusText = "Wybierz parametr docelowy i ustawienia numeracji dla Duct Accessory.";
        UpdatePreview();
    }

    public AccessoryTerminalNumberingOptions Options => new()
    {
        TargetParameterName = GetSelectedString(TargetParameterComboBox),
        StartNumber = int.TryParse(GetText(StartNumberTextBox), out int value) ? value : 0,
        UseLastProjectNumber = UseLastProjectNumberCheckBox?.IsChecked == true
    };

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Options.TargetParameterName))
        {
            FooterBar.StatusText = "Wybierz parametr docelowy.";
            return;
        }

        if (!Options.UseLastProjectNumber && Options.StartNumber <= 0)
        {
            FooterBar.StatusText = "Podaj dodatni numer startowy albo zaznacz pobieranie ostatniego numeru z projektu.";
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

    private void OptionsChanged(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private void OptionsChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewTextBlock == null)
        {
            return;
        }

        PreviewTextBlock.Text = $"TYPEMARK.HC_SYSTEM.{GetPreviewNumber()}";
    }

    private static string GetText(TextBox? textBox)
    {
        return textBox?.Text?.Trim() ?? string.Empty;
    }

    private static string GetSelectedString(ComboBox? comboBox)
    {
        return comboBox?.SelectedItem as string ?? string.Empty;
    }

    private string GetPreviewNumber()
    {
        if (Options.UseLastProjectNumber)
        {
            return "ostatni+1";
        }

        return Options.StartNumber > 0 ? Options.StartNumber.ToString() : "1";
    }
}
