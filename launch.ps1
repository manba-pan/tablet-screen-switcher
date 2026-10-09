param(
    [switch]$Demo,
    [switch]$List,
    [switch]$Check,
    [switch]$Next,
    [int]$Screen = 0,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$executablePath = Join-Path $PSScriptRoot $(if ($Demo) { 'PenBridge-Demo.exe' } else { 'PenBridge.exe' })
if (-not (Test-Path -LiteralPath $executablePath)) { & (Join-Path $PSScriptRoot 'build.ps1') | Out-Null }
$actionCount = [int]$List.IsPresent + [int]$Check.IsPresent + [int]$Next.IsPresent + [int]($Screen -ne 0)
if ($actionCount -gt 1) { throw 'Choose one of -List, -Check, -Next or -Screen.' }
if ($Demo -and $actionCount -ne 0) { throw 'Demo opens an interactive simulation; do not combine it with live commands.' }
if ($actionCount -eq 0) {
    Start-Process -FilePath $executablePath -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
    exit 0
}
$resultPath = if ($OutputPath) { [System.IO.Path]::GetFullPath($OutputPath) } else { Join-Path ([System.IO.Path]::GetTempPath()) ('tablet-switch-' + [Guid]::NewGuid().ToString('N') + '.json') }
if ($List) { $actionArguments = @('--list') }
elseif ($Check) { $actionArguments = @('--check') }
elseif ($Next) { $actionArguments = @('--next') }
else { $actionArguments = @('--screen', $Screen.ToString()) }
# File paths cannot contain a quote in Windows; explicit quoting is safe for this path.
$processArguments = ($actionArguments -join ' ') + ' --out "' + $resultPath + '"'
$process = Start-Process -FilePath $executablePath -ArgumentList $processArguments -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -Wait -PassThru
try {
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 }
    else { throw 'No result was written by the application.' }
}
finally { if (-not $OutputPath -and (Test-Path -LiteralPath $resultPath)) { Remove-Item -LiteralPath $resultPath } }
exit $process.ExitCode
