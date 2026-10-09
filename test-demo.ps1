$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) { throw '.NET Framework C# compiler not found.' }
$demoTestDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('TabletSwitchDemoChecks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $demoTestDirectory | Out-Null
$demoTestExecutable = Join-Path $demoTestDirectory 'TabletSwitchDemoChecks.exe'
$demoTestExitCode = 1
try {
    $demoTestArguments = @('/nologo', '/target:exe', '/optimize+', '/platform:anycpu', '/utf8output', '/codepage:65001',
        '/main:TabletSwitch.DemoChecks', ('/out:' + $demoTestExecutable),
        '/r:System.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll',
        (Join-Path $PSScriptRoot 'Core.cs'), (Join-Path $PSScriptRoot 'Demo.cs'),
        (Join-Path $PSScriptRoot 'Theme.cs'), (Join-Path $PSScriptRoot 'Ui.cs'), (Join-Path $PSScriptRoot 'DemoChecks.cs'))
    & $compilerPath @demoTestArguments
    if ($LASTEXITCODE -ne 0) { throw 'Demo check compilation failed.' }
    & $demoTestExecutable (Join-Path $demoTestDirectory 'settings')
    $demoTestExitCode = $LASTEXITCODE
}
finally {
    $resolvedDemoTestDirectory = [System.IO.Path]::GetFullPath($demoTestDirectory)
    $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedDemoTestDirectory.StartsWith($resolvedTemporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([System.IO.Path]::GetFileName($resolvedDemoTestDirectory)).StartsWith('TabletSwitchDemoChecks-', [StringComparison]::Ordinal)) {
        throw 'Refusing to clean a directory outside the dedicated temporary test location.'
    }
    Remove-Item -LiteralPath $resolvedDemoTestDirectory -Recurse -Force
}
exit $demoTestExitCode
