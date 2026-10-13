param([string]$ExecutablePath)
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$manager=Start-Process -FilePath $ExecutablePath -WindowStyle Hidden -PassThru
try {
  for($attempt=0;$attempt -lt 35;$attempt++) {
    Start-Sleep -Milliseconds 200;$manager.Refresh()
    if($manager.MainWindowHandle -ne 0){break}
  }
  $window=[System.Windows.Automation.AutomationElement]::FromHandle($manager.MainWindowHandle)
  $itemCondition=[System.Windows.Automation.AndCondition]::new(
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'不知火 · 神社双焰'),
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
  $item=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$itemCondition)
  for($attempt=0;$attempt -lt 80 -and -not $item.Current.IsEnabled;$attempt++){Start-Sleep -Milliseconds 150}
  $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  $startCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'应用主题 / 启动 Codex')
  $button=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$startCondition)
  for($attempt=0;$attempt -lt 60 -and -not $button.Current.IsEnabled;$attempt++){Start-Sleep -Milliseconds 150}
  $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $env:CODEX_SKIN_PROFILE=Join-Path $env:APPDATA 'Codex/web/Codex'
  for($attempt=0;$attempt -lt 30;$attempt++) {
    Start-Sleep -Milliseconds 400
    $state=((node work/cdp-probe.cjs eval work/check-internal.js | Out-String | ConvertFrom-Json).result.value | ConvertFrom-Json)
    if($state.host -and $state.effect -eq 'shrine' -and $state.img -gt 0 -and $state.version -eq 4){break}
  }
  if(-not $state.host -or $state.effect -ne 'shrine' -or $state.version -ne 4){throw 'Current client theme did not apply'}
  $manager.CloseMainWindow() | Out-Null
  if(-not $manager.WaitForExit(5000)){throw 'Settings window did not exit'}
  Start-Sleep -Seconds 1
  $state=((node work/cdp-probe.cjs eval work/check-internal.js | Out-String | ConvertFrom-Json).result.value | ConvertFrom-Json)
  if(-not $state.host -or $state.effect -ne 'shrine'){throw 'Current theme disappeared after settings exit'}
  Write-Output ('Current Codex: '+($state | ConvertTo-Json -Compress))
}
finally {
  if(-not $manager.HasExited){$manager.CloseMainWindow() | Out-Null;$manager.WaitForExit(5000) | Out-Null}
}
