param(
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts\win-x64'
)

$ErrorActionPreference = 'Stop'
$dotnet = if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' } else { 'dotnet' }

& $dotnet publish '.\src\Dbms.App\Orbit.DataBridge.csproj' `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $Output

Write-Host "Published Orbit.DataBridge.exe to $Output"
