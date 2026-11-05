using System;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using HCB.RevitAddin.Features.ViewerExport.Models;
using HCB.RevitAddin.Infrastructure.WithoutOpen;
using HCB.RevitAddin.Infrastructure.WithoutOpen.Models;

namespace HCB.RevitAddin.Features.ViewerExport;

/// <summary>
/// Exports cloud (ACC/BIM 360) models for Autodesk Viewer:
/// open in background (detached when workshared) -> unload RVT links -> purge unused (loop) -> SaveAs into one
/// local folder, following all loaded RVT links; then reopen each saved host and reload its links from the
/// local copies (relative paths). Source models in the cloud are never saved.
/// </summary>
public sealed class ViewerExportService
{
    private const string OperationName = "Viewer Export";
    private const int MaxPurgePasses = 10;

    private readonly WithoutOpenDocumentService _documentService = new();

    public ViewerExportResult Export(UIApplication uiApplication, IEnumerable<CloudModelCandidate> selectedModels, string outputFolderPath)
    {
        Application application = uiApplication.Application;
        Directory.CreateDirectory(outputFolderPath);

        // Background opens/saves can raise modal warnings (e.g. link problems) and other add-ins' failures;
        // answer them automatically so the batch never waits for a click.
        uiApplication.DialogBoxShowing += OnDialogBoxShowing;
        application.FailuresProcessing += OnFailuresProcessing;
        try
        {
            return ExportCore(application, selectedModels, outputFolderPath);
        }
        finally
        {
            uiApplication.DialogBoxShowing -= OnDialogBoxShowing;
            application.FailuresProcessing -= OnFailuresProcessing;
        }
    }

    private ViewerExportResult ExportCore(Application application, IEnumerable<CloudModelCandidate> selectedModels, string outputFolderPath)
    {
        List<WithoutOpenOperationLogEntry> entries = [];
        Queue<ExportJob> queue = new();
        HashSet<string> queuedKeys = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> savedPathsByKey = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);
        List<(string HostPath, List<LinkInfo> Links)> hostsToRelink = [];

        foreach (CloudModelCandidate candidate in selectedModels)
        {
            if (queuedKeys.Add(candidate.ModelGuid.ToString()))
            {
                queue.Enqueue(ExportJob.FromCandidate(candidate));
            }
        }

        while (queue.Count > 0)
        {
            ExportJob job = queue.Dequeue();
            DateTime startedAt = DateTime.UtcNow;
            Document? document = null;

            string? openConflict = FindOpenConflict(application, job);
            if (openConflict != null)
            {
                entries.Add(CreateLog(job.DisplayName, WithoutOpenOperationStatus.Skipped, openConflict, startedAt));
                continue;
            }

            string step = "otwieranie";
            try
            {
                List<Document> alreadyOpen = application.Documents.Cast<Document>().ToList();
                Document opened = OpenForExport(application, job);
                if (alreadyOpen.Any(open => open.Equals(opened)))
                {
                    // Revit returned a document the user already has open: never purge, save or close it.
                    entries.Add(CreateLog(job.DisplayName, WithoutOpenOperationStatus.Skipped, "Model jest otwarty w Revicie. Zamknij go i uruchom eksport ponownie.", startedAt));
                    continue;
                }

                document = opened;

                step = "linki";
                List<LinkInfo> links = CollectLoadedRevitLinks(document);
                foreach (LinkInfo link in links)
                {
                    if (queuedKeys.Add(link.Job.Key))
                    {
                        queue.Enqueue(link.Job);
                    }
                }

                UnloadLinks(document, links);

                step = "purge";
                int purgedCount = PurgeUnused(document);

                step = "zapis";
                string targetPath = BuildTargetPath(outputFolderPath, job.FileName, usedFileNames);
                string? saveNote = SaveCopy(document, targetPath, startedAt);

                savedPathsByKey[job.Key] = targetPath;
                if (links.Count > 0)
                {
                    hostsToRelink.Add((targetPath, links));
                }

                string message = $"Zapisano. Purge: {purgedCount} elementow. Linki RVT: {links.Count}.";
                if (saveNote != null)
                {
                    message += " Uwaga (inny dodatek przy zapisie): " + saveNote;
                }

                entries.Add(CreateLog(job.DisplayName, WithoutOpenOperationStatus.Success, message, startedAt, targetPath));
            }
            catch (Exception exception)
            {
                entries.Add(CreateLog(job.DisplayName, WithoutOpenOperationStatus.Failed, $"[{step}] {exception.Message}", startedAt));
            }
            finally
            {
                if (document != null)
                {
                    try
                    {
                        _documentService.CloseWithoutSave(document);
                    }
                    catch (Exception exception)
                    {
                        entries.Add(CreateLog(job.DisplayName, WithoutOpenOperationStatus.Failed, "Nie udalo sie zamknac modelu: " + exception.Message, startedAt));
                    }
                }
            }
        }

