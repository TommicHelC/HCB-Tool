using System;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>
/// Plain text log: %LOCALAPPDATA%\HCB\ScheduleExcel\logs\yyyyMMdd.log. Never throws - logging must not break
/// an export or a sync.
/// </summary>
internal static class ScheduleExcelLog
{
    private static readonly object Gate = new();

    public static string Folder { get; } = Path.Combine(ScheduleExcelSettings.Directory, "logs");

    public static string CurrentFile => Path.Combine(Folder, DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

    public static void Info(string message) => Append("INFO", message);

    public static void Error(string message, Exception exception) => Append("ERROR", $"{message}{Environment.NewLine}{exception}");

    private static void Append(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(
                    CurrentFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Ignore: a full disk or locked file must not surface in Revit.
        }
    }
}
