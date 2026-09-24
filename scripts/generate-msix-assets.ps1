[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

try {
    Add-Type -AssemblyName System.Drawing
}
catch {
    throw 'System.Drawing is required to generate the MSIX icon assets on Windows.'
}

$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

function New-BrandIcon {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [int]$Size
    )

    $bitmap = $null
    $graphics = $null
    $orbitPen = $null
    $accentPen = $null
    $bridgePen = $null
    $nodeBrush = $null

    try {
        $bitmap = [System.Drawing.Bitmap]::new(
            $Size,
            $Size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $navy = [System.Drawing.Color]::FromArgb(20, 33, 61)
        $indigo = [System.Drawing.Color]::FromArgb(91, 92, 226)
        $mint = [System.Drawing.Color]::FromArgb(45, 190, 155)
        $white = [System.Drawing.Color]::FromArgb(247, 250, 252)
        $graphics.Clear($navy)

        $scale = [single]($Size / 150.0)
        $orbitBounds = [System.Drawing.RectangleF]::new(
            [single](28 * $scale),
            [single](35 * $scale),
            [single](94 * $scale),
            [single](80 * $scale))

        $orbitPen = [System.Drawing.Pen]::new($indigo, [single](11 * $scale))
        $orbitPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $graphics.DrawEllipse($orbitPen, $orbitBounds)

        $accentPen = [System.Drawing.Pen]::new($mint, [single](6 * $scale))
        $accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawArc($accentPen, $orbitBounds, 198, 112)

        $bridgePen = [System.Drawing.Pen]::new($white, [single](16 * $scale))
        $bridgePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $bridgePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine(
            $bridgePen,
            [single](42 * $scale),
            [single](75 * $scale),
            [single](108 * $scale),
            [single](75 * $scale))

        $nodeBrush = [System.Drawing.SolidBrush]::new($mint)
        $nodeRadius = [single](7 * $scale)
        $nodeDiameter = [single](2 * $nodeRadius)
        foreach ($nodeX in @(42, 108)) {
            $graphics.FillEllipse(
                $nodeBrush,
                [single](($nodeX * $scale) - $nodeRadius),
                [single]((75 * $scale) - $nodeRadius),
                $nodeDiameter,
                $nodeDiameter)
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        if ($nodeBrush) { $nodeBrush.Dispose() }
        if ($bridgePen) { $bridgePen.Dispose() }
        if ($accentPen) { $accentPen.Dispose() }
        if ($orbitPen) { $orbitPen.Dispose() }
        if ($graphics) { $graphics.Dispose() }
        if ($bitmap) { $bitmap.Dispose() }
    }
}

$assets = [ordered]@{
    'StoreLogo.png' = 50
    'Square44x44Logo.png' = 44
    'Square150x150Logo.png' = 150
    'Square310x310Logo.png' = 310
}

foreach ($asset in $assets.GetEnumerator()) {
    New-BrandIcon -Path (Join-Path $outputDirectory $asset.Key) -Size $asset.Value
}

Write-Host "Generated Orbit DataBridge MSIX assets in $outputDirectory"
