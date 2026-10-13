param([string]$ReleaseDirectory='outputs\CodexDreamSkin-v6')
$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$testProfile=Join-Path $PSScriptRoot 'v6-test-profile'
$sha=[Security.Cryptography.SHA256]::Create()
try{$key=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($testProfile.ToUpperInvariant())))).Replace('-','').Substring(0,16)}finally{$sha.Dispose()}
$settingsRegistry='HKCU:\Software\CodexDreamSkin\Profiles\'+$key
$pipeName='codex-dream-skin-'+$key
function Send-UpgradeCommand([string]$command){
 $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$pipeName,[IO.Pipes.PipeDirection]::InOut)
 try{
  $pipe.Connect(2000)
  $writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),4096,$true);$writer.AutoFlush=$true
  $reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8,$false,4096,$true)
  $writer.WriteLine((@{Command=$command;AllowLaunch=$false}|ConvertTo-Json -Compress))
  return ($reader.ReadLine()|ConvertFrom-Json)
 }finally{$pipe.Dispose()}
}
$settings=@{Enabled=$true;AutoStart=$true;PresetId='center-shrine';Panel=35;Motion=100;CustomMediaPath=$null}
Set-ItemProperty -LiteralPath $settingsRegistry -Name Settings -Value ($settings|ConvertTo-Json -Compress)
$legacy=Start-Process -FilePath (Join-Path (Get-Location) 'outputs\CodexDreamSkin-v5\CodexDreamSkin.Worker.exe') -ArgumentList ('--background --profile-dir="'+$testProfile+'"') -WindowStyle Hidden -PassThru
try{
 for($attempt=0;$attempt -lt 20;$attempt++){try{$version=Send-UpgradeCommand 'version';break}catch{Start-Sleep -Milliseconds 100}}
 if($version.Version -ne 5){throw 'Expected v5 test guardian'}
 $manager=Start-Process -FilePath (Join-Path (Get-Location) ($ReleaseDirectory+'\CodexDreamSkin.exe')) -ArgumentList ('--profile-dir="'+$testProfile+'"') -WindowStyle Hidden -PassThru
 for($attempt=0;$attempt -lt 40;$attempt++){Start-Sleep -Milliseconds 50;$manager.Refresh();if($manager.MainWindowHandle -ne 0){break}}
 # 在启动交接尚未完成时关闭窗口，检验隐藏后的交接能够继续完成。
 if(!$manager.CloseMainWindow()){throw 'Could not close upgrade window'}
 if(!$manager.WaitForExit(15000)){throw 'Hidden upgrade UI did not finish'}
 $version=Send-UpgradeCommand 'version'
 $saved=Get-ItemPropertyValue -LiteralPath $settingsRegistry -Name Settings | ConvertFrom-Json
 if($version.Version -ne 6 -or !$saved.Enabled){throw 'Upgrade lost enabled state'}
 if(!$legacy.HasExited){throw 'Legacy worker stayed alive'}
 Write-Output 'PASS closing settings during v5-to-v6 handoff preserves enabled state and v6 guardian'
}finally{
 try{Send-UpgradeCommand 'stop'|Out-Null}catch{}
 if(!$legacy.HasExited){$legacy.Kill();$legacy.WaitForExit()}
}
