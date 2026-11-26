using System;
using System.Windows;
using System.Windows.Controls;

namespace HCB.RevitAddin.Features.RibbonShortcuts.UI;

public partial class ShortcutEditWindow : Window
{
    private readonly string commandId;

    internal ShortcutEditWindow(RibbonShortcutService.CommandInfo command)
    {
        InitializeComponent();
        RibbonShortcutService.InvalidateIndex();
        commandId = command.Id;
        Header.Title = command.Name;
        Header.Description = string.IsNullOrWhiteSpace(command.Paths) ? command.Id : command.Paths.Replace(">", " › ");
        ShortcutsTextBox.Text = string.Join("; ", command.Shortcuts);
        Loaded += (_, _) =>
        {
            ShortcutsTextBox.Focus();
            ShortcutsTextBox.SelectAll();
        };
    }

    private IReadOnlyList<string> Entered => RibbonShortcutService.Parse(ShortcutsTextBox.Text);

    private void ShortcutsTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateStatus();
    }

    /// <returns>False when an entered shortcut is invalid.</returns>
    private bool UpdateStatus()
    {
        IReadOnlyList<string> shortcuts = Entered;
        List<string> errors = shortcuts.Select(RibbonShortcutService.Validate).OfType<string>().ToList();
        if (errors.Count > 0)
        {
            ShowStatus(string.Join(Environment.NewLine, errors), "ErrorTextStyle");
            return false;
        }

        IReadOnlyDictionary<string, List<string>> conflicts = RibbonShortcutService.FindConflicts(shortcuts, commandId);
        if (conflicts.Count > 0)
        {
            ShowStatus(
                "Już używane (Revit pozwala na to; przełączasz Tab/strzałkami):" + Environment.NewLine +
                string.Join(Environment.NewLine, conflicts.Select(pair => $"{pair.Key}: {string.Join(", ", pair.Value.Distinct().Take(4))}")),
                "WarningTextStyle");
            return true;
        }

        StatusText.Visibility = Visibility.Collapsed;
        return true;
    }

    private void ShowStatus(string text, string styleKey)
    {
        StatusText.Text = text;
        StatusText.Style = (Style)FindResource(styleKey);
        StatusText.Visibility = Visibility.Visible;
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShortcutsTextBox.Clear();
        Save();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        Save();
    }

    private void Save()
    {
        if (!UpdateStatus())
        {
            return;
        }

        try
        {
            RibbonShortcutService.SetShortcuts(commandId, Entered);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ShowStatus("Nie udało się zapisać skrótu: " + exception.Message, "ErrorTextStyle");
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
