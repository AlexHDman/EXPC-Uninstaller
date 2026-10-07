param([string]$Payload = 'C:\EXPC-Payload2', [switch]$RunNonMsiSuite)
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or !(Test-Path C:\EXPC-Test\ALLOW-SANDBOX-TESTS)) { exit 99 }
try {
    $work = 'C:\EXPC-StoreFixture'
    New-Item -ItemType Directory $work -Force | Out-Null
    Copy-Item C:\EXPC-StoreInput\fixture.msix "$work\fixture.msix"
    $certificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=EXPC Sandbox Test' -KeyUsage DigitalSignature -FriendlyName 'Disposable EXPC Sandbox fixture' -CertStoreLocation Cert:\CurrentUser\My -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
    Export-Certificate -Cert $certificate -FilePath "$work\fixture.cer" | Out-Null
    Import-Certificate -FilePath "$work\fixture.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    & C:\EXPC-Signing\signtool.exe sign /fd SHA256 /sha1 $certificate.Thumbprint "$work\fixture.msix" > C:\EXPC-Results\store-sign.txt
    if ($LASTEXITCODE -ne 0) { throw 'Fixture signing failed' }
    Add-AppxPackage -Path "$work\fixture.msix"
    Get-AppxPackage -Name EXPC.SandboxFixture.Uninstall | Select-Object Name,PackageFullName,SignatureKind,NonRemovable | ConvertTo-Json | Set-Content C:\EXPC-Results\store-installed.json
    $run = 'C:\EXPC-StoreRun-' + [guid]::NewGuid().ToString('N')
    New-Item -ItemType Directory $run | Out-Null
    Copy-Item "$Payload\*" $run -Recurse
    if ($RunNonMsiSuite) {
        & "$run\ExpcSandboxTests.exe" --skip-msi > C:\EXPC-Results\completion-console.txt
        $LASTEXITCODE | Set-Content C:\EXPC-Results\completion-exit.txt
    }
    Get-FileHash "$run\EXPC-Uninstaller.dll" | ConvertTo-Json | Set-Content C:\EXPC-Results\completion-dll.json
    & "$run\ExpcSandboxTests.exe" store-only > C:\EXPC-Results\store-console.txt
    $LASTEXITCODE | Set-Content C:\EXPC-Results\store-exit.txt
} catch {
    $_ | Out-String | Set-Content C:\EXPC-Results\store-error.txt
    exit 1
}
