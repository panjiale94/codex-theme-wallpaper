using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace CodexDreamSkin;

internal static class StartupIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexDreamSkin";
    private static string? configured;

    internal static async Task UpdateAsync(ThemeSettings settings, string? profileOverride)
    {
        if (!string.Equals(WorkerIdentity.ProfileDirectory(profileOverride), WorkerIdentity.ProfileDirectory(null), StringComparison.OrdinalIgnoreCase))
            return; // 独立验收资料目录不改变真实用户的登录和快捷方式。
        new ThemeSettingsStore(profileOverride).Save(settings, true);
        using var previousInstallation = Registry.CurrentUser.OpenSubKey(@"Software\CodexDreamSkin");
        string worker = settings.Enabled ? await RuntimeInstallation.EnsureAsync() :
            (previousInstallation?.GetValue("InstalledWorker") as string ?? Path.Combine(AppContext.BaseDirectory, "CodexDreamSkin.Worker.exe"));
        string fingerprint = $"{worker}|{settings.Enabled}|{settings.AutoStart}";
        if (configured == fingerprint) return;
        using (var installation = Registry.CurrentUser.CreateSubKey(@"Software\CodexDreamSkin"))
            installation.SetValue("InstalledWorker", worker);
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            // 迁移旧 Run 项，登录启动改由计划任务和启动文件夹负责。
            run.DeleteValue(ValueName, false);
        }
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexDreamSkin");
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, "shortcuts-v7.txt");
        bool updateShortcuts = !settings.Enabled || !File.Exists(marker) || await File.ReadAllTextAsync(marker) != worker;
        string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        // 使用一次性的隐藏 PowerShell 创建快捷方式；守护进程无需常驻 COM/WPF。
        string script = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';trap{[Console]::Error.WriteLine($_.Exception.Message);exit 1};$worker=" + Quote(worker) + ";$directory=" + Quote(directory) + @";
$shell=New-Object -ComObject WScript.Shell;
$startupLink=Join-Path ([Environment]::GetFolderPath('Startup')) 'CodexDreamSkin AutoRestore.lnk';
$taskName='CodexDreamSkin.AutoRestore';
";
        if (settings.Enabled && settings.AutoStart)
            script += @"
