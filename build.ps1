# Builds AdapterToggle into .\app\AdapterToggle.exe (framework-dependent, needs .NET 8 Desktop Runtime).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Avoid the SDK's first-run experience, which can hang in non-interactive shells.
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'

$dotnet = 'dotnet'
if (-not (& dotnet --list-sdks 2>$null | Select-String '^8\.')) {
    $local = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet-sdk8\dotnet.exe'
    if (-not (Test-Path $local)) { throw '.NET 8 SDK not found.' }
    $dotnet = $local
}

& $dotnet publish .\AdapterToggle.csproj -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=None -o .\app
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
Write-Host "Built: $(Join-Path $PSScriptRoot 'app\AdapterToggle.exe')"
