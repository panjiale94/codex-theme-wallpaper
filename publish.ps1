param([string]$OutputDirectory = 'work\publish')
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    $destination = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutputDirectory))
    # 发布两个进程和原始主题文件；用户运行时无需安装 .NET。
    dotnet publish .\work\CodexDreamSkin.Worker\CodexDreamSkin.Worker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $destination
    if ($LASTEXITCODE -ne 0) { throw '轻量守护发布失败。' }
    dotnet publish .\work\CodexDreamSkin\CodexDreamSkin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $destination
    if ($LASTEXITCODE -ne 0) { throw '设置界面发布失败。' }
    Write-Output $destination
}
finally { Pop-Location }
