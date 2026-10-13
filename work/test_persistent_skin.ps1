param([string]$ExecutablePath)
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$skinExecutable = if ($ExecutablePath) { $ExecutablePath } else {
  Join-Path $PSScriptRoot 'CodexDreamSkin/bin/Release/net8.0-windows/CodexDreamSkin.exe'
}
$skinProfile = Join-Path $PSScriptRoot 'cdp-probe-profile'
$manager = $null
function Start-Manager {
  $script:manager = Start-Process -FilePath $skinExecutable -ArgumentList ('--profile-dir="' + $skinProfile + '"') -PassThru -WindowStyle Hidden
  for ($attempt=0; $attempt -lt 35; $attempt++) {
    Start-Sleep -Milliseconds 200; $script:manager.Refresh()
    if ($script:manager.MainWindowHandle -ne 0) {
      $script:window=[System.Windows.Automation.AutomationElement]::FromHandle($script:manager.MainWindowHandle)
      return
    }
  }
  throw 'Manager window missing'
}
function Invoke-Button([string]$name) {
  $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name)
  $button=$script:window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
  if(-not $button){throw "Button missing: $name"}
  for($attempt=0;$attempt -lt 50 -and -not $button.Current.IsEnabled;$attempt++){Start-Sleep -Milliseconds 150}
  $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Read-Skin {
  $output=node work/cdp-probe.cjs eval work/check-internal.js 2>$null
  if($LASTEXITCODE -ne 0){return $null}
  return (($output | Out-String | ConvertFrom-Json).result.value | ConvertFrom-Json)
}
function Wait-Skin([string]$effect) {
  $testCodex = Get-Process -Id ([int](Get-Content work/probe-pid.txt))
  Start-Process -FilePath $testCodex.Path -ArgumentList ('--user-data-dir="' + $skinProfile + '"') -WindowStyle Hidden | Out-Null
  for($attempt=0;$attempt -lt 35;$attempt++) {
    Start-Sleep -Milliseconds 500
    $state=Read-Skin
    if($state -and $state.host -and $state.img -gt 0 -and $state.textOpacity -eq '1' -and $state.effect -eq $effect -and $state.frames -gt 2){return $state}
  }
  throw "Theme $effect did not render: $($state | ConvertTo-Json -Compress)"
}
function Close-Manager {
  $script:manager.CloseMainWindow() | Out-Null
  if(-not $script:manager.WaitForExit(5000)){throw 'Manager remained running after close'}
  $script:manager.Dispose();$script:manager=$null
}
function Select-Theme([string]$name) {
  $condition=[System.Windows.Automation.AndCondition]::new(
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name),
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
  $item=$script:window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
  if(-not $item){throw "Theme item missing: $name"}
  for($attempt=0;$attempt -lt 80 -and -not $item.Current.IsEnabled;$attempt++){Start-Sleep -Milliseconds 150}
  $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
try {
  Start-Manager
  Select-Theme '不知火 · 神社双焰'
  Invoke-Button '应用主题 / 启动 Codex'
  for($attempt=0;$attempt -lt 35;$attempt++) {
    Start-Sleep -Milliseconds 500
    try {
      $skinPort=[int](Get-Content (Join-Path $skinProfile 'DevToolsActivePort') -TotalCount 1)
      $connection=Get-NetTCPConnection -State Listen -LocalPort $skinPort | Select-Object -First 1
      $targets=Invoke-RestMethod -Uri ('http://127.0.0.1:'+$skinPort+'/json/list') -TimeoutSec 1
      if($targets | Where-Object url -eq 'app://-/index.html'){$connection.OwningProcess | Set-Content work/probe-pid.txt;break}
    } catch {}
  }
  node work/cdp-probe.cjs front
  $state=Wait-Skin 'shrine'
  node work/cdp-probe.cjs screenshot
  Copy-Item work/codex-internal-test.png outputs/CodexDreamSkin-v4/实际效果-神社双焰.png -Force
  Close-Manager
  $before=Read-Skin;Start-Sleep -Seconds 1;$after=Read-Skin
  if(-not $after.host -or $after.frames -le $before.frames){throw 'Effects stopped after manager exit'}
  Write-Output 'PASS manager process exited; background and particles continue'
  node work/cdp-probe.cjs reload
  $state=Wait-Skin 'shrine'
  Write-Output 'PASS page reload preserves effects while manager is closed'
  Start-Manager
  $state=Wait-Skin 'shrine'
  foreach($variant in @(@{Name='不知火 · 天雷夜雨';Effect='storm';File='天雷夜雨'},@{Name='不知火 · 月海潮汐';Effect='tide';File='月海潮汐'},@{Name='不知火 · 赤月轮舞';Effect='eclipse';File='赤月轮舞'})) {
    Select-Theme $variant.Name
    $state=Wait-Skin $variant.Effect
    if($state.canvasCount -ne 1){throw 'Particle canvas accumulated after theme switch'}
    node work/cdp-probe.cjs screenshot
    Copy-Item work/codex-internal-test.png ('outputs/CodexDreamSkin-v4/实际效果-'+$variant.File+'.png') -Force
    Write-Output ('PASS theme '+$variant.Effect+' decoded and animated')
  }
  Invoke-Button '关闭效果'
  for($attempt=0;$attempt -lt 25;$attempt++){Start-Sleep -Milliseconds 200;$off=Read-Skin;if(-not $off.host -and -not $off.style){break}}
  if($off.host -or $off.style -or $off.theme -ne 'light'){throw 'Explicit stop did not restore original theme'}
  Close-Manager
  node work/cdp-probe.cjs reload
  for($attempt=0;$attempt -lt 25;$attempt++){Start-Sleep -Milliseconds 400;$off=Read-Skin;if($off.textOpacity -eq '1'){break}}
  if($off.host -or $off.style){throw 'Removed injection returned after reload'}
  Start-Manager;Start-Sleep -Seconds 1;$off=Read-Skin
  if($off.host){throw 'Disabled preference unexpectedly re-enabled effect'}
  Close-Manager
  Write-Output 'PASS reopen then stop; reload and next manager launch remain disabled'
}
finally {
  if($script:manager -and -not $script:manager.HasExited){$script:manager.CloseMainWindow() | Out-Null;$script:manager.WaitForExit(5000) | Out-Null}
}