        foreach ((string hostPath, List<LinkInfo> links) in hostsToRelink)
        {
            entries.Add(RelinkHost(application, hostPath, links, savedPathsByKey));
        }

        return new ViewerExportResult
        {
            Entries = entries
        };
    }

    private static string? FindOpenConflict(Application application, ExportJob job)
    {
        foreach (Document openDocument in application.Documents)
        {
            string? key = TryGetDocumentKey(openDocument);
            if (key == null || !string.Equals(key, job.Key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return openDocument.IsLinked
                ? "Model jest zaladowany jako link w projekcie otwartym w Revicie. Zamknij ten projekt i uruchom eksport ponownie."
                : "Model jest otwarty w Revicie. Zamknij go i uruchom eksport ponownie.";
        }

        return null;
    }

    private static string? TryGetDocumentKey(Document document)
    {
        try
        {
            if (document.IsModelInCloud)
            {
                return document.GetCloudModelPath().GetModelGUID().ToString();
            }

            return string.IsNullOrWhiteSpace(document.PathName) ? null : Path.GetFullPath(document.PathName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Document OpenForExport(Application application, ExportJob job)
    {
        Exception? lastError = null;
        foreach (ModelPath modelPath in job.GetCandidatePaths())
        {
            // Workshared cloud models must be detached; single-user (non-workshared) cloud models reject
            // the detach option, so fall back to a plain open. Either way only SaveAs to a local path follows.
            foreach (bool detach in new[] { true, false })
            {
                try
                {
                    OpenOptions options = new();
                    if (detach)
                    {
                        options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                        options.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
                    }

                    return application.OpenDocumentFile(modelPath, options);
                }
                catch (Exception exception)
                {
                    lastError = exception;
                }
            }
        }

        throw new InvalidOperationException($"Nie udalo sie otworzyc modelu: {lastError?.Message}", lastError);
    }

    /// <summary>
    /// Cloud links are external resources, not external file references, so the link target is read
    /// from the loaded link document itself.
    /// </summary>
    private static List<LinkInfo> CollectLoadedRevitLinks(Document document)
    {
        List<LinkInfo> links = [];
        Dictionary<ElementId, Document> linkDocumentsByType = new();
        foreach (RevitLinkInstance instance in new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            ElementId typeId = instance.GetTypeId();
            if (linkDocumentsByType.ContainsKey(typeId))
            {
                continue;
            }

            Document? linkDocument = instance.GetLinkDocument();
            if (linkDocument != null)
            {
                linkDocumentsByType[typeId] = linkDocument;
            }
        }

        foreach (RevitLinkType linkType in new FilteredElementCollector(document).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
        {
            // Nested link types belong to another link; they are exported as direct links of that link model.
            if (linkType.IsNestedLink || !linkDocumentsByType.TryGetValue(linkType.Id, out Document? linkDocument))
            {
                continue;
            }

            try
            {
                ModelPath? linkPath = linkDocument.IsModelInCloud
                    ? linkDocument.GetCloudModelPath()
                    : string.IsNullOrWhiteSpace(linkDocument.PathName) ? null : ModelPathUtils.ConvertUserVisiblePathToModelPath(linkDocument.PathName);

                if (linkPath == null || linkPath.Empty)
                {
                    continue;
                }

                links.Add(new LinkInfo(linkType.Id, ExportJob.FromLink(linkPath, linkType.Name)));
            }
            catch (Exception)
            {
            }
        }

        return links;
    }

    private static void UnloadLinks(Document document, IEnumerable<LinkInfo> links)
    {
        foreach (LinkInfo link in links)
        {
            try
            {
                if (document.GetElement(link.TypeId) is RevitLinkType linkType && RevitLinkType.IsLoaded(document, link.TypeId))
                {
                    linkType.Unload(null);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    private static int PurgeUnused(Document document)
    {
        int totalDeleted = 0;
        for (int pass = 0; pass < MaxPurgePasses; pass++)
        {
            ICollection<ElementId> unusedIds = document.GetUnusedElements(new HashSet<ElementId>());
            if (unusedIds.Count == 0)
            {
                break;
            }

            int deletedInPass = 0;
            using (Transaction transaction = new(document, "Purge Unused (Viewer Export)"))
            {
                FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
                failureOptions.SetFailuresPreprocessor(new SuppressWarningsPreprocessor());
                failureOptions.SetClearAfterRollback(true);
                transaction.SetFailureHandlingOptions(failureOptions);
                transaction.Start();

                using (SubTransaction bulkDelete = new(document))
                {
                    bulkDelete.Start();
                    try
                    {
                        deletedInPass = document.Delete(unusedIds).Count;
                        bulkDelete.Commit();
                    }
                    catch (Exception)
                    {
                        bulkDelete.RollBack();
                        deletedInPass = 0;
                    }
                }

                if (deletedInPass == 0)
                {
                    foreach (ElementId id in unusedIds)
                    {
                        if (document.GetElement(id) == null)
                        {
                            continue;
                        }

                        try
                        {
                            deletedInPass += document.Delete(id).Count;
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

                if (transaction.Commit() != TransactionStatus.Committed)
                {
                    break;
                }
            }

            if (deletedInPass == 0)
            {
                break;
            }

            totalDeleted += deletedInPass;
        }

        return totalDeleted;
    }

    /// <summary>
    /// Saves a local copy. Returns a note when another add-in's event handler threw after the file was written
    /// (Revit reports that as an exception from SaveAs although the copy is complete).
    /// </summary>
    private static string? SaveCopy(Document document, string targetPath, DateTime startedAtUtc)
    {
        SaveAsOptions saveAsOptions = new()
        {
            OverwriteExistingFile = true,
            MaximumBackups = 1
        };

        if (document.IsWorkshared)
        {
            saveAsOptions.SetWorksharingOptions(new WorksharingSaveAsOptions
            {
                SaveAsCentral = true
            });
        }

        string? note = null;
        try
        {
            document.SaveAs(targetPath, saveAsOptions);
        }
        catch (Exception exception) when (File.Exists(targetPath) && File.GetLastWriteTimeUtc(targetPath) >= startedAtUtc)
        {
            note = exception.Message;
        }

        DeleteNumberedBackups(targetPath);
        return note;
    }

    private static void DeleteNumberedBackups(string savedPath)
    {
        string? folder = Path.GetDirectoryName(savedPath);
        if (folder == null)
        {
            return;
        }

        string baseName = Path.GetFileNameWithoutExtension(savedPath);
        foreach (string backupPath in Directory.EnumerateFiles(folder, baseName + ".*.rvt"))
        {
            string middle = Path.GetFileNameWithoutExtension(backupPath)[(baseName.Length + 1)..];
            if (middle.Length != 4 || !middle.All(char.IsDigit))
            {
                continue;
            }

            try
            {
                File.Delete(backupPath);
            }
            catch (Exception)
            {
            }
        }
    }

    private WithoutOpenOperationLogEntry RelinkHost(Application application, string hostPath, IReadOnlyList<LinkInfo> links, IReadOnlyDictionary<string, string> savedPathsByKey)
    {
        DateTime startedAt = DateTime.UtcNow;
        string displayName = Path.GetFileName(hostPath) + " (linki)";
        Document? document = null;

        try
        {
            bool isWorkshared = BasicFileInfo.Extract(hostPath).IsWorkshared;
            OpenOptions options = new();
            if (isWorkshared)
            {
                options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
            }

            document = application.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(hostPath), options);

            int relinked = 0;
            List<string> problems = [];
            foreach (LinkInfo link in links)
            {
                if (document.GetElement(link.TypeId) is not RevitLinkType linkType)
                {
                    problems.Add($"{link.Job.FileName}: brak typu linku");
                    continue;
                }

                if (!savedPathsByKey.TryGetValue(link.Job.Key, out string? localLinkPath))
                {
                    problems.Add($"{link.Job.FileName}: brak kopii lokalnej");
                    continue;
                }

                try
                {
                    LinkLoadResult loadResult = linkType.LoadFrom(ModelPathUtils.ConvertUserVisiblePathToModelPath(localLinkPath), new WorksetConfiguration());
                    if (loadResult.LoadResult != LinkLoadResultType.LinkLoaded)
                    {
                        problems.Add($"{link.Job.FileName}: {loadResult.LoadResult}");
                        continue;
                    }

                    SetRelativePath(document, linkType);
                    relinked++;
                }
                catch (Exception exception)
                {
                    problems.Add($"{link.Job.FileName}: {exception.Message}");
                }
            }

            string? saveNote = SaveRelinked(document, hostPath, isWorkshared, startedAt);

            string message = $"Przepieto linki na lokalne kopie: {relinked}/{links.Count}.";
            if (problems.Count > 0)
            {
                message += " Problemy: " + string.Join("; ", problems) + ".";
            }

            if (saveNote != null)
            {
                message += " Uwaga (inny dodatek przy zapisie): " + saveNote;
            }

            WithoutOpenOperationStatus status = problems.Count == 0 ? WithoutOpenOperationStatus.Success : WithoutOpenOperationStatus.Failed;
            return CreateLog(displayName, status, message, startedAt, hostPath);
        }
        catch (Exception exception)
        {
            return CreateLog(displayName, WithoutOpenOperationStatus.Failed, exception.Message, startedAt, hostPath);
        }
        finally
        {
            if (document != null)
            {
                try
                {
                    _documentService.CloseWithoutSave(document);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private static void SetRelativePath(Document document, RevitLinkType linkType)
    {
        try
        {
            using Transaction transaction = new(document, "Relative link path (Viewer Export)");
            transaction.Start();
            linkType.PathType = PathType.Relative;
            transaction.Commit();
        }
        catch (Exception)
        {
        }
    }

    private static string? SaveRelinked(Document document, string hostPath, bool isWorkshared, DateTime startedAtUtc)
    {
        if (isWorkshared)
        {
            return SaveCopy(document, hostPath, startedAtUtc);
        }

        try
        {
            document.Save();
        }
        catch (Exception exception) when (File.GetLastWriteTimeUtc(hostPath) >= startedAtUtc)
        {
            return exception.Message;
        }

        DeleteNumberedBackups(hostPath);
        return null;
    }

    private static string? TryGetModelKey(ModelPath path)
    {
        try
        {
            if (path.CloudPath)
            {
                return path.GetModelGUID().ToString();
            }

            return Path.GetFullPath(ModelPathUtils.ConvertModelPathToUserVisiblePath(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string NameKey(string fileName)
    {
        return "name:" + Path.GetFileNameWithoutExtension(fileName);
    }

    private static string BuildTargetPath(string outputFolderPath, string fileName, ISet<string> usedFileNames)
    {
        string baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(fileName));
        string candidate = baseName + ".rvt";
        for (int index = 2; !usedFileNames.Add(candidate); index++)
        {
            candidate = $"{baseName} ({index}).rvt";
        }

        return Path.Combine(outputFolderPath, candidate);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "Model" : name.Trim();
    }

    private static void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs args)
    {
        try
        {
            args.OverrideResult((int)TaskDialogResult.Ok);
        }
        catch (Exception)
        {
        }
    }

    private static void OnFailuresProcessing(object? sender, FailuresProcessingEventArgs args)
    {
        FailuresAccessor failuresAccessor = args.GetFailuresAccessor();
        foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
        {
            if (failure.GetSeverity() == FailureSeverity.Warning)
            {
                failuresAccessor.DeleteWarning(failure);
            }
        }

        args.SetProcessingResult(FailureProcessingResult.Continue);
    }

    private static WithoutOpenOperationLogEntry CreateLog(string filePath, WithoutOpenOperationStatus status, string message, DateTime startedAt, string outputPath = "")
    {
        return new WithoutOpenOperationLogEntry
        {
            FilePath = filePath,
            OperationName = OperationName,
            Status = status,
            Message = message,
            OutputPath = outputPath,
            Duration = DateTime.UtcNow - startedAt
        };
    }

    private sealed record LinkInfo(ElementId TypeId, ExportJob Job);

    private sealed class ExportJob
    {
        private CloudModelCandidate? _candidate;
        private ModelPath? _linkPath;

        public string Key { get; private init; } = string.Empty;

        public string FileName { get; private init; } = string.Empty;

        public string DisplayName { get; private init; } = string.Empty;

        public static ExportJob FromCandidate(CloudModelCandidate candidate)
        {
            return new ExportJob
            {
                _candidate = candidate,
                Key = candidate.ModelGuid.ToString(),
                FileName = candidate.ModelName,
                DisplayName = candidate.DisplayName
            };
        }

        public static ExportJob FromLink(ModelPath linkPath, string linkTypeName)
        {
            string fileName = linkTypeName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) ? linkTypeName : linkTypeName + ".rvt";
            return new ExportJob
            {
                _linkPath = linkPath,
                Key = TryGetModelKey(linkPath) ?? NameKey(fileName),
                FileName = fileName,
                DisplayName = "Link: " + fileName
            };
        }

        public IEnumerable<ModelPath> GetCandidatePaths()
        {
            if (_linkPath != null)
            {
                yield return _linkPath;
                yield break;
            }

            if (_candidate == null)
            {
                yield break;
            }

            // The cache does not store the region, so try the regions in order (HCB projects are in EMEA).
            foreach (string region in new[] { ModelPathUtils.CloudRegionEMEA, ModelPathUtils.CloudRegionUS })
            {
                yield return ModelPathUtils.ConvertCloudGUIDsToCloudPath(region, _candidate.ProjectGuid, _candidate.ModelGuid);
            }
        }
    }

    private sealed class SuppressWarningsPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
            }

            return FailureProcessingResult.Continue;
        }
    }
}
