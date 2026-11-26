using System;
using UIFramework;
using UIFrameworkServices;
using AW = Autodesk.Windows;

namespace HCB.RevitAddin.Features.RibbonShortcuts;

/// <summary>
/// Reads and changes Revit keyboard shortcuts for ribbon commands through the same
/// (undocumented, public) UIFramework services the native Keyboard Shortcuts dialog uses.
/// Changes apply to the running session and are saved to KeyboardShortcuts.xml.
/// Must run on the Revit UI thread.
/// </summary>
internal static class RibbonShortcutService
{
    private const char Separator = '#';

    private static bool backupDone;
    private static List<(string Id, string Name, string[] Keys)>? shortcutIndex;

    public sealed record CommandInfo(string Id, string Name, string Paths, IReadOnlyList<string> Shortcuts);

    /// <summary>Another command that already uses <see cref="Shortcut"/>.</summary>
    public sealed record ShortcutConflict(string Shortcut, string CommandId, string CommandName);

    /// <summary>Resolves a ribbon item to a command Revit can bind a shortcut to; null for pulldown headers etc.</summary>
    public static CommandInfo? Describe(AW.RibbonItem? item)
    {
        item = ResolveCommandItem(item);
        if (item == null || string.IsNullOrEmpty(item.Id))
        {
            return null;
        }

        Dictionary<string, ShortcutItem> commands = GetCommands(item.Id);
        if (!commands.TryGetValue(item.Id, out ShortcutItem? command))
        {
            return null;
        }

        string name = string.IsNullOrWhiteSpace(command.CommandName) ? CleanText(item.Text ?? item.Name) : command.CommandName;
        return new CommandInfo(command.CommandId, name, command.CommandPaths ?? string.Empty, Split(CurrentRep(command)));
    }

    /// <summary>Commands (other than <paramref name="exceptId"/>) that already use any of the given shortcuts.</summary>
    public static IReadOnlyList<ShortcutConflict> FindConflicts(IEnumerable<string> shortcuts, string exceptId)
    {
        HashSet<string> wanted = new(shortcuts, StringComparer.OrdinalIgnoreCase);
        List<ShortcutConflict> conflicts = new();
        if (wanted.Count == 0)
        {
            return conflicts;
        }

        shortcutIndex ??= GetCommands(null).Values
            .Select(command => (command.CommandId, command.CommandName, Split(CurrentRep(command)).ToArray()))
            .Where(entry => entry.Item3.Length > 0)
            .ToList();

        foreach ((string id, string name, string[] keys) in shortcutIndex)
        {
            if (string.Equals(id, exceptId, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string shortcut in keys.Where(wanted.Contains))
            {
                conflicts.Add(new ShortcutConflict(shortcut, id, name));
            }
        }

        return conflicts;
    }

    /// <summary>Drops the cached shortcut index (call when a shortcut editor opens).</summary>
    public static void InvalidateIndex()
    {
        shortcutIndex = null;
    }

    /// <summary>Returns an error message, or null when the shortcut can be used.</summary>
    public static string? Validate(string shortcut)
    {
        if (ShortcutKeyManager.IsReservedKey(shortcut))
        {
            return $"„{shortcut}” jest zarezerwowany przez Revit.";
        }

        return ShortcutKeyManager.IsLegalShortcutKey(shortcut)
            ? null
            : $"„{shortcut}” nie jest poprawnym skrótem (litery/cyfry, max 5 znaków, albo np. Ctrl+Shift+A).";
    }

    /// <summary>
    /// Replaces all shortcuts of one command (empty list = remove), applies them live and saves the file.
    /// With <paramref name="takeOverFromOthers"/> the same keys are removed from every other command
    /// ("move" instead of sharing a shortcut) in the same apply.
    /// </summary>
    public static void SetShortcuts(string commandId, IReadOnlyList<string> shortcuts, bool takeOverFromOthers = false)
    {
        BackupShortcutFileOnce();

        Dictionary<string, ShortcutItem> commands = GetCommands(commandId);
        if (!commands.ContainsKey(commandId))
        {
            throw new InvalidOperationException("Revit nie zna tej komendy: " + commandId);
        }

        HashSet<string> taken = takeOverFromOthers
            ? new HashSet<string>(shortcuts, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Hand Revit the complete, current table (like its own dialog does) with only the affected commands changed.
        Dictionary<string, ShortcutItem> table = new(commands.Count, StringComparer.Ordinal);
        Dictionary<string, string> changed = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ShortcutItem> pair in commands)
        {
            string current = CurrentRep(pair.Value);
            string rep;
            if (pair.Key == commandId)
            {
                rep = string.Join(Separator, shortcuts);
            }
            else if (taken.Count > 0 && Split(current).Any(taken.Contains))
            {
                rep = string.Join(Separator, Split(current).Where(key => !taken.Contains(key)));
            }
            else
            {
                rep = current;
            }

            if (!string.Equals(rep, current, StringComparison.Ordinal) || pair.Key == commandId)
            {
                changed[pair.Key] = rep;
            }

            ShortcutItem copy = new(pair.Value);
            Assign(copy, rep);
            table[pair.Key] = copy;
        }

        KeyboardShortcutService.applyShortcutChanges(table);
        ShortcutsHelper.SaveShortcuts(table.Values);

        // Keep the cached command table in sync for the next menu opening.
        foreach (KeyValuePair<string, string> pair in changed)
        {
            Assign(commands[pair.Key], pair.Value);
        }

        shortcutIndex = null;
    }

    public static string Normalize(string raw)
    {
        string[] parts = raw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        List<string> output = new(parts.Length);
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index];
            bool isLast = index == parts.Length - 1;
            output.Add(part.ToLowerInvariant() switch
            {
                "ctrl" or "control" or "strg" when !isLast => "Ctrl",
                "shift" when !isLast => "Shift",
                "alt" when !isLast => "Alt",
                _ => part.Length == 1 || parts.Length == 1 ? part.ToUpperInvariant() : Capitalize(part)
            });
        }

