$ErrorActionPreference = 'Stop'
$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('PenBridgeReleaseCheck-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory | Out-Null
try {
    $mainExecutable = Join-Path $testDirectory 'App.exe'
    $demoExecutable = Join-Path $testDirectory 'Renamed.exe'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PenBridge.exe') -Destination $mainExecutable
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PenBridge-Demo.exe') -Destination $demoExecutable

    $assembly = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($mainExecutable))
    $helpStream = $assembly.GetManifestResourceStream('PenBridge.Readme')
    if ($null -eq $helpStream) { throw 'Standalone EXE has no embedded help.' }
    try { if ($helpStream.Length -lt 100) { throw 'Embedded help is empty.' } } finally { $helpStream.Dispose() }
    if ([System.Diagnostics.FileVersionInfo]::GetVersionInfo($mainExecutable).ProductName -ne 'PenBridge') { throw 'Missing release product metadata.' }

    $mainResult = Join-Path $testDirectory 'main.json'
    $mainProcess = Start-Process -FilePath $mainExecutable -ArgumentList ('--smoke --out "' + $mainResult + '"') -WindowStyle Hidden -PassThru -Wait
    if ($mainProcess.ExitCode -ne 0 -or -not (Get-Content -LiteralPath $mainResult -Raw -Encoding UTF8 | ConvertFrom-Json).Success) { throw 'Standalone EXE without sidecars failed.' }

    $listResult = Join-Path $testDirectory 'demo-list.json'
    $listProcess = Start-Process -FilePath $demoExecutable -ArgumentList ('--list --out "' + $listResult + '"') -WindowStyle Hidden -PassThru -Wait
    $screens = Get-Content -LiteralPath $listResult -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($listProcess.ExitCode -ne 0 -or $screens.Count -ne 3 -or ($screens.Id -join ',') -ne 'demo-left,demo-main,demo-portrait') { throw 'Renamed demo escaped the simulated catalog.' }

    $guardResult = Join-Path $testDirectory 'demo-guard.json'
    $guardProcess = Start-Process -FilePath $demoExecutable -ArgumentList ('--smoke --next --out "' + $guardResult + '"') -WindowStyle Hidden -PassThru -Wait
    $guard = Get-Content -LiteralPath $guardResult -Raw -Encoding UTF8 | ConvertFrom-Json
    $demoPrefix = -join @([char]0x6f14, [char]0x793a, [char]0x6a21, [char]0x5f0f)
    if ($guardProcess.ExitCode -ne 1 -or $guard.Success -or -not $guard.Message.StartsWith($demoPrefix)) {
        throw 'Demo accepted a real-operation command or failed with an unexpected error.'
    }

    $demoResult = Join-Path $testDirectory 'demo.json'
    $demoProcess = Start-Process -FilePath $demoExecutable -ArgumentList ('--smoke --out "' + $demoResult + '"') -WindowStyle Hidden -PassThru -Wait
    if ($demoProcess.ExitCode -ne 0 -or -not (Get-Content -LiteralPath $demoResult -Raw -Encoding UTF8 | ConvertFrom-Json).Success) { throw 'Renamed standalone demo failed.' }
    Write-Output 'PASS: embedded help, release metadata, standalone EXE, renamed demo catalog, real-command rejection, and renamed demo UI.'
}
finally {
    $resolvedDirectory = [System.IO.Path]::GetFullPath($testDirectory)
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedDirectory.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -or
        -not [System.IO.Path]::GetFileName($resolvedDirectory).StartsWith('PenBridgeReleaseCheck-', [StringComparison]::Ordinal)) { throw 'Unexpected test directory.' }
    Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
}
