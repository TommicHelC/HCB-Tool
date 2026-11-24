using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.RotateFittings.Models;

namespace HCB.RevitAddin.Features.RotateFittings.UI;

public partial class RotateFittingsWindow : Window
{
    // Kept for the Revit session so reopening the window restores the last used settings.
    private static RotateFittingsOptions lastOptions = new()
    {
        AngleDegrees = 45d,
        AxisMode = RotateFittingsAxisMode.ConnectorAxis,
        IncludeDuctFittings = true,
        IncludePipeFittings = true,
        IncludeConduitFittings = true
    };

    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const int VkEscape = 0x1B;

    private readonly ExternalEvent pickEvent;
    private readonly RotateFittingsPickHandler pickHandler;
    private readonly IntPtr revitWindowHandle;

    public RotateFittingsWindow(ExternalEvent pickEvent, RotateFittingsPickHandler pickHandler, IntPtr revitWindowHandle)
    {
        InitializeComponent();
        this.pickEvent = pickEvent;
        this.pickHandler = pickHandler;
        this.revitWindowHandle = revitWindowHandle;

        AngleTextBox.Text = lastOptions.AngleDegrees.ToString("0.##", CultureInfo.CurrentCulture);
        ConnectorAxisRadioButton.IsChecked = lastOptions.AxisMode == RotateFittingsAxisMode.ConnectorAxis;
        VerticalAxisRadioButton.IsChecked = lastOptions.AxisMode == RotateFittingsAxisMode.VerticalAxis;
        DuctFittingsCheckBox.IsChecked = lastOptions.IncludeDuctFittings;
        PipeFittingsCheckBox.IsChecked = lastOptions.IncludePipeFittings;
        ConduitFittingsCheckBox.IsChecked = lastOptions.IncludeConduitFittings;
        Closed += (_, _) =>
        {
            IsClosed = true;
            CancelRevitPick();
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// PickObject cannot be cancelled through the API, so closing the window sends Revit the same ESC the user would press.
    /// </summary>
    private void CancelRevitPick()
    {
        if (!pickHandler.IsPicking || revitWindowHandle == IntPtr.Zero)
        {
            return;
        }

        SetForegroundWindow(revitWindowHandle);
        PostMessage(revitWindowHandle, WmKeyDown, (IntPtr)VkEscape, IntPtr.Zero);
        PostMessage(revitWindowHandle, WmKeyUp, (IntPtr)VkEscape, IntPtr.Zero);
    }

    public bool IsClosed { get; private set; }

    public void SetStatus(string text)
    {
        FooterBar.StatusText = text;
    }

    /// <summary>
    /// Starts the pick loop in Revit, or lets the running loop pick up the new settings on the next click.
    /// </summary>
    public void RequestPicking()
    {
        if (!TryBuildOptions(out RotateFittingsOptions? options, out string? error))
        {
            SetStatus(error);
            return;
        }

        SetStatus($"Obrot o {options.AngleDegrees:0.##} st. - klikaj ksztaltki, ESC wstrzymuje.");
        if (!pickHandler.IsPicking)
        {
            pickEvent.Raise();
        }
    }

    public bool TryBuildOptions([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RotateFittingsOptions? options, out string error)
    {
        options = null;
        error = string.Empty;
        if (!TryParseAngle(out double angleDegrees))
        {
            error = "Podaj poprawny, niezerowy kat w stopniach.";
            return false;
        }

        bool ducts = DuctFittingsCheckBox.IsChecked == true;
        bool pipes = PipeFittingsCheckBox.IsChecked == true;
        bool conduits = ConduitFittingsCheckBox.IsChecked == true;
        if (!ducts && !pipes && !conduits)
        {
            error = "Wybierz przynajmniej jedna kategorie.";
            return false;
        }

        options = new RotateFittingsOptions
        {
            AngleDegrees = angleDegrees,
            AxisMode = VerticalAxisRadioButton.IsChecked == true
                ? RotateFittingsAxisMode.VerticalAxis
                : RotateFittingsAxisMode.ConnectorAxis,
            IncludeDuctFittings = ducts,
            IncludePipeFittings = pipes,
            IncludeConduitFittings = conduits
        };
        lastOptions = options;
        return true;
    }

    private void PresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } &&
            double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out double angle))
        {
            AngleTextBox.Text = angle.ToString("0.##", CultureInfo.CurrentCulture);
            RequestPicking();
        }
    }

    private void AngleTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RequestPicking();
            e.Handled = true;
        }
    }

    private void AngleTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && pickHandler.IsPicking && TryParseAngle(out double angle))
        {
            SetStatus($"Obrot o {angle:0.##} st. od nastepnego klikniecia.");
        }
    }

    private void PickButton_OnClick(object sender, RoutedEventArgs e)
    {
        RequestPicking();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private bool TryParseAngle(out double angleDegrees)
    {
        string text = AngleTextBox.Text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out angleDegrees) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out angleDegrees))
        {
            angleDegrees %= 360d;
            return Math.Abs(angleDegrees) > 1e-6;
        }

        angleDegrees = 0d;
        return false;
    }
}
