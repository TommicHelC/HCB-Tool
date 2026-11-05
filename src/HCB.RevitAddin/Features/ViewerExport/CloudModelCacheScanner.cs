using System;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.ViewerExport.Models;

namespace HCB.RevitAddin.Features.ViewerExport;

/// <summary>
/// Builds the list of ACC/BIM 360 models known to this machine from the Revit CollaborationCache
/// (%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 20xx\CollaborationCache\{user}\{projectGuid}\{modelGuid}.rvt).
/// Model names come from the "Central Model Path" stored in each cached file header.
/// </summary>
public sealed class CloudModelCacheScanner
{
    private const int HeaderBytesToRead = 8 * 1024 * 1024;

    private static readonly Regex CentralPathRegex = new(
        @"Central Model Path:\s*((?:Autodesk Docs|BIM 360)://[^\r\n\x00]+?\.rvt)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VersionedBackupRegex = new(@"\.\d{4}\.rvt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<CloudModelCandidate> Scan(string revitVersionNumber)
    {
        string cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Autodesk",
            "Revit",
            $"Autodesk Revit {revitVersionNumber}",
            "CollaborationCache");

        if (!Directory.Exists(cacheRoot))
        {
            return [];
        }

        Dictionary<Guid, CloudModelCandidate> candidates = new();
        foreach (string userFolder in Directory.EnumerateDirectories(cacheRoot))
        {
            foreach (string projectFolder in Directory.EnumerateDirectories(userFolder))
            {
                if (!Guid.TryParse(Path.GetFileName(projectFolder), out Guid projectGuid))
                {
                    continue;
                }

                foreach (string filePath in EnumerateCachedModels(projectFolder))
                {
                    if (!Guid.TryParse(Path.GetFileNameWithoutExtension(filePath), out Guid modelGuid))
                    {
                        continue;
                    }

                    DateTime lastUsed = File.GetLastWriteTime(filePath);
                    if (candidates.TryGetValue(modelGuid, out CloudModelCandidate? existing) && existing.LastUsed >= lastUsed)
                    {
                        continue;
                    }

                    string? centralPath = ReadCentralPath(filePath) ?? existing?.ProjectName + "/" + existing?.ModelName;
                    if (!TrySplitCentralPath(centralPath, out string projectName, out string modelName))
                    {
                        continue;
                    }

                    candidates[modelGuid] = new CloudModelCandidate
                    {
                        ProjectGuid = projectGuid,
                        ModelGuid = modelGuid,
                        ProjectName = projectName,
                        ModelName = modelName,
                        CachedFilePath = filePath,
                        LastUsed = lastUsed
                    };
                }
            }
        }

        return candidates.Values
            .OrderBy(candidate => candidate.ProjectName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.ModelName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateCachedModels(string projectFolder)
    {
        foreach (string subFolder in new[] { projectFolder, Path.Combine(projectFolder, "LinkedModels"), Path.Combine(projectFolder, "CentralCache") })
        {
            if (!Directory.Exists(subFolder))
            {
                continue;
            }

            foreach (string filePath in Directory.EnumerateFiles(subFolder, "*.rvt"))
            {
                if (!VersionedBackupRegex.IsMatch(filePath))
                {
                    yield return filePath;
                }
            }
        }
    }

    private static string? ReadCentralPath(string filePath)
    {
        try
        {
            byte[] buffer;
            using (FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                int length = (int)Math.Min(stream.Length, HeaderBytesToRead);
                buffer = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int chunk = stream.Read(buffer, read, length - read);
                    if (chunk == 0)
                    {
                        break;
                    }

                    read += chunk;
                }
            }

            // BasicFileInfo text is UTF-16; the stream can start on an odd byte, so try both alignments.
            for (int offset = 0; offset < 2; offset++)
            {
                string text = Encoding.Unicode.GetString(buffer, offset, buffer.Length - offset);
                Match match = CentralPathRegex.Match(text);
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        try
        {
            BasicFileInfo info = BasicFileInfo.Extract(filePath);
            return string.IsNullOrWhiteSpace(info.CentralPath) ? null : info.CentralPath;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TrySplitCentralPath(string? centralPath, out string projectName, out string modelName)
    {
        projectName = string.Empty;
        modelName = string.Empty;
        if (string.IsNullOrWhiteSpace(centralPath))
        {
            return false;
        }

        int schemeIndex = centralPath.IndexOf("://", StringComparison.Ordinal);
        string relative = schemeIndex >= 0 ? centralPath[(schemeIndex + 3)..] : centralPath;
        int slashIndex = relative.LastIndexOf('/');
        if (slashIndex <= 0 || slashIndex == relative.Length - 1)
        {
            return false;
        }

        projectName = relative[..slashIndex];
        modelName = relative[(slashIndex + 1)..];
        return true;
    }
}
