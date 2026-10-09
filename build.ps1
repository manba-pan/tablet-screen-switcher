$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework C# compiler not found. Windows 10/11 with .NET Framework 4.8 is required.' }
$buildArguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/codepage:65001',
    ('/out:' + (Join-Path $taskRoot 'TabletScreenSwitcher.exe')),
    ('/win32manifest:' + (Join-Path $taskRoot 'application.manifest')),
    ('/win32icon:' + (Join-Path $taskRoot 'assets\PenBridge.ico')),
    ('/resource:' + (Join-Path $taskRoot 'README.md') + ',PenBridge.Readme'),
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll',
    (Join-Path $taskRoot 'AssemblyInfo.cs'), (Join-Path $taskRoot 'Core.cs'), (Join-Path $taskRoot 'Demo.cs'), (Join-Path $taskRoot 'Theme.cs'), (Join-Path $taskRoot 'Ui.cs'), (Join-Path $taskRoot 'Program.cs'))
& $compilerPath $buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$demoBuildArguments = @('/define:DEMO', ('/out:' + (Join-Path $taskRoot 'PenBridge-Demo.exe'))) + @($buildArguments | Where-Object { -not $_.StartsWith('/out:') })
& $compilerPath $demoBuildArguments
if ($LASTEXITCODE -ne 0) { throw 'Demo build failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'PenBridge-Demo.exe') -Destination (Join-Path $taskRoot 'TabletScreenSwitcherDemo.exe') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'TabletScreenSwitcher.exe.config') -Destination (Join-Path $taskRoot 'TabletScreenSwitcherDemo.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'TabletScreenSwitcher.exe') -Destination (Join-Path $taskRoot 'PenBridge.exe') -Force
Write-Output (Join-Path $taskRoot 'TabletScreenSwitcher.exe')
