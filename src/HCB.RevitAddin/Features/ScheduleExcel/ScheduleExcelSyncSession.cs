using System;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using HCB.RevitAddin.Features.ScheduleExcel.UI;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>
/// One live link between a workbook and a Revit model: every save in Excel (Ctrl+S) syncs edits to Revit.
/// Threads: FileSystemWatcher and timers run on the thread pool and only read/parse the file; the Revit
/// part runs in <see cref="SyncHandler"/> via ExternalEvent. Nothing here blocks the Revit thread, and
/// every background callback catches and logs (an unhandled exception there kills Revit).
/// </summary>
internal sealed class ScheduleExcelSyncSession
{
    private const int DebounceMilliseconds = 1500;
    private const int MonitorMilliseconds = 3000;
    private const int ConfirmAboveEdits = 500;
    private static readonly TimeSpan IdleLimit = TimeSpan.FromHours(2);

    private readonly object _gate = new();
    private readonly Dictionary<string, string[]> _baseline;
    private readonly ExternalEvent _externalEvent;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private readonly Timer _monitor;
    private readonly ScheduleExcelOverlay _overlay;

    private ScheduleExcelWorkbookContent? _pending;
    private string _lastHash;
    private bool _stopped;
    private bool _excelSeen;
    private int _excelMissingChecks;
    private DateTime _lastActivity = DateTime.Now;
    private int _syncCount;
    private int _changeCount;

    private ScheduleExcelSyncSession(Document document, string filePath, string scheduleName, ScheduleExcelWorkbookContent initial)
    {
        Document = document;
        FilePath = Path.GetFullPath(filePath);
        ScheduleName = scheduleName;
        _lastHash = initial.Hash;
        _baseline = initial.Snapshot.ToDictionary(pair => pair.Key, pair => (string[])pair.Value.Clone(), StringComparer.Ordinal);

        _externalEvent = ExternalEvent.Create(new SyncHandler(this));
        _debounce = new Timer(_ => Guard("Odczyt pliku", ReadChangedFile), null, Timeout.Infinite, Timeout.Infinite);
        _monitor = new Timer(_ => Guard("Kontrola sesji", Monitor), null, MonitorMilliseconds, MonitorMilliseconds);

        _watcher = new FileSystemWatcher(Path.GetDirectoryName(FilePath)!, Path.GetFileName(FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        _watcher.Changed += (_, _) => OnFileEvent();
        _watcher.Created += (_, _) => OnFileEvent();
        _watcher.Renamed += (_, args) =>
        {
            // Excel saves to a temp file and renames it over the workbook.
            if (string.Equals(args.FullPath, FilePath, StringComparison.OrdinalIgnoreCase))
            {
                OnFileEvent();
            }
        };
        _watcher.Error += (_, args) => Guard("Watcher", () =>
        {
            ScheduleExcelLog.Error("FileSystemWatcher zgłosił błąd - restart.", args.GetException());
            _watcher.EnableRaisingEvents = false;
            _watcher.EnableRaisingEvents = true;
            OnFileEvent();
        });

        _overlay = ScheduleExcelOverlay.Start(FilePath, scheduleName, () => Stop("zatrzymano przyciskiem Stop"));
        _watcher.EnableRaisingEvents = true;
    }

    public Document Document { get; }

    public string FilePath { get; }

    public string ScheduleName { get; }

    public bool IsStopped
    {
        get
        {
            lock (_gate)
            {
                return _stopped;
            }
        }
    }

    /// <summary>Must run in a Revit API context (command): ExternalEvent.Create needs it.</summary>
    public static ScheduleExcelSyncSession Start(Document document, string filePath, string scheduleName, ScheduleExcelWorkbookContent initial)
    {
        var session = new ScheduleExcelSyncSession(document, filePath, scheduleName, initial);
        ScheduleExcelLog.Info($"Start sesji: '{scheduleName}' <-> {session.FilePath} (model '{document.Title}').");
        return session;
    }

    public void Stop(string reason)
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
        }

        ScheduleExcelLog.Info($"Koniec sesji '{ScheduleName}' ({reason}). Synchronizacji: {_syncCount}, zmian: {_changeCount}.");
        TryRun(() => _watcher.EnableRaisingEvents = false);
        TryRun(_watcher.Dispose);
        TryRun(_debounce.Dispose);
        TryRun(_monitor.Dispose);
        TryRun(_overlay.Close);
        TryRun(_externalEvent.Dispose);
        ScheduleExcelSyncManager.Remove(this);
    }

