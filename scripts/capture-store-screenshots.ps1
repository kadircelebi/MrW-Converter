<#
.SYNOPSIS
    Takes the raw pictures for the Store listing, in both languages, from the real app.

.DESCRIPTION
    Runs the given build with MRWCONVERTER_LANGUAGE and drives it through UI Automation patterns and
    messages to its own windows, on sample files made here from do_not_share\site\samples. It sends no
    mouse or keyboard input. A running copy is closed first through its tray menu, because the app runs
    one copy per session, and it stops if that copy is busy.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $OutDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$samples = Join-Path $root 'do_not_share\site\samples'
$maker = Join-Path $root 'third_party\ffmpeg\fixtures-win-x64\ffmpeg.exe'
if (-not (Test-Path (Join-Path $samples 'liman.png'))) { throw "The sample photos are missing from $samples." }
if (-not (Test-Path $maker)) { throw 'The full FFmpeg is missing. Run: scripts\setup-ffmpeg.ps1' }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class StoreShots {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public static IntPtr Tray(uint pid) {
    var found = IntPtr.Zero;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); var s = new StringBuilder(64); GetWindowText(h, s, 64); if (p == pid && s.ToString() == "MrWConverterTray") found = h; return true; }, IntPtr.Zero);
    return found;
  }
}
"@
[void][StoreShots]::SetProcessDPIAware()
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$Prop = 'System.Windows.Automation.PropertyCondition'
$IP = [System.Windows.Automation.InvokePattern]
$SI = [System.Windows.Automation.SelectionItemPattern]
$RV = [System.Windows.Automation.RangeValuePattern]

function ById($Root, $Id) { $Root.FindFirst($TS::Descendants, (New-Object $Prop($AE::AutomationIdProperty, $Id))) }
function Press($Element) { $Element.GetCurrentPattern($IP::Pattern).Invoke(); Start-Sleep -Milliseconds 500 }
function Buttons($Root) { @($Root.FindAll($TS::Descendants, (New-Object $Prop($AE::ControlTypeProperty, $CT::Button)))) }
function ToolButton($Root, $Name) { Buttons $Root | Where-Object { $_.Current.Name -eq $Name } | Select-Object -First 1 }
function WindowOf($Element) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $window = $Element
    do { $window = $walker.GetParent($window) } until ($window.Current.ControlType -eq $CT::Window)
    $window
}
function Shot($Window, $Path) {
    $handle = [IntPtr]$Window.Current.NativeWindowHandle
    $rect = New-Object StoreShots+RECT
    [void][StoreShots]::GetWindowRect($handle, [ref]$rect)
    $bitmap = New-Object System.Drawing.Bitmap ($rect.R - $rect.L), ($rect.B - $rect.T)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    [void][StoreShots]::PrintWindow($handle, $dc, 2)
    $graphics.ReleaseHdc($dc)
    $graphics.Dispose()
    $bitmap.Save($Path)
    $bitmap.Dispose()
}
function Close-Running {
    foreach ($running in @(Get-Process MrWConverter -ErrorAction SilentlyContinue)) {
        $before = $running.CPU
        Start-Sleep -Seconds 1
        $running.Refresh()
        if (($running.CPU - $before) -gt 0.3) { throw "A running copy ($($running.Id)) is busy. Let it finish first." }
        [void][StoreShots]::PostMessage([StoreShots]::Tray($running.Id), 0x8001, [IntPtr]((600 -shl 16) -bor 800), [IntPtr]0x007B)
        $exit = $null
        $end = (Get-Date).AddSeconds(5)
        while ((Get-Date) -lt $end -and -not $exit) {
            Start-Sleep -Milliseconds 200
            foreach ($window in @($AE::RootElement.FindAll($TS::Children, (New-Object $Prop($AE::ProcessIdProperty, $running.Id))))) {
                $exit = @($window.FindAll($TS::Descendants, (New-Object $Prop($AE::ControlTypeProperty, $CT::MenuItem)))) | Select-Object -Last 1
                if ($exit) { break }
            }
        }
        if ($exit) { $exit.GetCurrentPattern($IP::Pattern).Invoke() }
        [void]$running.WaitForExit(6000)
        if (-not $running.HasExited) { throw "Could not close the running copy ($($running.Id))." }
    }
}
function Open-App([string] $Files) {
    $process = Start-Process $Exe -ArgumentList $Files -PassThru
    $end = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $end) {
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 150
    }
    Start-Sleep -Milliseconds 1800
    $process
}
function Stop-App($Process) {
    $Process.Kill()
    [void]$Process.WaitForExit(5000)
}

Close-Running
New-Item -ItemType Directory -Force $OutDir | Out-Null
# The window shows the folder it saves in, so the samples sit somewhere that says nothing about this machine.
$files = Join-Path $env:PUBLIC 'Documents\MrW Converter'
if (Test-Path $files) { throw "$files is already there, and this would delete it afterwards. Move it first." }
New-Item -ItemType Directory $files | Out-Null

