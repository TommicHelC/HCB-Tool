using System;
using System.Text.Json;
using HCB.RevitAddin.Features.RibbonManager.Models;

namespace HCB.RevitAddin.Features.RibbonManager;

internal static class RibbonLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HCB",
        "RibbonLayout.json");

    public static RibbonLayoutConfig Load()
    {
        try
        {
            return File.Exists(DefaultPath) ? Read(DefaultPath) : new RibbonLayoutConfig();
        }
        catch
        {
            // A broken file must never block Revit startup; the user can re-save from the manager.
            return new RibbonLayoutConfig();
        }
    }

    public static void Save(RibbonLayoutConfig config)
    {
        Write(DefaultPath, config);
    }

    public static RibbonLayoutConfig Read(string path)
    {
        string json = File.ReadAllText(path);
        RibbonLayoutConfig config = JsonSerializer.Deserialize<RibbonLayoutConfig>(json, JsonOptions) ?? new RibbonLayoutConfig();
        config.CustomTabs ??= new List<RibbonCustomTab>();
        config.HiddenTabs ??= new List<string>();
        config.HiddenPanels ??= new List<string>();
        config.PanelMoves ??= new Dictionary<string, string>();
        config.TabTitles ??= new Dictionary<string, string>();
        config.PanelTitles ??= new Dictionary<string, string>();
        return config;
    }

    public static void Write(string path, RibbonLayoutConfig config)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }
}