        return string.Join("+", output);
    }

    public static IReadOnlyList<string> Parse(string text)
    {
        return text
            .Split(new[] { ';', ',', Separator, '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, ShortcutItem> GetCommands(string? requiredId)
    {
        Dictionary<string, ShortcutItem>? commands = ShortcutsHelper.Commands;
        if (commands == null || commands.Count == 0 || (requiredId != null && !commands.ContainsKey(requiredId)))
        {
            // Walks the ribbon; also picks up add-in buttons created after the last load.
            ShortcutsHelper.LoadCommands();
            commands = ShortcutsHelper.Commands;
        }

        return commands ?? new Dictionary<string, ShortcutItem>();
    }

    // Live shortcut of the running session (the cached table can be stale after the native dialog was used).
    private static string CurrentRep(ShortcutItem command)
    {
        try
        {
            return KeyboardShortcutService.getShortcutRep(command.CommandId) ?? command.ShortcutsRep ?? string.Empty;
        }
        catch
        {
            return command.ShortcutsRep ?? string.Empty;
        }
    }

    private static void Assign(ShortcutItem item, string rep)
    {
        item.ShortcutsRep = rep;
        item.Shortcuts.Clear();
        foreach (string shortcut in Split(rep))
        {
            item.Shortcuts.Add(shortcut);
        }
    }

    private static IReadOnlyList<string> Split(string? rep)
    {
        return string.IsNullOrWhiteSpace(rep)
            ? Array.Empty<string>()
            : rep.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static AW.RibbonItem? ResolveCommandItem(AW.RibbonItem? item)
    {
        return item switch
        {
            AW.RibbonSplitButton split => split.Current ?? split,
            AW.RibbonListButton => null,
            _ => item
        };
    }

    private static void BackupShortcutFileOnce()
    {
        if (backupDone)
        {
            return;
        }

        backupDone = true;
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit");
            if (!Directory.Exists(root))
            {
                return;
            }

            string? file = Directory.GetFiles(root, "KeyboardShortcuts.xml", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(info => info.DirectoryName?.Contains(AppVersionFolder(), StringComparison.OrdinalIgnoreCase) == true)
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Select(info => info.FullName)
                .FirstOrDefault();

            if (file != null)
            {
                File.Copy(file, file + ".hcb-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), overwrite: false);
            }
        }
        catch
        {
            // Backup is best effort; the native dialog can always export/restore shortcuts.
        }
    }

    private static string AppVersionFolder()
    {
        // Assembly versions of RevitAPIUI follow the Revit year (25.x -> 2025).
        Version? version = typeof(Autodesk.Revit.UI.UIApplication).Assembly.GetName().Version;
        return version == null ? "Revit" : "Revit 20" + version.Major.ToString(CultureInfo.InvariantCulture);
    }

    private static string CleanText(string? text)
    {
        return (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string Capitalize(string value)
    {
        return value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
    }
}