    private void OnFileEvent()
    {
        if (IsStopped)
        {
            return;
        }

        _lastActivity = DateTime.Now;
        TryRun(() => _debounce.Change(DebounceMilliseconds, Timeout.Infinite));
    }

    /// <summary>Thread pool: read a copy of the file, skip unchanged content, hand the parsed file to Revit.</summary>
    private void ReadChangedFile()
    {
        if (IsStopped || !File.Exists(FilePath))
        {
            return;
        }

        byte[] bytes = ScheduleExcelWorkbookReader.ReadBytes(FilePath);
        string hash = ScheduleExcelWorkbookReader.ComputeHash(bytes);
        lock (_gate)
        {
            if (hash == _lastHash || (_pending != null && _pending.Hash == hash))
            {
                return;
            }
        }

        ScheduleExcelWorkbookContent content;
        try
        {
            content = ScheduleExcelWorkbookReader.Read(bytes);
        }
        catch (Exception exception)
        {
            ScheduleExcelLog.Error("Nie udało się odczytać zapisanego pliku.", exception);
            _overlay.SetStatus("Nie udało się odczytać pliku.", exception.Message, isError: true);
            lock (_gate)
            {
                _lastHash = hash;
            }

            return;
        }

        lock (_gate)
        {
            // Single flight: keep only the newest save; the handler takes it when Revit is free.
            _pending = content;
        }

        _overlay.SetStatus("Wykryto zapis - czekam na Revit...", string.Empty);
        ExternalEventRequest request = _externalEvent.Raise();
        if (request != ExternalEventRequest.Accepted && request != ExternalEventRequest.Pending)
        {
            ScheduleExcelLog.Info($"ExternalEvent.Raise zwrócił {request}.");
        }
    }

    private void Monitor()
    {
        if (IsStopped)
        {
            return;
        }

        if (DateTime.Now - _lastActivity > IdleLimit)
        {
            Stop("brak aktywności przez 2 h");
            return;
        }

        bool open = ScheduleExcelWindows.FindWorkbookWindow(FilePath) != IntPtr.Zero;
        if (open)
        {
            _excelSeen = true;
            _excelMissingChecks = 0;
        }
        else if (_excelSeen && ++_excelMissingChecks >= 2)
        {
            Stop("plik zamknięty w Excel");
        }
    }

