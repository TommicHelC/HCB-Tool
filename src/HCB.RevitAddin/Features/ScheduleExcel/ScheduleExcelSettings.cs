using System;
using System.Text.Json;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>Per-user choices remembered between exports: %LOCALAPPDATA%\HCB\ScheduleExcel\settings.json.</summary>
internal sealed class ScheduleExcelSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HCB",
        "ScheduleExcel");

    private static string FilePath => Path.Combine(Directory, "settings.json");

    public string LastFolder { get; set; } = string.Empty;

    public bool OpenAfterExport { get; set; } = true;

    public bool ProtectReadOnlyCells { get; set; }

    public bool ExpandGroups { get; set; }

    public static ScheduleExcelSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<ScheduleExcelSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new ScheduleExcelSettings()
                : new ScheduleExcelSettings();
        }
        catch (Exception)
        {
            // Broken settings only cost the remembered folder.
            return new ScheduleExcelSettings();
        }
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Not worth failing an export over.
        }
    }
}
