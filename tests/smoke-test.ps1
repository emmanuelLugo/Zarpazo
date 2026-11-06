# End-to-end check of the skeleton: drives the real app with synthetic input and reads the real screen.
# Run from the repo root, after building, with PowerShell 7 and nothing else moving on screen:
#   pwsh tests/smoke-test.ps1
# It takes over the mouse for a few seconds and restores its position at the end.
param([string]$Exe = "$PSScriptRoot\..\Zarpazo\bin\Debug\net8.0-windows\Zarpazo.exe")

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Smoke -Name Win32 -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
[DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
[DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out System.Drawing.Point p);
'@ -ReferencedAssemblies System.Drawing.Primitives

# Same coordinate space as the app, so the numbers below are physical pixels.
[Smoke.Win32]::SetProcessDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null

# Another screenshot app would answer PrtScn with its own darkened overlay and fool the pixel checks.
if (-not [Smoke.Win32]::RegisterHotKey([IntPtr]::Zero, 1, 0x4000, 0x2C)) {
    Write-Host 'PrtScn is taken by another app (Lightshot, ShareX, Snipping...). Close it and run this again.'
    exit 2
}
[Smoke.Win32]::UnregisterHotKey([IntPtr]::Zero, 1) | Out-Null

$failures = 0
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    Write-Host ("{0}  {1} {2}" -f ($ok ? 'OK  ' : 'FAIL'), $name, $detail)
    if (-not $ok) { $script:failures++ }
}

function Press([byte]$vk) {
    [Smoke.Win32]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    [Smoke.Win32]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
}

# Filters by process because any other window, like an open folder, can also be titled "Zarpazo".
function Find-Overlay {
    $hwnd = [IntPtr]::Zero
    # [NullString]: PowerShell would turn $null into "", which FindWindowEx treats as a real class name.
    while (($hwnd = [Smoke.Win32]::FindWindowEx([IntPtr]::Zero, $hwnd, [NullString]::Value, 'Zarpazo')) -ne [IntPtr]::Zero) {
        [uint32]$owner = 0
        [Smoke.Win32]::GetWindowThreadProcessId($hwnd, [ref]$owner) | Out-Null
        if ($owner -eq $app.Id) { return $hwnd }
    }
    return [IntPtr]::Zero
}

function Wait-Until([scriptblock]$condition, [int]$timeoutMs = 3000) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $timeoutMs) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 50
    }
    return $false
}

function Grab($x, $y, $w, $h) {
    $bitmap = [Drawing.Bitmap]::new($w, $h)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($x, $y, 0, 0, [Drawing.Size]::new($w, $h))
    $graphics.Dispose()
    return $bitmap
}

# Fraction of sampled pixels in the given region where $after equals $before * $factor, give or take 3 levels.
function Match-Fraction($before, $after, $left, $top, $right, $bottom, [double]$factor) {
    $hits = 0; $total = 0
    for ($y = $top; $y -lt $bottom; $y += [Math]::Max(1, [int](($bottom - $top) / 40))) {
        for ($x = $left; $x -lt $right; $x += [Math]::Max(1, [int](($right - $left) / 40))) {
            $a = $before.GetPixel($x, $y); $b = $after.GetPixel($x, $y)
            $total++
            if ([Math]::Abs($a.R * $factor - $b.R) -le 3 -and
                [Math]::Abs($a.G * $factor - $b.G) -le 3 -and
                [Math]::Abs($a.B * $factor - $b.B) -le 3) { $hits++ }
        }
    }
    return $hits / $total
}

$vx = [Smoke.Win32]::GetSystemMetrics(76); $vy = [Smoke.Win32]::GetSystemMetrics(77)
$vw = [Smoke.Win32]::GetSystemMetrics(78); $vh = [Smoke.Win32]::GetSystemMetrics(79)
Write-Host "Virtual screen: origin ($vx,$vy), size ${vw}x$vh"

$cursor = [Drawing.Point]::Empty
[Smoke.Win32]::GetCursorPos([ref]$cursor) | Out-Null
$app = Start-Process $Exe -PassThru
try {
    Start-Sleep -Milliseconds 1500
    Check 'the app stays running' (-not $app.HasExited)

    $second = Start-Process $Exe -PassThru
    Check 'a second instance exits by itself' ($second.WaitForExit(5000))
    Check 'the first instance survives' (-not $app.HasExited)

    # Park the cursor so it isn't in the before/after comparisons.
    [Smoke.Win32]::SetCursorPos($vx + 5, $vy + 5) | Out-Null
    Start-Sleep -Milliseconds 300
    $before = Grab $vx $vy $vw $vh

    Press 0x2C
    Check 'PrtScn opens the overlay' (Wait-Until { (Find-Overlay) -ne [IntPtr]::Zero })
    Start-Sleep -Milliseconds 500

    $rect = [Smoke.Win32+RECT]::new()
    [Smoke.Win32]::GetWindowRect((Find-Overlay), [ref]$rect) | Out-Null
    Check 'the overlay covers the virtual screen' (
        $rect.Left -eq $vx -and $rect.Top -eq $vy -and
        ($rect.Right - $rect.Left) -eq $vw -and ($rect.Bottom - $rect.Top) -eq $vh
    ) "($($rect.Left),$($rect.Top))-($($rect.Right),$($rect.Bottom))"

    $dimmed = Grab $vx $vy $vw $vh
    $fraction = Match-Fraction $before $dimmed 0 0 $vw $vh 0.5
    Check 'the screen is frozen and darkened' ($fraction -gt 0.95) "match=$fraction"

    # Drag across the middle of the virtual screen. Coordinates are bitmap-relative below.
    $l = [int]($vw * 0.3); $t = [int]($vh * 0.3); $r = [int]($vw * 0.7); $b = [int]($vh * 0.7)
    [Smoke.Win32]::SetCursorPos($vx + $r, $vy + $b) | Out-Null
    Start-Sleep -Milliseconds 100
    [Smoke.Win32]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [Smoke.Win32]::SetCursorPos($vx + $l, $vy + $t) | Out-Null   # dragging backwards on purpose
    Start-Sleep -Milliseconds 100
    [Smoke.Win32]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    [Smoke.Win32]::SetCursorPos($vx + 5, $vy + 5) | Out-Null
    Start-Sleep -Milliseconds 300

    $selected = Grab $vx $vy $vw $vh
    $inside = Match-Fraction $before $selected ($l + 4) ($t + 4) ($r - 4) ($b - 4) 1.0
    Check 'the selection shows the undimmed capture' ($inside -gt 0.95) "match=$inside"
    $outside = Match-Fraction $before $selected 0 0 $vw ($t - 4) 0.5
    Check 'outside the selection stays darkened' ($outside -gt 0.95) "match=$outside"

    Press 0x1B
    Check 'Esc closes the overlay' (Wait-Until { (Find-Overlay) -eq [IntPtr]::Zero })

    Press 0x2C
    Check 'PrtScn works again after Esc' (Wait-Until { (Find-Overlay) -ne [IntPtr]::Zero })
    Start-Sleep -Milliseconds 300
    Press 0x1B
    Check 'Esc closes it again' (Wait-Until { (Find-Overlay) -eq [IntPtr]::Zero })
    Check 'the app is still running' (-not $app.HasExited)
}
finally {
    [Smoke.Win32]::SetCursorPos($cursor.X, $cursor.Y) | Out-Null
    if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force }
}

Write-Host ($failures -eq 0 ? 'All checks passed.' : "$failures check(s) failed.")
exit ($failures -eq 0 ? 0 : 1)
