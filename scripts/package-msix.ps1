[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Version = '0.1.0',

    [string]$PackageIdentityName = $env:ORBIT_STORE_IDENTITY_NAME,

    [string]$Publisher = $env:ORBIT_STORE_PUBLISHER,

    [string]$PublisherDisplayName = $env:ORBIT_STORE_PUBLISHER_DISPLAY_NAME,

    [string]$DisplayName = 'Orbit DataBridge'
)

$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$msixRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'msix'))
$publishPath = Join-Path $msixRoot 'publish\win-x64'
$packageRoot = Join-Path $msixRoot 'package'
$symbolsRoot = Join-Path $msixRoot 'symbols'
$uploadStaging = Join-Path $msixRoot 'upload-staging'
$projectPath = Join-Path $repoRoot 'src\Dbms.App\Orbit.DataBridge.csproj'
$manifestTemplatePath = Join-Path $repoRoot 'packaging\msix\AppxManifest.xml'
$assetScriptPath = Join-Path $repoRoot 'scripts\generate-msix-assets.ps1'

if ([string]::IsNullOrWhiteSpace($PackageIdentityName) -or
    [string]::IsNullOrWhiteSpace($Publisher) -or
    [string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
    throw @'
Microsoft Store identity values are required. In Partner Center, open Product management > Product identity and pass the exact Name, Publisher, and PublisherDisplayName values to this script.
'@
}

function ConvertTo-MsixVersion {
    param([Parameter(Mandatory)][string]$InputVersion)

    $cleanVersion = $InputVersion.Trim()
    if ($cleanVersion.StartsWith('v', [StringComparison]::OrdinalIgnoreCase)) {
        $cleanVersion = $cleanVersion.Substring(1)
    }

    $coreVersion = ($cleanVersion -split '[+-]', 2)[0]
    $parts = @($coreVersion.Split('.'))
    if ($parts.Count -lt 3 -or $parts.Count -gt 4) {
        throw "Version '$InputVersion' must contain three or four numeric components, such as 0.1.0."
    }

    $numbers = @(
        foreach ($part in $parts) {
            if ($part -notmatch '^\d+$') {
                throw "Version '$InputVersion' contains a non-numeric MSIX version component."
            }

            $number = 0L
            if (-not [long]::TryParse(
                    $part,
                    [Globalization.NumberStyles]::None,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [ref]$number) -or $number -gt 65535) {
                throw "Version '$InputVersion' contains a component outside the MSIX range 0-65535."
            }

            [int]$number
        }
    )

    while ($numbers.Count -lt 4) {
        $numbers += 0
    }

    $numbers -join '.'
}

function Remove-GeneratedDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $msixRootWithSeparator = $msixRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($fullPath -notlike "$msixRootWithSeparator*") {
        throw "Refusing to remove a path outside the MSIX artifacts directory: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

function Remove-GeneratedFile {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $msixRootWithSeparator = $msixRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($fullPath -notlike "$msixRootWithSeparator*") {
        throw "Refusing to remove a path outside the MSIX artifacts directory: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Force
    }
}

$msixVersion = ConvertTo-MsixVersion -InputVersion $Version
$msixPath = Join-Path $msixRoot "Orbit.DataBridge_${msixVersion}_x64.msix"
$appxsymPath = Join-Path $msixRoot "Orbit.DataBridge_${msixVersion}.appxsym"
$msixUploadPath = Join-Path $msixRoot "Orbit.DataBridge_${msixVersion}_x64.msixupload"
$checksumsPath = Join-Path $msixRoot 'SHA256SUMS.txt'

$dotnet = $null
if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
    $dotnetCandidate = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
    if (Test-Path -LiteralPath $dotnetCandidate) {
        $dotnet = $dotnetCandidate
    }
}
if (-not $dotnet) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCommand) {
        $dotnet = $dotnetCommand.Source
    }
}
if (-not $dotnet) {
    throw 'dotnet.exe was not found. Install the .NET SDK or run this packaging script in the Store workflow.'
}

$programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
$windowsKitRoot = if ($programFilesX86) { Join-Path $programFilesX86 'Windows Kits\10\bin' } else { $null }
$makeAppxCandidates = @()
if ($windowsKitRoot -and (Test-Path -LiteralPath $windowsKitRoot)) {
    $makeAppxCandidates = @(
        Get-ChildItem -LiteralPath $windowsKitRoot -Filter 'makeappx.exe' -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
            Sort-Object FullName -Descending
    )
}
if ($makeAppxCandidates.Count -eq 0) {
    throw 'makeappx.exe was not found. Install the Windows SDK or run this packaging script in the GitHub-hosted Store workflow.'
}
$makeAppx = $makeAppxCandidates[0].FullName