    /// <summary>Revit thread (ExternalEvent): diff against the baseline and write edits in one transaction.</summary>
    private void ApplyPending(UIApplication application)
    {
        ScheduleExcelWorkbookContent? content;
        lock (_gate)
        {
            content = _pending;
            _pending = null;
        }

        if (content == null || IsStopped)
        {
            return;
        }

        _lastActivity = DateTime.Now;
        string? invalid = ScheduleExcelApplier.Validate(Document, content);
        if (invalid != null)
        {
            Finish(content.Hash);
            ScheduleExcelLog.Info($"Synchronizacja odrzucona: {invalid}");
            _overlay.SetStatus("Nie zsynchronizowano.", invalid, isError: true);
            return;
        }

        ScheduleExcelChangeSet changes = ScheduleExcelChangeSet.Compute(content, _baseline);
        if (changes.Edits.Count == 0)
        {
            Finish(content.Hash);
            _overlay.SetStatus($"{DateTime.Now:HH:mm:ss} - zapis bez zmian do Revit.", Describe(changes.Skipped));
            LogSkipped(changes.Skipped);
            return;
        }

        if (changes.Edits.Count > ConfirmAboveEdits)
        {
            var dialog = new TaskDialog("HCB Excel Sync")
            {
                MainInstruction = $"Zapisać {changes.Edits.Count} zmian do Revit?",
                MainContent = $"Plik: {Path.GetFileName(FilePath)}\nTo dużo zmian w jednym zapisie. Całość będzie można cofnąć jednym Ctrl+Z.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (dialog.Show() != TaskDialogResult.Yes)
            {
                Finish(content.Hash);
                _overlay.SetStatus("Anulowano zapis do Revit.", "Zapisz plik ponownie, aby spróbować jeszcze raz.");
                return;
            }
        }

        if (Document.IsWorkshared)
        {
            _overlay.SetStatus("Sprawdzanie własności elementów...", string.Empty);
        }

        ScheduleExcelApplyResult result = ScheduleExcelApplier.Apply(Document, content, changes.Edits);
        foreach (ScheduleExcelEdit edit in result.Applied)
        {
            if (_baseline.TryGetValue(edit.RowKey, out string[]? row) && edit.FieldPosition < row.Length)
            {
                row[edit.FieldPosition] = edit.NewText;
            }
        }

        Finish(content.Hash);
        _syncCount++;
        _changeCount += result.Applied.Count;

        List<ScheduleExcelSkip> skipped = [.. changes.Skipped, .. result.Skipped];
        ScheduleExcelLog.Info($"Synchronizacja '{result.TransactionName}': zmian {result.Applied.Count}, zapisów parametrów {result.ParameterWrites}, pominięto {skipped.Count}, hash {content.Hash[..12]}.");
        LogSkipped(skipped);

        if (result.Error.Length > 0)
        {
            _overlay.SetStatus("Nie zsynchronizowano.", result.Error, isError: true);
            return;
        }

        _overlay.SetStatus(
            $"{DateTime.Now:HH:mm:ss} - zapisano {result.Applied.Count} zmian" + (skipped.Count > 0 ? $", pominięto {skipped.Count}" : string.Empty) + ".",
            Describe(skipped),
            isError: result.Applied.Count == 0 && skipped.Count > 0);
    }

    private void Finish(string hash)
    {
        lock (_gate)
        {
            _lastHash = hash;
        }
    }

    private static string Describe(IReadOnlyList<ScheduleExcelSkip> skipped)
    {
        if (skipped.Count == 0)
        {
            return string.Empty;
        }

        IEnumerable<string> lines = skipped.Take(3).Select(skip => $"{skip.Column}: {skip.Reason}");
        return string.Join(Environment.NewLine, lines) + (skipped.Count > 3 ? $"{Environment.NewLine}... więcej w logu" : string.Empty);
    }

    private static void LogSkipped(IEnumerable<ScheduleExcelSkip> skipped)
    {
        foreach (ScheduleExcelSkip skip in skipped)
        {
            ScheduleExcelLog.Info($"  pominięto {skip.RowKey} / {skip.Column}: {skip.Reason}");
        }
    }

    private void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            ScheduleExcelLog.Error($"{what}: wyjątek w wątku tła.", exception);
            TryRun(() => _overlay.SetStatus($"{what}: błąd.", exception.Message, isError: true));
        }
    }

    private static void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            ScheduleExcelLog.Error("Sprzątanie sesji.", exception);
        }
    }

    private sealed class SyncHandler : IExternalEventHandler
    {
        private readonly ScheduleExcelSyncSession _session;

        public SyncHandler(ScheduleExcelSyncSession session)
        {
            _session = session;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                _session.ApplyPending(app);
            }
            catch (Exception exception)
            {
                ScheduleExcelLog.Error("Synchronizacja w Revit nie powiodła się.", exception);
                _session._overlay.SetStatus("Błąd synchronizacji.", exception.Message, isError: true);
            }
        }

        public string GetName() => "HCB Excel Sync";
    }
}

/// <summary>Active sessions; ends them when their model or Revit closes.</summary>
internal static class ScheduleExcelSyncManager
{
    private static readonly object Gate = new();
    private static readonly List<ScheduleExcelSyncSession> Sessions = [];
    private static bool _subscribed;

    public static void Add(UIApplication application, ScheduleExcelSyncSession session)
    {
        lock (Gate)
        {
            Sessions.Add(session);
        }

        if (!_subscribed)
        {
            application.Application.DocumentClosing += OnDocumentClosing;
            application.ApplicationClosing += OnApplicationClosing;
            _subscribed = true;
        }
    }

    public static void Remove(ScheduleExcelSyncSession session)
    {
        lock (Gate)
        {
            Sessions.Remove(session);
        }
    }

    public static ScheduleExcelSyncSession? Find(string filePath)
    {
        string full = Path.GetFullPath(filePath);
        lock (Gate)
        {
            return Sessions.FirstOrDefault(session => string.Equals(session.FilePath, full, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static IReadOnlyList<ScheduleExcelSyncSession> All()
    {
        lock (Gate)
        {
            return Sessions.ToList();
        }
    }

    private static void OnDocumentClosing(object? sender, DocumentClosingEventArgs args)
    {
        foreach (ScheduleExcelSyncSession session in All().Where(session => session.Document.Equals(args.Document)))
        {
            session.Stop("zamykanie modelu");
        }
    }

    private static void OnApplicationClosing(object? sender, ApplicationClosingEventArgs args)
    {
        foreach (ScheduleExcelSyncSession session in All())
        {
            session.Stop("zamykanie Revit");
        }
    }
}
