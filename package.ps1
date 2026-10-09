param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') | Out-Null }
$releaseDirectory = Join-Path $PSScriptRoot 'releases'
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$stageDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('PenBridgePackage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stageDirectory | Out-Null
try {
    foreach ($name in @('PenBridge.exe', 'PenBridge-Demo.exe', 'README.md', 'RELEASE_NOTES.md', 'launch.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stageDirectory $name)
    }
    foreach ($entry in @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cmd' -File)) {
        Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $stageDirectory $entry.Name)
    }
    $docsStage = Join-Path $stageDirectory 'docs'
    New-Item -ItemType Directory -Path $docsStage | Out-Null
    foreach ($name in @('demo.gif', 'demo-preview.png')) {
        $sourcePath = Join-Path $PSScriptRoot ('docs\' + $name)
        if (Test-Path -LiteralPath $sourcePath) { Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $docsStage $name) }
    }
    $binaryHashes = foreach ($name in @('PenBridge.exe', 'PenBridge-Demo.exe')) {
        $binarySource = Join-Path $stageDirectory $name
        Copy-Item -LiteralPath $binarySource -Destination (Join-Path $releaseDirectory $name) -Force
        ((Get-FileHash -LiteralPath $binarySource -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name)
    }
    [System.IO.File]::WriteAllLines((Join-Path $stageDirectory 'SHA256SUMS.txt'), $binaryHashes, [System.Text.UTF8Encoding]::new($false))
    $archivePath = Join-Path $releaseDirectory 'PenBridge-0.3.0-Windows.zip'
    Compress-Archive -Path (Join-Path $stageDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal -Force
    $allHashes = @($binaryHashes) + @(((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($archivePath)))
    [System.IO.File]::WriteAllLines((Join-Path $releaseDirectory 'SHA256SUMS.txt'), $allHashes, [System.Text.UTF8Encoding]::new($false))
    Write-Output $archivePath
    Write-Output ($allHashes -join [Environment]::NewLine)
}
finally {
    $resolvedStage = [System.IO.Path]::GetFullPath($stageDirectory)
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedStage.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -or
        -not [System.IO.Path]::GetFileName($resolvedStage).StartsWith('PenBridgePackage-', [StringComparison]::Ordinal)) { throw 'Refusing to clean an unexpected staging path.' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
