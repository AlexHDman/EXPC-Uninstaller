$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or !(Test-Path 'C:\EXPC-Test\ALLOW-SANDBOX-TESTS')) { throw 'Sandbox only' }
$winget = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'
$output = & $winget pin list --source winget --disable-interactivity 2>&1 | Out-String
@{ ExitCode = $LASTEXITCODE; Output = $output } | ConvertTo-Json | Set-Content 'C:\EXPC-Results\pins-winget-source.json' -Encoding UTF8
