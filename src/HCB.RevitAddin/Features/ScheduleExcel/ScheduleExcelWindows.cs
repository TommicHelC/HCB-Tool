using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>
/// Finds the Excel window of a workbook by its title ("Name.xlsx - Excel"). Excel 2013+ gives each workbook
/// its own top-level window, so this detects "workbook closed" without touching the file (holding a handle,
/// even briefly, could make Excel's save fail).
/// </summary>
internal static class ScheduleExcelWindows
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTopmost = 0x00000008;

    /// <summary>Visible top-level window whose title holds the workbook name and "Excel"; IntPtr.Zero if none.</summary>
    public static IntPtr FindWorkbookWindow(string filePath)
    {
        string name = Path.GetFileNameWithoutExtension(filePath);
        IntPtr found = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            int length = GetWindowTextLength(handle);
            if (length == 0)
            {
                return true;
            }

            var text = new StringBuilder(length + 1);
            GetWindowText(handle, text, text.Capacity);
            string title = text.ToString();
            if (title.Contains(name, StringComparison.OrdinalIgnoreCase) && title.Contains("Excel", StringComparison.OrdinalIgnoreCase))
            {
                found = handle;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Tool window that never takes focus and stays on top (no taskbar entry).</summary>
    public static void MakeNonActivatingTopmost(IntPtr handle)
    {
        int style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExToolWindow | WsExNoActivate | WsExTopmost);
    }
}
