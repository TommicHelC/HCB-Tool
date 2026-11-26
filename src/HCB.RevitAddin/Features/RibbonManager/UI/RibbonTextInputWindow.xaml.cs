using System;
using System.Windows;
using System.Windows.Interop;

namespace HCB.RevitAddin.Features.RibbonManager.UI;

public partial class RibbonTextInputWindow : Window
{
    private RibbonTextInputWindow(string title, string value, string hint)
    {
        InitializeComponent();
        Header.Title = title;
        ValueTextBox.Text = value;
        HintText.Text = hint;
        HintText.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };
    }

    public string Value => ValueTextBox.Text.Trim();

    /// <summary>Returns the entered text (may be empty) or null when cancelled.</summary>
    public static string? Ask(string title, string value, string hint, IntPtr owner)
    {
        RibbonTextInputWindow window = new(title, value, hint);
        if (owner != IntPtr.Zero)
        {
            _ = new WindowInteropHelper(window) { Owner = owner };
        }

        return window.ShowDialog() == true ? window.Value : null;
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
