<#
Captures the HCB Tools ribbon from a running Revit for the README documentation.

    powershell -ExecutionPolicy Bypass -File tools\docs\capture_ribbon.ps1 [-OutDir .scrape\ribbon] [-Pulldowns]

Requirements: Revit running with the HCB Tools tab active and visible, nothing covering the ribbon.
Writes:
  ribbon.png          - screenshot of the ribbon strip
  layout.json         - panel and item rectangles (screen pixels, ribbon.png origin in "origin")
  pulldown-<n>-before.png / -after.png / .json
                      - with -Pulldowns: the same region before and after opening pulldown n (tab order)

The script only clicks pulldown buttons (they open a menu, they never run a command) and closes each
menu with ESC. Do not touch the mouse while it runs.
#>
param(
    [string]$OutDir = ".scrape\ribbon",
    [switch]$Pulldowns,
    [int[]]$Only = @()   # re-capture only these pulldown numbers (1 = first on the tab)
)

$ErrorActionPreference = "Stop"
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Native {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
}
"@
[Native]::SetProcessDpiAwarenessContext([IntPtr]-4) | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$A = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Rect($e) {
    $r = $e.Current.BoundingRectangle
    if ($r.IsEmpty -or [double]::IsInfinity($r.X) -or [double]::IsInfinity($r.Width)) { return $null }
    @{ x = [int]$r.X; y = [int]$r.Y; w = [int]$r.Width; h = [int]$r.Height }
}
# Stacked rows are containers without a rectangle - flatten them to their buttons, top to bottom.
function RibbonItems($e) {
    $out = @()
    foreach ($c in (Children $e)) {
        if ($c.Current.Name -like "Autodesk.Windows.Ribbon*Button" -and (Rect $c)) { $out += $c }
        elseif (-not (Rect $c)) { $out += RibbonItems $c }
    }
    $out
}
function Children($e) { $out = @(); $c = $walker.GetFirstChild($e); while ($c) { $out += $c; $c = $walker.GetNextSibling($c) }; $out }
function Shot($x, $y, $w, $h, $path) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
function Click($x, $y) {
    [Native]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 120
    [Native]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero); [Native]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
}
function Esc { [Native]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero); [Native]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero) }

$proc = Get-Process Revit | Select-Object -First 1
[Native]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 500
$win = $A::FromHandle($proc.MainWindowHandle)

$scroll = $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, "HCB Tools_PanelBarScrollViewer")))
if (-not $scroll) { throw "HCB Tools tab not found - open the tab in Revit first." }

$panels = @()
$pulldownItems = @()
$prefix = "CustomCtrl_%HCB Tools%"
$all = $scroll.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Custom)))
foreach ($p in $all) {
    $id = $p.Current.AutomationId
    if (-not $id.StartsWith($prefix) -or $id.EndsWith("_PanelTitleBar")) { continue }
    $view = Children $p | Where-Object { $_.Current.AutomationId -eq "mRibbonPanelView" } | Select-Object -First 1
    $items = @()
    foreach ($it in (RibbonItems $view)) {
        $kind = $it.Current.Name.Replace("Autodesk.Windows.", "")
        $label = (Children $it | Select-Object -First 1)
        $entry = @{ kind = $kind; text = $(if ($label) { $label.Current.Name } else { "" }); rect = (Rect $it) }
        $items += $entry
        if ($kind -eq "RibbonSplitButton") { $pulldownItems += $entry }
    }
    $panels += @{ name = $id.Substring($prefix.Length); rect = (Rect $p); items = $items }
}

$right = ($panels | ForEach-Object { $_.rect.x + $_.rect.w } | Measure-Object -Maximum).Maximum
$top = [Math]::Max(0, ($panels[0].rect.y) - 30)
$bottom = ($panels | ForEach-Object { $_.rect.y + $_.rect.h } | Measure-Object -Maximum).Maximum
$origin = @{ x = 0; y = $top; w = [int]$right + 4; h = [int]($bottom - $top + 2) }
Shot $origin.x $origin.y $origin.w $origin.h (Join-Path $OutDir "ribbon.png")
$revit = if ($proc.MainWindowTitle -match "Revit (\d{4})") { [int]$Matches[1] } else { 0 }
@{ origin = $origin; revit = $revit; panels = $panels } | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $OutDir "layout.json")
"ribbon: {0} panels, {1} pulldowns" -f $panels.Count, $pulldownItems.Count

if (-not $Pulldowns) { return }

# Pulldown menus are WPF popups that UI Automation does not expose, so each menu is captured as a
# before/after pair of the same screen region; make_images.py finds the menu from the difference.
$region = @{ x = 0; y = $origin.y; w = [int]($origin.w + 400); h = 720 }
function Park { [Native]::SetCursorPos(($origin.w + 300), ($origin.y + 700)) | Out-Null; Start-Sleep -Milliseconds 300 }
# Warm-up: the first synthetic click after focusing Revit is sometimes swallowed.
[Native]::SetCursorPos(($origin.w / 2), ($origin.y + 60)) | Out-Null; Start-Sleep -Milliseconds 600
$n = 0
foreach ($pd in $pulldownItems) {
    $n++
    if ($Only.Count -gt 0 -and $Only -notcontains $n) { continue }
    $r = $pd.rect
    Park
    Shot $region.x $region.y $region.w $region.h (Join-Path $OutDir "pulldown-$n-before.png")
    # AdWindows ignores a bare click - the cursor has to hover over the button first.
    $cx = [int]($r.x + $r.w / 2); $cy = [int]($r.y + $r.h / 2)
    foreach ($k in 0..8) { [Native]::SetCursorPos($cx - 8 + $k, $cy - 8 + $k) | Out-Null; Start-Sleep -Milliseconds 50 }
    Start-Sleep -Milliseconds 350
    [Native]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 150
    [Native]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 900
    Shot $region.x $region.y $region.w $region.h (Join-Path $OutDir "pulldown-$n-after.png")
    Esc
    Start-Sleep -Milliseconds 300
    @{ origin = $region; button = $r; text = $pd.text } | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $OutDir "pulldown-$n.json")
    "pulldown {0}: {1}" -f $n, $pd.text
}
Park
