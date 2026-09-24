[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Version = '0.1.0',
    [switch]$RequireSigning
)

$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$projectPath = Join-Path $repoRoot 'src\Dbms.App\Orbit.DataBridge.csproj'
$publishPath = Join-Path $artifactsRoot 'publish\win-x64'
$releasePath = Join-Path $artifactsRoot 'release'
$releaseNotesPath = Join-Path $repoRoot 'RELEASE_NOTES.md'
$version = $Version.TrimStart('v')

if ($version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "Version '$version' is not a valid SemVer 2 version for Velopack."
}

$dotnet = if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' } else { 'dotnet' }

function Remove-GeneratedDirectory([string]$path) {
    $fullPath = [IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the artifacts directory: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

Remove-GeneratedDirectory $publishPath
Remove-GeneratedDirectory $releasePath
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null

& $dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit code $LASTEXITCODE." }

& $dotnet publish $projectPath `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:Version=$version `
    -o $publishPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$signParams = $env:VPK_SIGN_PARAMS
if ($RequireSigning -and [string]::IsNullOrWhiteSpace($signParams)) {
    throw 'Production packaging requires VPK_SIGN_PARAMS. Configure a public code-signing certificate before creating a public release.'
}
if ($RequireSigning -and -not (Get-Command signtool.exe -ErrorAction SilentlyContinue)) {
    throw 'Production packaging requires signtool.exe on PATH. Install the Windows SDK or use the hosted release workflow.'
}

$vpkArgs = @(
    'tool', 'run', 'vpk', '--', 'pack',
    '--packId', 'Orbit.DataBridge',
    '--packVersion', $version,
    '--packDir', $publishPath,
    '--mainExe', 'Orbit.DataBridge.exe',
    '--packTitle', 'Orbit DataBridge',
    '--packAuthors', 'Orbit',
    '--runtime', 'win-x64',
    '--channel', 'stable',
    '--outputDir', $releasePath,
    '--shortcuts', 'Desktop,StartMenuRoot'
)

if (Test-Path -LiteralPath $releaseNotesPath) {
    $vpkArgs += @('--releaseNotes', $releaseNotesPath)
}

if (-not [string]::IsNullOrWhiteSpace($signParams)) {
    $vpkArgs += @('--signParams', $signParams)
}

& $dotnet @vpkArgs
if ($LASTEXITCODE -ne 0) { throw "Velopack packaging failed with exit code $LASTEXITCODE." }

$releaseAssets = Get-ChildItem -LiteralPath $releasePath -File | Sort-Object Name
if (-not ($releaseAssets.Name | Where-Object { $_ -like 'Orbit.DataBridge-*-Setup.exe' })) {
    throw 'Velopack did not create an Orbit.DataBridge setup executable.'
}
if (-not ($releaseAssets.Name -contains 'releases.stable.json')) {
    throw 'Velopack did not create releases.stable.json.'
}

$checksumLines = foreach ($asset in $releaseAssets) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $asset.FullName).Hash.ToLowerInvariant()
    "$hash *$($asset.Name)"
}
[IO.File]::WriteAllLines(
    (Join-Path $releasePath 'SHA256SUMS.txt'),
    $checksumLines,
    [Text.UTF8Encoding]::new($false))

Write-Host "Packaged Orbit DataBridge $version in $releasePath"
Get-ChildItem -LiteralPath $releasePath -File | Sort-Object Name | Select-Object Name, Length
