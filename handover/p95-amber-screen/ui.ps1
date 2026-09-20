Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if (-not ('W95' -as [type])) {
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W95 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
}
"@
}
function Click([int]$x, [int]$y, [int]$n = 1) {
  [W95]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 120
  for ($i = 0; $i -lt $n; $i++) {
    [W95]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [W95]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
  }
}
function RClick([int]$x, [int]$y) {
  [W95]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 120
  [W95]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); [W95]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero)
}
function Keys([string]$s) { [System.Windows.Forms.SendKeys]::SendWait($s) }
function Shot([string]$path) {
  $b = New-Object System.Drawing.Bitmap 1920, 1080
  $g = [System.Drawing.Graphics]::FromImage($b)
  $g.CopyFromScreen(0, 0, 0, 0, $b.Size); $g.Dispose()
  $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
  $path
}
function Fg() {
  $h = [W95]::GetForegroundWindow(); $sb = New-Object System.Text.StringBuilder 512
  [W95]::GetWindowText($h, $sb, 512) | Out-Null; "fg=[$($sb.ToString())]"
}
function App95() { Get-Process BecquerelMonitor -ErrorAction SilentlyContinue | Where-Object { $_.Path -like 'D:\BqMoni_Claude\p95*' } }
function Front() {
  $p = App95; if ($p) { [W95]::ShowWindow($p.MainWindowHandle, 9) | Out-Null; [W95]::SetForegroundWindow($p.MainWindowHandle) | Out-Null }
}
