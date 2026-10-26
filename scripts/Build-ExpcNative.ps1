param([string]$Output = "$PSScriptRoot\..\build\native")
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath("$PSScriptRoot\..")
$out=[IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force $out | Out-Null
$vs=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -property installationPath
if (!$vs) { throw 'MSVC Build Tools not found' }
$cmd=@"
@call "$vs\VC\Auxiliary\Build\vcvars64.bat" >nul
@cl /nologo /LD /MT /O2 /Brepro /EHsc /std:c++20 /utf-8 /DUNICODE /D_UNICODE "$root\src\Expc.PortableLauncher\recycle.cpp" /Fo"$out\recycle.obj" /link /Brepro /OUT:"$out\Expc.Native.dll" /IMPLIB:"$out\Expc.Native.lib" ole32.lib shell32.lib uuid.lib
@exit /b %errorlevel%
"@
$batch=Join-Path $out build-native.cmd
[IO.File]::WriteAllText($batch,$cmd,[Text.Encoding]::Default)
$p=Start-Process cmd.exe -ArgumentList @('/d','/c',('"'+$batch+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out\build.out" -RedirectStandardError "$out\build.err"
$null=$p.Handle
if (!$p.WaitForExit(120000)) { $p.Kill($true); throw 'Native build timeout' }
Get-Content "$out\build.out"
Get-Content "$out\build.err"
if ($p.ExitCode -ne 0) { throw "Native build failed: $($p.ExitCode)" }
