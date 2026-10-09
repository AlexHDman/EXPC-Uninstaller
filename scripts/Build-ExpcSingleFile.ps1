param([Parameter(Mandatory)][string]$OutputRoot, [switch]$CompactOnly)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath("$PSScriptRoot\..")
$out=[IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $out) { throw 'OutputRoot must be new; old packages are preserved' }
New-Item -ItemType Directory $out | Out-Null
function Run([string]$exe,[string[]]$arguments,[string]$name,[int]$seconds=240) {
 $p=Start-Process $exe -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out\$name.out" -RedirectStandardError "$out\$name.err"
 $null=$p.Handle
 if (!$p.WaitForExit($seconds*1000)) { $p.Kill($true); throw "$name timeout" }
 Get-Content "$out\$name.out" -Tail 10
 if ($p.ExitCode -ne 0) { Get-Content "$out\$name.err"; throw "$name failed: $($p.ExitCode)" }
}
& "$PSScriptRoot\Build-ExpcNative.ps1"
$vs=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -property installationPath
foreach ($variant in $(if($CompactOnly){@('B')}else{@('A','B')})) {
 $self=if($variant -eq 'A'){'true'}else{'false'}
 $pub=Join-Path $out "$variant-publish"
 $base=@('-c','Release','-r','win-x64','--self-contained',$self,'-p:Platform=x64','-p:PublishTrimmed=false','-p:RollForward=LatestPatch','-p:DebugType=None','-p:DebugSymbols=false','-p:RestoreSources=https://api.nuget.org/v3/index.json','--nologo')
 Run 'dotnet' (@('publish','source/ExpcUninstaller/ExpcUninstaller.csproj')+$base+@('-o',$pub)) "$variant-main"
 Run 'dotnet' (@('publish','tools/Expc.PackageProbe/Expc.PackageProbe.csproj')+$base+@('-o',"$pub\package-probe")) "$variant-probe"
 Copy-Item -LiteralPath "$root\Licence.txt","$root\NOTICE","$root\docs\licenses\WinGet-ComInterop-NOTICE.txt","$root\docs\licenses\WinGet-MIT.txt" -Destination "$pub\package-probe"
 if ($variant -eq 'B') {
  foreach($config in Get-ChildItem -LiteralPath $pub -Filter '*.runtimeconfig.json' -Recurse) {
   $json=Get-Content -LiteralPath $config.FullName -Raw | ConvertFrom-Json
   foreach($framework in $json.runtimeOptions.frameworks) { if($framework.name -in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')) { $framework.version='10.0.12' } }
   if ($json.runtimeOptions.framework) { $json.runtimeOptions.framework.version='10.0.12' }
   [IO.File]::WriteAllText($config.FullName,($json|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
  }
 }
 $pack=Join-Path $out "$variant-payload"
 Run 'python' @('-X','utf8',"$root\scripts\Pack-ExpcPayload.py",$pub,$pack) "$variant-compress" 360
 $define=if($variant -eq 'B'){'/DFRAMEWORK_DEPENDENT'}else{''}
 $name=if($variant -eq 'A'){'EXPC-Uninstaller-SelfContained.exe'}else{'EXPC-Uninstaller-Compact.exe'}
 $batch=@"
@call "$vs\VC\Auxiliary\Build\vcvars64.bat" >nul
@rc /nologo /fo "$pack\payload.res" "$pack\payload.rc"
@if errorlevel 1 exit /b %errorlevel%
@cl /nologo /MT /O2 /EHsc /std:c++20 /utf-8 /DUNICODE /D_UNICODE $define /I"$pack" "$root\tools\Expc.PortableLauncher\launcher.cpp" /Fo"$pack\launcher.obj" /link /MANIFEST:EMBED /MANIFESTINPUT:"$root\tools\Expc.PortableLauncher\launcher.manifest" /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT /OUT:"$out\$name" "$pack\payload.res" bcrypt.lib cabinet.lib shell32.lib ole32.lib advapi32.lib user32.lib comctl32.lib gdi32.lib
@exit /b %errorlevel%
"@
 [IO.File]::WriteAllText("$pack\compile.cmd",$batch,[Text.Encoding]::Default)
 Run 'cmd.exe' @('/d','/c',('"'+"$pack\compile.cmd"+'"')) "$variant-launcher"
 Get-Item -LiteralPath "$out\$name" | Select-Object FullName,Length
}
