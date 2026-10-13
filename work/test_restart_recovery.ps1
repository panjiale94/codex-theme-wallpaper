param([string]$ExecutablePath)
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$workerPath = if($ExecutablePath){$ExecutablePath}else{Join-Path (Get-Location) 'outputs\CodexDreamSkin-v5\CodexDreamSkin.Worker.exe'}
$testProfile = Join-Path $PSScriptRoot 'v5-test-profile'
$env:CODEX_SKIN_PROFILE = $testProfile
$sha = [Security.Cryptography.SHA256]::Create()
try {$key=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($testProfile.ToUpperInvariant())))).Replace('-','').Substring(0,16)}finally{$sha.Dispose()}
$settingsPath=Join-Path $env:APPDATA ('CodexDreamSkin\settings-'+$key+'.json')
$settingsRegistry='HKCU:\Software\CodexDreamSkin\Profiles\'+$key
$pipeName='codex-dream-skin-'+$key
$worker=$null
function Save-Theme([string]$preset='center-shrine',[string]$media=$null){
 $settings=@{Enabled=$true;AutoStart=$true;PresetId=$preset;Panel=35;Motion=100;CustomMediaPath=$media}
 New-Item -Path $settingsRegistry -Force | Out-Null
 Set-ItemProperty -LiteralPath $settingsRegistry -Name Settings -Value ($settings | ConvertTo-Json -Compress) -Type String
}
function Start-Worker {
 $script:worker=Start-Process -FilePath $workerPath -ArgumentList ('--background --startup --profile-dir="'+$testProfile+'"') -WindowStyle Hidden -PassThru
}
function Send-Command([string]$command){
 $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$pipeName,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
 try{
  $pipe.Connect(5000)
  $writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),4096,$true);$writer.AutoFlush=$true
  $reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8,$false,4096,$true)
  $writer.WriteLine((@{Command=$command;AllowLaunch=$false}|ConvertTo-Json -Compress))
  $reply=$reader.ReadLine()|ConvertFrom-Json
  if(!$reply.Success){throw $reply.Message};return $reply
 }finally{$pipe.Dispose()}
}
function Read-State {
 try{$output=node work/cdp-probe.cjs eval work/check-internal.js 2>$null;if($LASTEXITCODE -ne 0){return $null};return (($output|Out-String|ConvertFrom-Json).result.value|ConvertFrom-Json)}catch{return $null}
}
function Client-Pid {
 try{$port=[int](Get-Content -LiteralPath (Join-Path $testProfile 'DevToolsActivePort') -TotalCount 1);return (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction Stop | Select-Object -First 1).OwningProcess}catch{return $null}
}
function Launch-Client {
 Start-Process -FilePath $workerPath -ArgumentList ('--launch --profile-dir="'+$testProfile+'"') -WindowStyle Hidden | Out-Null
 for($attempt=0;$attempt -lt 40;$attempt++){Start-Sleep -Milliseconds 300;$clientPid=Client-Pid;if($clientPid){
  $client=Get-Process -Id $clientPid
  Start-Process -FilePath $client.Path -ArgumentList ('--user-data-dir="'+$testProfile+'"') -WindowStyle Hidden | Out-Null
  return
 }};throw 'Restoring launcher failed'
}
function Wait-State([string]$effect){
 for($attempt=0;$attempt -lt 45;$attempt++){Start-Sleep -Milliseconds 350;$state=Read-State
  if($state -and $state.host -and $state.effect -eq $effect -and $state.version -eq 5 -and $state.img -gt 0 -and $state.livingArt -eq 'gpu' -and $state.frames -gt 2){return $state}}
 throw ('Theme did not recover: '+($state|ConvertTo-Json -Compress))
}
function Close-Client {
 $clientPid=Client-Pid;if(!$clientPid){return}
 $owned=Get-CimInstance Win32_Process -Filter ('ProcessId='+$clientPid)
 if($owned.Name -ne 'ChatGPT.exe' -or !$owned.CommandLine.Contains($testProfile)){throw 'Refusing to close non-test Codex'}
 node work/cdp-probe.cjs close-browser | Out-Null
 Start-Sleep -Milliseconds 500
 $owned=Get-CimInstance Win32_Process -Filter ('ProcessId='+$clientPid)
 if($owned -and $owned.CommandLine.Contains($testProfile)){taskkill /PID $clientPid /T /F | Out-Null}
}
try{
 Save-Theme;Start-Worker;Launch-Client
 $state=Wait-State 'shrine'
 $native=Get-Process -Id $worker.Id
 $activeMemory=@{WorkingSetMB=[Math]::Round($native.WorkingSet64/1MB,1);PrivateMB=[Math]::Round($native.PrivateMemorySize64/1MB,1)}
 if(@($native.Modules.ModuleName|Where-Object {$_ -match 'PresentationFramework|PresentationCore|wpfgfx'}).Count){throw 'Worker loaded WPF'}
 Write-Output ('PASS light worker: '+($activeMemory|ConvertTo-Json -Compress))
 node work/cdp-probe.cjs reload | Out-Null;$state=Wait-State 'shrine'
 Write-Output 'PASS reload preserves theme and GPU animation'
 Close-Client;Start-Sleep -Seconds 1
 if($worker.HasExited){throw 'Worker exited when Codex closed'}
 Launch-Client;$state=Wait-State 'shrine'
 Write-Output 'PASS full Codex exit and restoring launcher reopen'
 foreach($theme in @(@{Id='center-storm';Effect='storm'},@{Id='center-tide';Effect='tide'},@{Id='center-eclipse';Effect='eclipse'},@{Id='center-shrine';Effect='shrine'})){
  Save-Theme $theme.Id;Send-Command 'apply'|Out-Null;$state=Wait-State $theme.Effect
  if($state.canvasCount -ne 1 -or $state.livingArtCount -ne 1){throw 'Theme switch leaked renderers'}
 };Write-Output 'PASS four themes retain maximum particles and GPU effects'
 Save-Theme 'custom' (Join-Path (Get-Location) 'outputs\CodexDreamSkin-v4\四款强动效预览.mp4');Send-Command 'apply'|Out-Null
 for($attempt=0;$attempt -lt 60;$attempt++){Start-Sleep -Milliseconds 300;$state=Read-State;if($state.videoReady -ge 2 -and $state.videoTime -gt 1){break}}
 if($state.videoReady -lt 2 -or $state.videoTime -le 1){throw 'Chunked video playback failed'}
 Write-Output 'PASS chunked MP4 loads and plays'
 Save-Theme;Send-Command 'apply'|Out-Null;$state=Wait-State 'shrine'
 # 模拟关机：关闭客户端和守护；模拟下一次登录：仅启动守护。
 Close-Client;$worker.Kill();$worker.WaitForExit();Start-Worker;Start-Sleep -Seconds 1
 if(Client-Pid){throw 'Logon worker unexpectedly launched Codex'}
 if($worker.HasExited -or $worker.MainWindowHandle -ne 0){throw 'Headless startup failed'}
 Launch-Client;$state=Wait-State 'shrine'
 Write-Output 'PASS simulated logon starts only worker; next Codex launch restores theme'
 $native=Get-Process -Id $worker.Id
 $finalMemory=@{WorkingSetMB=[Math]::Round($native.WorkingSet64/1MB,1);PrivateMB=[Math]::Round($native.PrivateMemorySize64/1MB,1)}
 $particles=$state.particleCount
 Send-Command 'stop'|Out-Null
 for($attempt=0;$attempt -lt 20;$attempt++){Start-Sleep -Milliseconds 200;$state=Read-State;if(!$state.host -and !$state.style){break}}
 if($state.host -or $state.style){throw 'Explicit stop failed'}
 $worker.WaitForExit(5000)|Out-Null
 $startup=Start-Process -FilePath $workerPath -ArgumentList ('--background --startup --profile-dir="'+$testProfile+'"') -WindowStyle Hidden -PassThru
 if(!$startup.WaitForExit(5000)){throw 'Disabled startup stayed running'}
 Write-Output 'PASS stop restores interface and disabled logon leaves no worker'
 @{ActiveMemory=$activeMemory;FinalMemory=$finalMemory;ParticleCount=$particles;BootTest='Simulated logon, no actual Windows reboot';Result='PASS'}|ConvertTo-Json -Depth 4|Set-Content work/v5-test-report.json -Encoding UTF8
}
finally{
 try{Send-Command 'stop'|Out-Null}catch{}
 Close-Client
 if($worker -and !$worker.HasExited){$worker.Kill();$worker.WaitForExit()}
}
