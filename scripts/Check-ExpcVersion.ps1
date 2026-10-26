param([Parameter(Mandatory)][string]$Stage,[Parameter(Mandatory)][string]$Version)
$ErrorActionPreference='Stop'
foreach ($relative in @('EXPCdelete.exe','B-publish\EXPC-Uninstaller.dll','B-publish\self-update\Expc.SelfUpdate.dll')) {
    $info=(Get-Item -LiteralPath (Join-Path $Stage $relative)).VersionInfo
    if ($info.FileVersion -ne $Version -or $info.ProductVersion -ne $Version) { throw "Version mismatch: $relative ($($info.FileVersion), $($info.ProductVersion)); expected $Version" }
}
Write-Output "Launcher, app and update helper: $Version"
