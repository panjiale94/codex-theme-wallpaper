# 在真实当前用户上验收计划任务。保留并恢复主题配置，不结束 Codex 工作实例。
$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$profile=Join-Path $env:APPDATA 'Codex\web\Codex'
$sha=[Security.Cryptography.SHA256]::Create()
try{$key=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($profile.ToUpperInvariant())))).Replace('-','').Substring(0,16)}finally{$sha.Dispose()}
$settingsRegistry='HKCU:\Software\CodexDreamSkin\Profiles\'+$key
$originalSettings=Get-ItemPropertyValue -LiteralPath $settingsRegistry -Name Settings
$installed=Get-ItemPropertyValue -LiteralPath HKCU:\Software\CodexDreamSkin -Name InstalledWorker
$runtimeRoot=Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) ('CodexDreamSkin\Users\'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value+'\Runtime')
if(!$installed.StartsWith($runtimeRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected runtime installation'}
$startupLink=Join-Path ([Environment]::GetFolderPath('Startup')) 'CodexDreamSkin AutoRestore.lnk'
$taskName='CodexDreamSkin.AutoRestore'
$sharedSettings=Join-Path (Split-Path -Parent $runtimeRoot) ('Profiles\settings-'+$key+'.json')
if(Test-Path -LiteralPath $sharedSettings){$originalSettings=[IO.File]::ReadAllText($sharedSettings)}
$pipeName='codex-dream-skin-'+$key
function Send-GuardianCommand([string]$command){
 $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$pipeName,[IO.Pipes.PipeDirection]::InOut)
 try{
  $pipe.Connect(2000)
  $writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),4096,$true);$writer.AutoFlush=$true
  $reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8,$false,4096,$true)
  $writer.WriteLine((@{Command=$command;AllowLaunch=$false}|ConvertTo-Json -Compress))
  return ($reader.ReadLine()|ConvertFrom-Json)
 }finally{$pipe.Dispose()}
}
function Wait-Guardian {
 for($attempt=0;$attempt -lt 40;$attempt++){
  try{$reply=Send-GuardianCommand 'version';if($reply.Version -eq 7){
   $owned=@(Get-CimInstance Win32_Process -Filter "Name='CodexDreamSkin.Worker.exe'" | Where-Object {$_.ExecutablePath -eq $installed})
   if($owned.Count -eq 1){return (Get-Process -Id $owned[0].ProcessId)}
  }}catch{}
  Start-Sleep -Milliseconds 300
 };throw 'Scheduled v7 guardian did not start'
}
try{
 $task=Get-ScheduledTask -TaskName $taskName
 if($task.Actions.Execute -ne $installed -or $task.Settings.ExecutionTimeLimit -ne 'PT0S' -or $task.Settings.DisallowStartIfOnBatteries){throw 'Task settings incorrect'}
 if(!($task.Triggers | Where-Object {$_.CimClass.CimClassName -eq 'MSFT_TaskLogonTrigger'})){throw 'Logon trigger missing'}
 if(!($task.Triggers | Where-Object {$_.Repetition.Interval -eq 'PT1M'})){throw 'Watchdog trigger missing'}
 $shell=New-Object -ComObject WScript.Shell
 if($shell.CreateShortcut($startupLink).TargetPath -ne $installed){throw 'Startup folder fallback incorrect'}
 # 只回收本次诊断启动的旧守护，不触碰客户端或其他资料目录的守护。
 Get-CimInstance Win32_Process -Filter "Name='CodexDreamSkin.Worker.exe'" | Where-Object {
  $_.ExecutablePath -like '*\outputs\CodexDreamSkin-v6\CodexDreamSkin.Worker.exe' -and $_.CommandLine -notmatch '--profile-dir='
 } | ForEach-Object {Stop-Process -Id $_.ProcessId -Force}
 Start-ScheduledTask -TaskName $taskName
 $guardian=Wait-Guardian
 if(@($guardian.Modules.ModuleName|Where-Object {$_ -match 'PresentationFramework|PresentationCore|wpfgfx'}).Count){throw 'Worker loaded WPF'}
 Write-Output 'PASS real scheduled task launches installed v7 guardian without WPF'
 $duplicate=Start-Process -FilePath $installed -ArgumentList '--background --startup' -WindowStyle Hidden -PassThru
 if(!$duplicate.WaitForExit(5000)){throw 'Duplicate guardian stayed running'}
 Write-Output 'PASS startup fallback and scheduled task retain a single guardian'
 $crashPid=$guardian.Id
 $guardian.Kill();$guardian.WaitForExit()
 $timer=[Diagnostics.Stopwatch]::StartNew();$recovered=$null
 for($attempt=0;$attempt -lt 40;$attempt++){
  Start-Sleep -Seconds 3
  $owned=@(Get-CimInstance Win32_Process -Filter "Name='CodexDreamSkin.Worker.exe'" | Where-Object {$_.ExecutablePath -eq $installed -and $_.ProcessId -ne $crashPid})
  if($owned.Count -eq 1){$recovered=Get-Process -Id $owned[0].ProcessId;break}
 }
 if(!$recovered){throw 'Automatic watchdog did not restart killed guardian'}
 $reply=Send-GuardianCommand 'version';if($reply.Version -ne 7){throw 'Recovered guardian not responding'}
 $elapsed=[Math]::Round($timer.Elapsed.TotalSeconds,1)
 Write-Output ('PASS automatic watchdog recovery after '+$elapsed+' seconds; no manual task start')
 $recovered.Refresh();$memory=@{WorkingSetMB=[Math]::Round($recovered.WorkingSet64/1MB,1);PrivateMB=[Math]::Round($recovered.PrivateMemorySize64/1MB,1)}
 $reply=Send-GuardianCommand 'stop';if(!$reply.Success){throw $reply.Message}
 if(Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue){throw 'Stop left task installed'}
 if(Test-Path -LiteralPath $startupLink){throw 'Stop left startup link installed'}
 $disabled=Start-Process -FilePath $installed -ArgumentList '--background --startup' -WindowStyle Hidden -PassThru
 if(!$disabled.WaitForExit(5000)){throw 'Disabled startup stayed running'}
 Write-Output 'PASS explicit stop removes task and startup fallback; disabled login exits'
 @{Result='PASS';WatchdogRecoverySeconds=$elapsed;Memory=$memory;WindowsReboot='Not performed'}|ConvertTo-Json -Depth 4|Set-Content work/v7-startup-report.json -Encoding UTF8
}finally{
 Set-ItemProperty -LiteralPath $settingsRegistry -Name Settings -Value $originalSettings
 [IO.File]::WriteAllText($sharedSettings,$originalSettings,[Text.UTF8Encoding]::new($false))
 $repair=Start-Process -FilePath $installed -ArgumentList '--repair-startup' -WindowStyle Hidden -PassThru -Wait
 if($repair.ExitCode -ne 0){throw 'Unable to restore automatic startup'}
 Start-ScheduledTask -TaskName $taskName
 $final=Wait-Guardian
 Write-Output 'RESTORED original user theme and enabled automatic startup'
}
