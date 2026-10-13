param([string]$ExecutablePath,[string]$ProfileDirectory,[string]$OutputPath)
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CaptureSkinWindow {
  public struct Rect { public int Left,Top,Right,Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
}
'@
$startOptions = @{FilePath=$ExecutablePath;WindowStyle='Hidden';PassThru=$true}
if($ProfileDirectory){$startOptions.ArgumentList='--profile-dir="'+$ProfileDirectory+'"'}
$skinManager = Start-Process @startOptions
try {
  for($attempt=0;$attempt -lt 35;$attempt++) {
    Start-Sleep -Milliseconds 200;$skinManager.Refresh()
    if($skinManager.MainWindowHandle -ne 0){break}
  }
  Start-Sleep -Seconds 5
  $rect=[CaptureSkinWindow+Rect]::new()
  [CaptureSkinWindow]::GetWindowRect($skinManager.MainWindowHandle,[ref]$rect) | Out-Null
  $bitmap=[System.Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top)
  $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
  $dc=$graphics.GetHdc()
  try{[CaptureSkinWindow]::PrintWindow($skinManager.MainWindowHandle,$dc,2) | Out-Null}
  finally{$graphics.ReleaseHdc($dc)}
  $bitmap.Save($OutputPath,[System.Drawing.Imaging.ImageFormat]::Png)
  $graphics.Dispose();$bitmap.Dispose()
  Write-Output $OutputPath
}
finally{
  if($skinManager -and !$skinManager.HasExited){
    $skinManager.CloseMainWindow() | Out-Null
    $skinManager.WaitForExit(5000) | Out-Null
  }
}
