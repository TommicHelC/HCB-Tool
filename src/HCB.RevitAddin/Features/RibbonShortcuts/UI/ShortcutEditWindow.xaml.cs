using System;
using System.Windows;
using System.Windows.Controls;

namespace HCB.RevitAddin.Features.RibbonShortcuts.UI;

public partial class ShortcutEditWindow : Window
{
    private const int MaxListedCommands = 6;

    private readonly string commandId;
    private IReadOnlyList<RibbonShortcutService.ShortcutConflict> conflicts = Array.Empty<RibbonShortcutService.ShortcutConflict>();

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
            conflicts = Array.Empty<RibbonShortcutService.ShortcutConflict>();
            ConflictGroup.Visibility = Visibility.Collapsed;
            ShowStatus(string.Join(Environment.NewLine, errors), "ErrorTextStyle");
            return false;
        }

        StatusText.Visibility = Visibility.Collapsed;
        conflicts = RibbonShortcutService.FindConflicts(shortcuts, commandId);
        if (conflicts.Count == 0)
        {
            ConflictGroup.Visibility = Visibility.Collapsed;
            return true;
        }

        IEnumerable<string> lines = conflicts
            .GroupBy(conflict => conflict.Shortcut, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                List<string> names = group.Select(conflict => conflict.CommandName).Distinct().ToList();
                string listed = string.Join(", ", names.Take(MaxListedCommands));
                return names.Count > MaxListedCommands
                    ? $"{group.Key}: {listed} i {names.Count - MaxListedCommands} innych"
                    : $"{group.Key}: {listed}";
            });

        ConflictText.Text = string.Join(Environment.NewLine, lines);
        ConflictGroup.Visibility = Visibility.Visible;
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

        bool takeOver = conflicts.Count > 0 && TakeOverRadioButton.IsChecked == true;
        try
        {
            RibbonShortcutService.SetShortcuts(commandId, Entered, takeOver);
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
