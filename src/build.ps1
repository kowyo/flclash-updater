param(
    [ValidateSet('x64', 'ARM64')][string]$Architecture = 'x64',
    [string]$DotNet = 'dotnet',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "..\$Architecture"
}
$runtime = if ($Architecture -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
& $DotNet publish (Join-Path $PSScriptRoot 'FlClashUpdater.csproj') -c Release -r $runtime "-p:Platform=$Architecture" --self-contained true -o $OutputDirectory -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'WinUI 3 build failed.' }
Write-Host "Built $OutputDirectory\FlClashUpdater-WinUI3.exe"