New-Item -ItemType Directory -Path $msixRoot -Force | Out-Null
Remove-GeneratedDirectory $publishPath
Remove-GeneratedDirectory $packageRoot
Remove-GeneratedDirectory $symbolsRoot
Remove-GeneratedDirectory $uploadStaging
Remove-GeneratedFile $msixPath
Remove-GeneratedFile $appxsymPath
Remove-GeneratedFile $msixUploadPath
Remove-GeneratedFile $checksumsPath
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

& $dotnet publish $projectPath `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:Version=$msixVersion `
    -o $publishPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -Path (Join-Path $publishPath '*') -Destination $packageRoot -Recurse -Force
& $assetScriptPath -OutputDirectory (Join-Path $packageRoot 'Assets')

$manifestText = Get-Content -LiteralPath $manifestTemplatePath -Raw -Encoding utf8
$tokenValues = @{
    '__PACKAGE_IDENTITY_NAME__' = [System.Security.SecurityElement]::Escape($PackageIdentityName)
    '__PUBLISHER__' = [System.Security.SecurityElement]::Escape($Publisher)
    '__PUBLISHER_DISPLAY_NAME__' = [System.Security.SecurityElement]::Escape($PublisherDisplayName)
    '__DISPLAY_NAME__' = [System.Security.SecurityElement]::Escape($DisplayName)
    '__VERSION__' = $msixVersion
}
foreach ($token in $tokenValues.Keys) {
    $manifestText = $manifestText.Replace($token, $tokenValues[$token])
}
if ($manifestText -match '__[A-Z0-9_]+__') {
    throw 'The generated AppxManifest.xml still contains an unreplaced package token.'
}

$manifestPath = Join-Path $packageRoot 'AppxManifest.xml'
[IO.File]::WriteAllText($manifestPath, $manifestText, [Text.UTF8Encoding]::new($false))
try {
    [xml](Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8) | Out-Null
}
catch {
    throw "The generated AppxManifest.xml is not valid XML: $($_.Exception.Message)"
}

$symbolFiles = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.pdb' -File -Recurse)
if ($symbolFiles.Count -gt 0) {
    New-Item -ItemType Directory -Path $symbolsRoot -Force | Out-Null
    foreach ($symbolFile in $symbolFiles) {
        $relativePath = [IO.Path]::GetRelativePath($packageRoot, $symbolFile.FullName)
        $symbolDestination = Join-Path $symbolsRoot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $symbolDestination) -Force | Out-Null
        Copy-Item -LiteralPath $symbolFile.FullName -Destination $symbolDestination -Force
        Remove-Item -LiteralPath $symbolFile.FullName -Force
    }

    $appxsymZipPath = "${appxsymPath}.zip"
    Compress-Archive -Path (Join-Path $symbolsRoot '*') -DestinationPath $appxsymZipPath -Force
    Move-Item -LiteralPath $appxsymZipPath -Destination $appxsymPath -Force
    Remove-GeneratedDirectory $symbolsRoot
}
else {
    Write-Warning 'No PDB files were produced; the .msixupload will not contain an .appxsym symbol archive.'
}

& $makeAppx pack /o /h SHA256 /d $packageRoot /p $msixPath
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $msixPath)) {
    throw 'MakeAppx completed without creating the MSIX package.'
}

New-Item -ItemType Directory -Path $uploadStaging -Force | Out-Null
Copy-Item -LiteralPath $msixPath -Destination (Join-Path $uploadStaging (Split-Path -Leaf $msixPath)) -Force
if (Test-Path -LiteralPath $appxsymPath) {
    Copy-Item -LiteralPath $appxsymPath -Destination (Join-Path $uploadStaging (Split-Path -Leaf $appxsymPath)) -Force
}

$msixUploadZipPath = "${msixUploadPath}.zip"
Compress-Archive -Path (Join-Path $uploadStaging '*') -DestinationPath $msixUploadZipPath -Force
Move-Item -LiteralPath $msixUploadZipPath -Destination $msixUploadPath -Force
Remove-GeneratedDirectory $uploadStaging

$checksumLines = @(
    foreach ($assetPath in @($msixPath, $msixUploadPath)) {
        $asset = Get-Item -LiteralPath $assetPath
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $asset.FullName).Hash.ToLowerInvariant()
        "$hash *$($asset.Name)"
    }
)
[IO.File]::WriteAllLines($checksumsPath, $checksumLines, [Text.UTF8Encoding]::new($false))

Write-Host "Packaged Orbit DataBridge $msixVersion for Microsoft Store upload."
Write-Host "MSIX:       $msixPath"
Write-Host "MSIXUPLOAD: $msixUploadPath"
Write-Host 'No package signing step was performed; Microsoft Store signs the submitted MSIX after certification.'