# 当前用户交互会话运行，无管理员权限、无密码、无桌面窗口。
$link=$shell.CreateShortcut($startupLink);$link.TargetPath=$worker;$link.Arguments='--background --startup';$link.WorkingDirectory=Split-Path -Parent $worker;$link.Save();
try{
 $svc=New-Object -ComObject Schedule.Service;$svc.Connect();$folder=$svc.GetFolder('\');$definition=$svc.NewTask(0);
 $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;
 $definition.RegistrationInfo.Description='Codex 动态主题：登录恢复、每分钟检查、异常重试；仅当前用户。';
 $definition.Principal.UserId=$sid;$definition.Principal.LogonType=3;$definition.Principal.RunLevel=0;
 $definition.Settings.Enabled=$true;$definition.Settings.Hidden=$true;$definition.Settings.StartWhenAvailable=$true;
 $definition.Settings.DisallowStartIfOnBatteries=$false;$definition.Settings.StopIfGoingOnBatteries=$false;
 $definition.Settings.ExecutionTimeLimit='PT0S';$definition.Settings.MultipleInstances=2;
 $definition.Settings.RestartInterval='PT1M';$definition.Settings.RestartCount=3;
 $login=$definition.Triggers.Create(9);$login.UserId=$sid;$login.Enabled=$true;
 $watchdog=$definition.Triggers.Create(1);$watchdog.StartBoundary=(Get-Date).AddMinutes(1).ToString('yyyy-MM-ddTHH:mm:ss');$watchdog.Enabled=$true;$watchdog.Repetition.Interval='PT1M';
 $unlock=$definition.Triggers.Create(11);$unlock.UserId=$sid;$unlock.StateChange=8;$unlock.Enabled=$true;
 $action=$definition.Actions.Create(0);$action.Path=$worker;$action.Arguments='--background --startup';$action.WorkingDirectory=Split-Path -Parent $worker;
 $folder.RegisterTaskDefinition($taskName,$definition,6,$null,$null,3,$null)|Out-Null;
 [Console]::Out.WriteLine('计划任务已登记：登录、解锁、每分钟守护检查。');
}catch{[Console]::Out.WriteLine('计划任务不可用，已保留启动文件夹入口：'+$_.Exception.Message)};
";
        else
            script += @"
if(Test-Path -LiteralPath $startupLink){Remove-Item -LiteralPath $startupLink};
try{$svc=New-Object -ComObject Schedule.Service;$svc.Connect();$folder=$svc.GetFolder('\');$existing=$null;try{$existing=$folder.GetTask($taskName)}catch{};if($existing){$folder.DeleteTask($taskName,0)}}catch{throw ('无法撤销自动恢复计划任务：'+$_.Exception.Message)};
";
        if (updateShortcuts)
        {
            script += @"
$manifest=Join-Path $directory 'shortcut-backups.json';
$entries=@();function Read-BackupEntries($value){if($value -is [Array]){foreach($item in $value){Read-BackupEntries $item}}elseif($value -and $value.Path -is [string] -and $value.Backup -is [string]){$script:entries+=$value}};if(Test-Path -LiteralPath $manifest){$decoded=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json;Read-BackupEntries $decoded};
$owned=@((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Codex（动态皮肤）.lnk'),(Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex（动态皮肤）.lnk'));
";
            if (settings.Enabled)
            {
                script += @"
$roots=@([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('Programs'),(Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'));
foreach($root in $roots){if(!(Test-Path -LiteralPath $root)){continue};foreach($file in Get-ChildItem -LiteralPath $root -Filter '*.lnk' -Recurse -ErrorAction SilentlyContinue){
if($owned -contains $file.FullName){continue};$link=$shell.CreateShortcut($file.FullName);
if(($link.TargetPath -match 'OpenAI\.Codex_' -and $link.TargetPath -match 'ChatGPT\.exe$') -or ($file.BaseName -eq 'Codex' -and $link.TargetPath -eq '') -or (($entries | Where-Object Path -eq $file.FullName) -and $link.TargetPath -match 'CodexDreamSkin(\.Worker)?\.exe$')){
if(!($entries | Where-Object Path -eq $file.FullName)){$backup=Join-Path $directory ([Guid]::NewGuid().ToString('N')+'.lnk');[IO.File]::WriteAllBytes($backup,[IO.File]::ReadAllBytes($file.FullName));$entries+= [pscustomobject]@{Path=$file.FullName;Backup=$backup}};
$link.TargetPath=$worker;$link.Arguments='--launch';$link.WorkingDirectory=Split-Path -Parent $worker;$link.Save()}}
};
foreach($path in $owned){$link=$shell.CreateShortcut($path);$link.TargetPath=$worker;$link.Arguments='--launch';$link.WorkingDirectory=Split-Path -Parent $worker;$link.Description='自动恢复 Codex 动态皮肤';$link.Save()};
ConvertTo-Json -InputObject @($entries) | Set-Content -LiteralPath $manifest -Encoding UTF8;
";
            }
            else
            {
                script += @"
foreach($entry in $entries){if(Test-Path -LiteralPath $entry.Backup){$restore=!(Test-Path -LiteralPath $entry.Path);if(!$restore){$link=$shell.CreateShortcut($entry.Path);$restore=$link.TargetPath -eq $worker};if($restore){[IO.File]::WriteAllBytes($entry.Path,[IO.File]::ReadAllBytes($entry.Backup))}}};
foreach($path in $owned){if(Test-Path -LiteralPath $path){$link=$shell.CreateShortcut($path);if($link.TargetPath -eq $worker){Remove-Item -LiteralPath $path}}};
";
            }
        }
        var process = new ProcessStartInfo("powershell.exe") {UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true, RedirectStandardOutput = true};
        process.ArgumentList.Add("-NoProfile"); process.ArgumentList.Add("-EncodedCommand");
        process.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var child = Process.Start(process) ?? throw new IOException("无法配置自动恢复入口。");
        Task<string> errors = child.StandardError.ReadToEndAsync();
        Task<string> output = child.StandardOutput.ReadToEndAsync();
        await child.WaitForExitAsync(); string details = await output;
        if (child.ExitCode != 0) throw new IOException("自动恢复入口配置失败：" + await errors);
        if (!string.IsNullOrWhiteSpace(details)) SkinLog.Write(details.Trim());
        if (settings.Enabled) await File.WriteAllTextAsync(marker, worker);
        else await File.WriteAllTextAsync(marker, "");
        configured = fingerprint;
    }
}
