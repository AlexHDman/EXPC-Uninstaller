param([string]$Payload = 'C:\EXPC-Payload', [string]$PortableArchive = '', [switch]$RunStoreFixture)
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or !(Test-Path -LiteralPath 'C:\EXPC-Test\ALLOW-SANDBOX-TESTS')) { exit 99 }
try {
    $run = 'C:\EXPC-Run-' + [guid]::NewGuid().ToString('N')
    New-Item -ItemType Directory -Path $run | Out-Null
    Copy-Item -Path "$Payload\*" -Destination $run -Recurse
    [pscustomobject]@{User=$env:USERNAME; Dotnet=(Get-Command dotnet -ErrorAction SilentlyContinue).Source; SystemDotnet=(Test-Path 'C:\Program Files\dotnet'); OS=[Environment]::OSVersion.VersionString} | ConvertTo-Json | Set-Content C:\EXPC-Results\environment.json
    $portableRun = $run
    if ($PortableArchive) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $portableRun = 'C:\EXPC-Portable-' + [guid]::NewGuid().ToString('N')
        [System.IO.Compression.ZipFile]::ExtractToDirectory($PortableArchive, $portableRun)
        Get-FileHash -LiteralPath $PortableArchive | ConvertTo-Json | Set-Content C:\EXPC-Results\tested-zip.json
    }
    Get-FileHash "$portableRun\EXPC-Uninstaller.dll" | ConvertTo-Json | Set-Content C:\EXPC-Results\tested-dll.json
    $app = Start-Process -FilePath "$portableRun\EXPC-Uninstaller.exe" -PassThru
    Start-Sleep -Seconds 4
    Copy-Item "$portableRun\startup-metrics.jsonl" C:\EXPC-Results\startup-metrics.jsonl
    # Only the process just launched in this disposable Sandbox; X normally hides it to tray.
    Stop-Process -Id $app.Id
    & "$run\ExpcSandboxTests.exe" command-fixtures > C:\EXPC-Results\command-console.txt
    $LASTEXITCODE | Set-Content C:\EXPC-Results\command-exit.txt
    & "$run\ExpcSandboxTests.exe" --skip-msi > C:\EXPC-Results\uninstall-console.txt
    $LASTEXITCODE | Set-Content C:\EXPC-Results\uninstall-exit.txt
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory('C:\EXPC-UpdateInput\chocolatey.nupkg','C:\EXPC-ChocoPackage')
    $env:ChocolateyInstall = 'C:\EXPC-ChocoPackage\tools\chocolateyInstall'
    $choco = Join-Path $env:ChocolateyInstall 'choco.exe'
    New-Item -ItemType Directory -Path "$env:ChocolateyInstall\bin" -Force | Out-Null
    # EXPC resolves the standard bin path; the official exe lives at the package root in this isolated fixture.
    Copy-Item -LiteralPath $choco -Destination "$env:ChocolateyInstall\bin\choco.exe"
    Get-ChildItem -LiteralPath $env:ChocolateyInstall -File | Where-Object Extension -in '.dll','.config' | Copy-Item -Destination "$env:ChocolateyInstall\bin"
    $env:Path = "$env:ChocolateyInstall\bin;" + $env:Path
    & $choco --version > C:\EXPC-Results\choco-version.txt
    & $choco source disable --name chocolatey --limit-output > C:\EXPC-Results\choco-source.txt
    New-Item -ItemType Directory -Path C:\EXPC-Feed,C:\EXPC-Package\tools -Force | Out-Null
    & $choco source add --name EXPCFixture --source C:\EXPC-Feed --limit-output >> C:\EXPC-Results\choco-source.txt
    function Make-Package([string]$Version,[bool]$Fail) {
        $spec = @"
<?xml version="1.0"?><package><metadata><id>expc.sandbox.update.fixture</id><version>$Version</version><authors>EXPC Sandbox Tests</authors><description>Disposable isolated update test</description></metadata><files><file src="tools\**" target="tools" /></files></package>
"@
        [IO.File]::WriteAllText('C:\EXPC-Package\fixture.nuspec',$spec)
        $script = @'
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or !(Test-Path 'C:\EXPC-Test\ALLOW-SANDBOX-TESTS')) { throw 'Sandbox only' }
if ($env:ChocolateyPackageVersion -eq '3.0.0') { throw 'Intentional update fixture failure' }
$key='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EXPC-Update-Fixture'
New-Item $key -Force | Out-Null
New-ItemProperty $key -Name DisplayName -Value 'EXPC Update Fixture' -PropertyType String -Force | Out-Null
New-ItemProperty $key -Name Publisher -Value 'EXPC Sandbox Tests' -PropertyType String -Force | Out-Null
New-ItemProperty $key -Name DisplayVersion -Value $env:ChocolateyPackageVersion -PropertyType String -Force | Out-Null
'@
        [IO.File]::WriteAllText('C:\EXPC-Package\tools\chocolateyInstall.ps1',$script)
        & $choco pack C:\EXPC-Package\fixture.nuspec --outputdirectory C:\EXPC-Feed --limit-output >> C:\EXPC-Results\choco-pack.txt
        if ($LASTEXITCODE -ne 0) { throw 'Fixture pack failed' }
    }
    Make-Package '1.0.0' $false
    & $choco install expc.sandbox.update.fixture --version 1.0.0 --source C:\EXPC-Feed --yes --limit-output > C:\EXPC-Results\fixture-install.txt
    if ($LASTEXITCODE -ne 0) { throw 'Initial fixture install failed' }
    Make-Package '2.0.0' $false
    & "$run\ExpcSandboxTests.exe" update-fixture success > C:\EXPC-Results\update-success-console.txt
    $LASTEXITCODE | Set-Content C:\EXPC-Results\update-success-exit.txt
    Make-Package '3.0.0' $true
    & "$run\ExpcSandboxTests.exe" update-fixture error > C:\EXPC-Results\update-error-console.txt
    $LASTEXITCODE | Set-Content C:\EXPC-Results\update-error-exit.txt
    if ($RunStoreFixture) { & C:\EXPC-Scripts\Run-Completion-Store.ps1 -Payload $Payload }
    # Final visible portable window remains available in the test VM.
    Start-Process -FilePath "$portableRun\EXPC-Uninstaller.exe"
} catch { $_ | Out-String | Set-Content C:\EXPC-Results\sandbox-error.txt; exit 1 }
