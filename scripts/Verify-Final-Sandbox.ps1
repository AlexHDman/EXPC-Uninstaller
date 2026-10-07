param([switch]$PortableOnly)
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or !(Test-Path 'C:\EXPC-Test\ALLOW-SANDBOX-TESTS')) { throw 'Sandbox only' }
$run=if($PortableOnly){'C:\EXPC-Final-v2'}else{'C:\EXPC-Final'}
$prefix=if($PortableOnly){'final-v2'}else{'final'}
$inputRoot=if($PortableOnly){'C:\EXPC-FinalInput-v2'}else{'C:\EXPC-FinalInput'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory("$inputRoot\EXPC-Uninstaller-updates-vault-net10-win-x64.zip",$run)
$app=Start-Process "$run\EXPC-Uninstaller.exe" -PassThru
Start-Sleep -Seconds 5
@{ SystemDotnetPresent=(Test-Path "$env:ProgramFiles\dotnet\dotnet.exe"); Alive=(!$app.HasExited); DllSha256=(Get-FileHash "$run\EXPC-Uninstaller.dll").Hash } | ConvertTo-Json | Set-Content "C:\EXPC-Results\$prefix-portable.json"
Copy-Item "$run\startup-metrics.jsonl" "C:\EXPC-Results\$prefix-startup.jsonl"
if (!$app.HasExited -and $app.Path -eq "$run\EXPC-Uninstaller.exe") { Stop-Process -Id $app.Id }
if($PortableOnly){exit 0}
# Test harness is kept outside the delivered ZIP; execute against the actual final application DLL.
Copy-Item 'C:\EXPC-Payload\ExpcSandboxTests*' $run
& "$run\ExpcSandboxTests.exe" manager-acceptance winget > 'C:\EXPC-Results\winget-final-console.txt'
$LASTEXITCODE | Set-Content 'C:\EXPC-Results\winget-final-exit.txt'
