$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw 'The built-in .NET Framework 4.x C# compiler was not found.'
}
$testRunDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('TabletSwitchCompile-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRunDirectory | Out-Null
$testExecutable = Join-Path $testRunDirectory 'TabletSwitchTests.exe'
$testExitCode = 1
try {
    $compilerArguments = @('/nologo', '/target:exe', '/optimize+', '/platform:anycpu', '/utf8output', '/codepage:65001', ('/out:' + $testExecutable), '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll', (Join-Path $PSScriptRoot 'Core.cs'), (Join-Path $PSScriptRoot 'Tests.cs'))
    & $compilerPath @compilerArguments
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExecutable
    $testExitCode = $LASTEXITCODE
}
finally {
    $resolvedTestRunDirectory = [System.IO.Path]::GetFullPath($testRunDirectory)
    $resolvedTempDirectory = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedTestRunDirectory.StartsWith($resolvedTempDirectory, [StringComparison]::OrdinalIgnoreCase) -or -not ([System.IO.Path]::GetFileName($resolvedTestRunDirectory)).StartsWith('TabletSwitchCompile-', [StringComparison]::Ordinal)) {
        throw 'Refusing to clean a test directory outside the expected temporary location.'
    }
    Remove-Item -LiteralPath $resolvedTestRunDirectory -Recurse -Force
}
exit $testExitCode