$names = @{
    tr = @{ Photo = 'liman.jpg'; Bike = 'bisiklet.jpg'; Video = 'liman gezisi.mp4'; Sound = 'yaz.mp3'; Shrink = 'Küçült'; Edit = 'Düzenle'; Tools = 'Ara'; Cancel = 'İptal' }
    en = @{ Photo = 'harbour.jpg'; Bike = 'bicycle.jpg'; Video = 'harbour trip.mp4'; Sound = 'summer.mp3'; Shrink = 'Shrink'; Edit = 'Edit'; Tools = 'Tools'; Cancel = 'Cancel' }
}

try {
    foreach ($lang in 'tr', 'en') {
        $word = $names[$lang]
        $env:MRWCONVERTER_LANGUAGE = $lang
        $photo = Join-Path $files $word.Photo
        $video = Join-Path $files $word.Video
        $sound = Join-Path $files $word.Sound
        $bike = Join-Path $files $word.Bike
        & $maker -hide_banner -loglevel error -y -i (Join-Path $samples 'liman.png') -q:v 2 $photo
        & $maker -hide_banner -loglevel error -y -i (Join-Path $samples 'bisiklet.png') -q:v 2 $bike
        & $maker -hide_banner -loglevel error -y -loop 1 -i (Join-Path $samples 'liman.png') -f lavfi -i 'sine=f=220:d=24,volume=eval=frame:volume=0.3*abs(sin(t*2))' -t 24 -vf "zoompan=z='1.05+0.15*on/720':x='iw/2-(iw/zoom/2)+on/6':y='ih/2-(ih/zoom/2)':d=720:s=1920x1080:fps=30" -c:v h264_mf -rate_control quality -quality 70 -c:a aac_mf -b:a 128k -ar 48000 -ac 2 $video
        & $maker -hide_banner -loglevel error -y -f lavfi -i 'sine=f=330:d=20' -c:a libmp3lame $sound

        # The window with a list of files, and the wheel over it.
        $app = Open-App "`"$photo`" `"$bike`""
        $main = $AE::FromHandle($app.MainWindowHandle)
        Start-Sleep -Milliseconds 600
        Shot $main (Join-Path $OutDir "$lang-main.png")
        Press (ById $main 'OpenWheelButton')
        $byProcess = New-Object $Prop($AE::ProcessIdProperty, $app.Id)
        $isWindow = New-Object $Prop($AE::ControlTypeProperty, $CT::Window)
        $wheel = $null
        $end = (Get-Date).AddSeconds(5)
        while ((Get-Date) -lt $end -and -not $wheel) {
            $wheel = @(@($main.FindAll($TS::Children, $isWindow)) + @($AE::RootElement.FindAll($TS::Children, $byProcess) | Where-Object { $_.Current.NativeWindowHandle -ne $app.MainWindowHandle.ToInt32() })) | Select-Object -First 1
            Start-Sleep -Milliseconds 100
        }
        if (-not $wheel) { throw 'The wheel did not stay open. A key pressed while it had the focus closes it; try again.' }
        Start-Sleep -Milliseconds 400
        Shot $wheel (Join-Path $OutDir "$lang-wheel.png")
        Stop-App $app

        # Shrink, on a mix of files.
        $app = Open-App "`"$photo`" `"$video`""
        $main = $AE::FromHandle($app.MainWindowHandle)
        Press (ToolButton $main $word.Shrink)
        $save = $null
        $end = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $end -and -not $save) { $save = ById $main 'SaveButton'; Start-Sleep -Milliseconds 200 }
        $dialog = WindowOf $save
        (ById $dialog 'LimitChoice').GetCurrentPattern($SI::Pattern).Select()
        Start-Sleep -Milliseconds 700
        Shot $dialog (Join-Path $OutDir "$lang-shrink.png")
        Stop-App $app

        # The editor with a few clips, one of them removed.
        $app = Open-App "`"$video`""
        $main = $AE::FromHandle($app.MainWindowHandle)
        Press (ToolButton $main $word.Edit)
        $timeline = $null
        $end = (Get-Date).AddSeconds(15)
        while ((Get-Date) -lt $end -and -not $timeline) { $timeline = ById $main 'Timeline'; Start-Sleep -Milliseconds 200 }
        $dialog = WindowOf $timeline
        Start-Sleep -Seconds 2
        foreach ($step in @(@(6, 'SplitButton'), @(11, 'SplitButton'), @(18, 'SplitButton'), @(8, 'RemoveButton'))) {
            $timeline.GetCurrentPattern($RV::Pattern).SetValue($step[0])
            Start-Sleep -Milliseconds 700
            Press (ById $dialog $step[1])
        }
        $timeline.GetCurrentPattern($RV::Pattern).SetValue(15)
        Start-Sleep -Seconds 2
        Shot $dialog (Join-Path $OutDir "$lang-edit.png")
        Stop-App $app
    }
}
finally {
    Get-Process MrWConverter -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force
    Remove-Item Env:MRWCONVERTER_LANGUAGE -ErrorAction SilentlyContinue
    Remove-Item $files -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "Raw pictures are in $OutDir"
