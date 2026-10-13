using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace CodexDreamSkin;

internal static class StartupIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexDreamSkin";
    private static string Worker => Path.Combine(AppContext.BaseDirectory, "CodexDreamSkin.Worker.exe");

    internal static async Task UpdateAsync(ThemeSettings settings, string? profileOverride)
    {
        if (!string.Equals(WorkerIdentity.ProfileDirectory(profileOverride), WorkerIdentity.ProfileDirectory(null), StringComparison.OrdinalIgnoreCase))
            return; // 独立验收资料目录不改变真实用户的登录和快捷方式。
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            string? old = run.GetValue(ValueName) as string;
            string? next = settings.Enabled && settings.AutoStart ? '"' + Worker + "\" --background --startup" : null;
            if (next is null) run.DeleteValue(ValueName, false);
            else if (old != next) run.SetValue(ValueName, next, RegistryValueKind.String);
        }
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexDreamSkin");
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, "shortcuts-v5.txt");
        if (settings.Enabled && File.Exists(marker) && await File.ReadAllTextAsync(marker) == Worker) return;
        string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        // 使用一次性的隐藏 PowerShell 创建快捷方式；守护进程无需常驻 COM/WPF。
        string script = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';trap{[Console]::Error.WriteLine($_.Exception.Message);exit 1};$worker=" + Quote(Worker) + ";$directory=" + Quote(directory) + @";
$shell=New-Object -ComObject WScript.Shell;
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
if(($link.TargetPath -match 'OpenAI\.Codex_' -and $link.TargetPath -match 'ChatGPT\.exe$') -or ($file.BaseName -eq 'Codex' -and $link.TargetPath -eq '')){
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
        var process = new ProcessStartInfo("powershell.exe") {UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true, RedirectStandardOutput = true};
        process.ArgumentList.Add("-NoProfile"); process.ArgumentList.Add("-EncodedCommand");
        process.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var child = Process.Start(process) ?? throw new IOException("无法配置自动恢复入口。");
        Task<string> errors = child.StandardError.ReadToEndAsync();
        Task<string> output = child.StandardOutput.ReadToEndAsync();
        await child.WaitForExitAsync(); await output;
        if (child.ExitCode != 0) throw new IOException("自动恢复入口配置失败：" + await errors);
        if (settings.Enabled) await File.WriteAllTextAsync(marker, Worker);
        else await File.WriteAllTextAsync(marker, "");
    }
}
