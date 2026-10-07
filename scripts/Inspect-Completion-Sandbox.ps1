if ($env:USERNAME -ne 'WDAGUtilityAccount' -or !(Test-Path C:\EXPC-Test\ALLOW-SANDBOX-TESTS)) { exit 99 }
$packages = @(Get-AppxPackage)
$policy = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
@{
    User = $env:USERNAME
    PackageCount = $packages.Count
    RemovableNonFrameworkCount = @($packages | Where-Object { !$_.NonRemovable -and !$_.IsFramework -and !$_.IsResourcePackage }).Count
    EnableLUA = $policy.EnableLUA
    ConsentPromptBehaviorAdmin = $policy.ConsentPromptBehaviorAdmin
    PromptOnSecureDesktop = $policy.PromptOnSecureDesktop
    OsVersion = [Environment]::OSVersion.Version.ToString()
    Note = 'Read-only environment inspection. No Store packages removed and no UAC settings changed.'
} | ConvertTo-Json | Set-Content C:\EXPC-Results\environment.json -Encoding UTF8
