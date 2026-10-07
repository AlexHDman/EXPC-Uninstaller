$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or !(Test-Path -LiteralPath 'C:\EXPC-Test\ALLOW-SANDBOX-TESTS')) { exit 99 }
$run='C:\EXPC-ManagerRun'
New-Item -ItemType Directory -Path $run | Out-Null
Copy-Item 'C:\EXPC-Payload\*' $run -Recurse
Start-Process "$run\ExpcSandboxTests.exe" -ArgumentList 'vault-receive','C:\EXPC-VaultTransfer' -WindowStyle Hidden -RedirectStandardError C:\EXPC-Results\vault-error.txt
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    [IO.Compression.ZipFile]::ExtractToDirectory('C:\EXPC-Input\DesktopAppInstaller_Dependencies.zip','C:\EXPC-WinGetDependencies')
    $dependencies=@(Get-ChildItem C:\EXPC-WinGetDependencies -Recurse -File | Where-Object { $_.Extension -in '.appx','.msix' -and $_.FullName -match '\\x64\\' } | Select-Object -ExpandProperty FullName)
    Add-AppxPackage -Path C:\EXPC-Input\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle -DependencyPath $dependencies
    $winget=Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'
    & $winget --version > C:\EXPC-Results\winget-version.txt
    & $winget install --id 7zip.7zip --exact --version 25.01 --source winget --scope machine --architecture x64 --installer-type exe --accept-source-agreements --disable-interactivity > C:\EXPC-Results\winget-fixture-install.txt
    if($LASTEXITCODE -ne 0){throw 'Test 7-Zip installation failed'}
    & "$run\ExpcSandboxTests.exe" manager-acceptance winget > C:\EXPC-Results\winget-console.txt
    $LASTEXITCODE | Set-Content C:\EXPC-Results\winget-exit.txt
} catch { $_ | Out-String | Set-Content C:\EXPC-Results\winget-bootstrap-error.txt }
try {
    [IO.Compression.ZipFile]::ExtractToDirectory('C:\EXPC-Input\mingit.zip','C:\EXPC-MinGit')
    $env:Path='C:\EXPC-MinGit\cmd;'+$env:Path
    & C:\EXPC-Input\install-scoop.ps1 -RunAsAdmin -ScoopDir C:\EXPC-Scoop > C:\EXPC-Results\scoop-bootstrap.txt
    $env:SCOOP='C:\EXPC-Scoop'; $scoop='C:\EXPC-Scoop\shims\scoop.ps1'
    if(!(Test-Path $scoop)){throw 'Scoop bootstrap did not create shim'}
    & C:\EXPC-MinGit\cmd\git.exe -C C:\EXPC-Scoop\buckets\main rev-parse HEAD > C:\EXPC-Results\scoop-bucket-commit.txt
    $manifest='C:\EXPC-Scoop\buckets\main\bucket\7zip.json'
    $current=[IO.File]::ReadAllText($manifest)
    Copy-Item C:\EXPC-Input\7zip-old.json $manifest
    & $scoop install main/7zip > C:\EXPC-Results\scoop-fixture-install.txt
    if($LASTEXITCODE -ne 0){throw 'Scoop fixture install failed'}
    [IO.File]::WriteAllText($manifest,$current)
    & $scoop config last_update ([DateTime]::Now.ToString('o')) > C:\EXPC-Results\scoop-config.txt
    & "$run\ExpcSandboxTests.exe" manager-acceptance scoop > C:\EXPC-Results\scoop-console.txt
    $LASTEXITCODE | Set-Content C:\EXPC-Results\scoop-exit.txt
} catch { $_ | Out-String | Set-Content C:\EXPC-Results\scoop-bootstrap-error.txt }
Get-FileHash "$run\EXPC-Uninstaller.dll" | ConvertTo-Json | Set-Content C:\EXPC-Results\tested-dll.json
