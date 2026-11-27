using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace HCB.RevitAddin.Features.ScheduleExcel.UI;

/// <summary>
/// Status card over Excel during a sync session. Lives on its own STA thread with its own Dispatcher,
/// so a busy Revit never freezes it and it never blocks Revit. All calls from other threads go through
/// <see cref="ScheduleExcelOverlay"/>.
/// </summary>
public partial class ScheduleExcelOverlayWindow : Window
{
    private readonly string _filePath;
    private readonly Action _onStop;
    private readonly DispatcherTimer _followTimer;

    public ScheduleExcelOverlayWindow(string filePath, string scheduleName, Action onStop)
    {
        InitializeComponent();
        _filePath = filePath;
        _onStop = onStop;
        ScheduleText.Text = $"{scheduleName}  ·  {Path.GetFileName(filePath)}";

        SourceInitialized += (_, _) => ScheduleExcelWindows.MakeNonActivatingTopmost(new WindowInteropHelper(this).Handle);
        _followTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => FollowExcel(), Dispatcher);
        Loaded += (_, _) =>
        {
            FollowExcel();
            _followTimer.Start();
        };
        Closed += (_, _) => _followTimer.Stop();
    }

    public void SetStatus(string status, string detail, bool isError)
    {
        StatusText.Text = status;
        StatusText.Foreground = (Brush)FindResource(isError ? "Brush.Danger" : "Brush.TextPrimary");
        DetailText.Text = detail;
        DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Top-right corner of the workbook window, below Excel's ribbon; hidden while Excel is minimized.</summary>
    private void FollowExcel()
    {
        try
        {
            IntPtr excel = ScheduleExcelWindows.FindWorkbookWindow(_filePath);
            if (excel != IntPtr.Zero && ScheduleExcelWindows.IsIconic(excel))
            {
                Visibility = Visibility.Hidden;
                return;
            }

            Visibility = Visibility.Visible;
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (excel != IntPtr.Zero && ScheduleExcelWindows.GetWindowRect(excel, out ScheduleExcelWindows.Rect rect))
            {
                Left = rect.Right / scale - ActualWidth - 28;
                Top = rect.Top / scale + 165;
            }
            else
            {
                Rect area = SystemParameters.WorkArea;
                Left = area.Right - ActualWidth - 16;
                Top = area.Bottom - ActualHeight - 16;
            }
        }
        catch (Exception exception)
        {
            ScheduleExcelLog.Error("Pozycjonowanie nakładki nie powiodło się.", exception);
        }
    }

    private void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        _onStop();
    }

    private void LogButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ScheduleExcelLog.CurrentFile) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ScheduleExcelLog.Error("Nie udało się otworzyć logu.", exception);
        }
    }
}

/// <summary>Thread-safe handle to the overlay window running on its own UI thread.</summary>
internal sealed class ScheduleExcelOverlay
{
    private ScheduleExcelOverlayWindow? _window;
    private Dispatcher? _dispatcher;

    public static ScheduleExcelOverlay Start(string filePath, string scheduleName, Action onStop)
    {
        var overlay = new ScheduleExcelOverlay();
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                overlay._dispatcher = Dispatcher.CurrentDispatcher;

                // An unhandled exception on this thread would take the whole Revit process down.
                overlay._dispatcher.UnhandledException += (_, args) =>
                {
                    ScheduleExcelLog.Error("Nieobsłużony wyjątek w nakładce synchronizacji.", args.Exception);
                    args.Handled = true;
                };
                overlay._window = new ScheduleExcelOverlayWindow(filePath, scheduleName, onStop);
                overlay._window.Closed += (_, _) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                overlay._window.Show();
            }
            catch (Exception exception)
            {
                ScheduleExcelLog.Error("Nie udało się pokazać nakładki synchronizacji.", exception);
                overlay._window = null;
            }
            finally
            {
                ready.Set();
            }

            if (overlay._window != null)
            {
                Dispatcher.Run();
            }
        })
        {
            IsBackground = true,
            Name = "HCB Excel Sync overlay"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        return overlay;
    }

    public void SetStatus(string status, string detail = "", bool isError = false)
    {
        Post(window => window.SetStatus(status, detail, isError));
    }

    public void Close()
    {
        Post(window => window.Close());
    }

    private void Post(Action<ScheduleExcelOverlayWindow> action)
    {
        ScheduleExcelOverlayWindow? window = _window;
        Dispatcher? dispatcher = _dispatcher;
        if (window == null || dispatcher == null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            try
            {
                action(window);
            }
            catch (Exception exception)
            {
                ScheduleExcelLog.Error("Aktualizacja nakładki nie powiodła się.", exception);
            }
        });
    }
}
