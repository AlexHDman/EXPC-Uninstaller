"""Headless compact build; checked staging promotion, no application execution."""
import os,sys,json,hashlib,subprocess,time,re,shutil
from pathlib import Path
R=Path(__file__).resolve().parent.parent
S=R/'build/staging';V=R/'artifacts/verification'/time.strftime('%Y%m%d-%H%M%S')
V.mkdir(parents=True,exist_ok=False)
D=R/'dist/EXPCdelete.exe';OWNER=R/'build/staging-files.json'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def safe(p):
 assert p.resolve().is_relative_to(R.resolve()),p
 for q in [p,*p.parents]:
  if q==R:break
  if q.exists():assert not q.lstat().st_file_attributes&1024,q
 return p
commands=[]
owns_stage=False
lock=R/'build/staging.lock'
lock.parent.mkdir(exist_ok=True)
lock_handle=lock.open('x')
def run(name,args,seconds=300):
 start=time.perf_counter()
 with (V/(name+'.log')).open('w',encoding='utf-8') as log:
  p=subprocess.run(args,cwd=R,stdout=log,stderr=subprocess.STDOUT,timeout=seconds,creationflags=subprocess.CREATE_NO_WINDOW,encoding='utf-8')
 commands.append(dict(name=name,args=args,exit=p.returncode,seconds=round(time.perf_counter()-start,3)))
 (V/'commands.json').write_text(json.dumps(commands,indent=2),encoding='utf-8')
 print(name,p.returncode,flush=True)
 if p.returncode:raise RuntimeError((V/(name+'.log')).read_text(encoding='utf-8',errors='replace')[-7000:])
try:
 # Only previous, recorded compiler/package outputs can be removed on a repeat build.
 if S.exists():
  known=json.loads(OWNER.read_text())
  current=[p for p in S.rglob('*') if p.is_file()]
  assert {str(p.relative_to(S)) for p in current}==set(known),'Unrecognized staging files; preserve and investigate'
  for p in current:
   safe(p);assert sha(p)==known[str(p.relative_to(S))],'Staging changed outside build'
  for p in current:p.unlink()
  for p in sorted(S.rglob('*'),key=lambda p:len(p.parts),reverse=True):
   safe(p)
   if p.is_dir():p.rmdir()
 S.mkdir(parents=True,exist_ok=True)
 owns_stage=True
 run('native',['pwsh','-NoProfile','-File',str(R/'scripts/Build-ExpcNative.ps1')],150)
 dotnet=str(Path(os.environ['ProgramFiles'])/'dotnet/dotnet.exe')
 base=['-c','Release','-r','win-x64','--self-contained','false','-p:Platform=x64','-p:PublishTrimmed=false','-p:RollForward=LatestPatch','-p:DebugType=None','-p:DebugSymbols=false','-p:RestoreSources=https://api.nuget.org/v3/index.json','-p:SatelliteResourceLanguages=ru%3Ben','--nologo']
 pub=S/'B-publish';pack=S/'B-payload'
 run('build-main',[dotnet,'build',str(R/'src/ExpcUninstaller/ExpcUninstaller.csproj'),*base,'--no-incremental'])
 run('publish-main',[dotnet,'publish',str(R/'src/ExpcUninstaller/ExpcUninstaller.csproj'),*base,'--no-build','--no-restore','-o',str(pub)])
 run('build-probe',[dotnet,'build',str(R/'src/Expc.PackageProbe/Expc.PackageProbe.csproj'),*base,'--no-incremental'])
 run('publish-probe',[dotnet,'publish',str(R/'src/Expc.PackageProbe/Expc.PackageProbe.csproj'),*base,'--no-build','--no-restore','-o',str(pub/'package-probe')])
 run('publish-updater',[dotnet,'publish',str(R/'src/Expc.SelfUpdate/Expc.SelfUpdate.csproj'),*base,'-o',str(pub/'self-update')])
 for p in [R/'Licence.txt',R/'NOTICE',R/'docs/licenses/WinGet-ComInterop-NOTICE.txt',R/'docs/licenses/WinGet-MIT.txt']:shutil.copy2(p,pub/'package-probe'/p.name)
 for p in pub.rglob('*.runtimeconfig.json'):
  j=json.loads(p.read_text(encoding='utf-8-sig'));o=j['runtimeOptions']
  for f in o.get('frameworks',[])+([o['framework']] if 'framework' in o else []):
   if f['name'] in ('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App'):f['version']='10.0.12'
  p.write_text(json.dumps(j,indent=2),encoding='utf-8')
 # EXPC selects only ru/en (Localization.Select); neutral KlocTools resources are English.
 # Filter only this proven donor satellite family, never arbitrary dependency/runtime resources.
 excluded=[]
 for satellite in pub.glob('*/KlocTools.resources.dll'):
  if satellite.parent.name not in ('ru','en'):
   excluded.append({'path':str(satellite.relative_to(pub)), 'sha256':sha(satellite)})
   satellite.unlink()
   if not any(satellite.parent.iterdir()):satellite.parent.rmdir()
 for deps in pub.glob('*.deps.json'):
  obj=json.loads(deps.read_text(encoding='utf-8-sig'))
  for target in obj.get('targets',{}).values():
   for library in target.values():
    if 'resources' in library:
     library['resources']={k:v for k,v in library['resources'].items() if not k.endswith('KlocTools.resources.dll') or v.get('locale') in ('ru','en')}
  deps.write_text(json.dumps(obj,indent=2),encoding='utf-8')
 (V/'excluded-satellites.json').write_text(json.dumps(excluded,indent=2),encoding='utf-8')
 run('pack',['python','-X','utf8',str(R/'scripts/Pack-ExpcPayload.py'),str(pub),str(pack)],360)
 vswhere=Path(os.environ['ProgramFiles(x86)'])/'Microsoft Visual Studio/Installer/vswhere.exe'
 vs=subprocess.check_output([str(vswhere),'-latest','-products','*','-property','installationPath'],timeout=20).decode().strip()
 assert vs
 cmd=f'''@call "{vs}\\VC\\Auxiliary\\Build\\vcvars64.bat" >nul
@rc /nologo /fo "{pack}\\payload.res" "{pack}\\payload.rc"
@if errorlevel 1 exit /b %errorlevel%
@cl /nologo /MT /O2 /Brepro /EHsc /std:c++20 /utf-8 /DUNICODE /D_UNICODE /DFRAMEWORK_DEPENDENT /I"{pack}" "{R}\\src\\Expc.PortableLauncher\\launcher.cpp" /Fo"{pack}\\launcher.obj" /link /Brepro /MANIFEST:EMBED /MANIFESTINPUT:"{R}\\src\\Expc.PortableLauncher\\launcher.manifest" /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT /OUT:"{S}\\EXPCdelete.exe" "{pack}\\payload.res" bcrypt.lib cabinet.lib shell32.lib ole32.lib advapi32.lib user32.lib comctl32.lib gdi32.lib
@exit /b %errorlevel%
'''
 (pack/'compile.cmd').write_text(cmd,encoding='utf-8')
 run('launcher',['cmd.exe','/d','/c',str(pack/'compile.cmd')],180)
 version=__import__('xml.etree.ElementTree',fromlist=['parse']).parse(R/'EXPC.Version.props').findtext('.//Version')
 run('versions',['pwsh','-NoProfile','-File',str(R/'scripts/Check-ExpcVersion.ps1'),'-Stage',str(S),'-Version',version],30)
 policy=S/'policy-tests.exe'
 policycmd=f'@call "{vs}\\VC\\Auxiliary\\Build\\vcvars64.bat" >nul\n@cl /nologo /MT /O2 /EHsc /std:c++20 /utf-8 "{R}\\src\\Expc.PortableLauncher\\policy-tests.cpp" /Fo"{S}\\policy-tests.obj" /link /OUT:"{policy}" bcrypt.lib\n@exit /b %errorlevel%\n'
 (S/'policy.cmd').write_text(policycmd,encoding='utf-8')
 run('policy-build',['cmd.exe','/d','/c',str(S/'policy.cmd')],120)
 run('policy-test',[str(policy)],30)
 tests='|'.join('FullyQualifiedName~'+x for x in ['CatalogTests','ActionTests','UpdateTests','VersionTests','VaultTests','WindowPlacementTests','PackagingTests','SelfUpdateTests'])
 run('tests',[dotnet,'test',str(R/'tests/ExpcUninstallerTests/ExpcUninstallerTests.csproj'),'-c','Release','-p:Platform=x64','-p:RestoreSources=https://api.nuget.org/v3/index.json','--filter',tests,'--logger','trx;LogFileName=safe-tests.trx','--results-directory',str(V),'--nologo'])
 run('inspect',['python','-X','utf8',str(R/'scripts/Inspect-ExpcPayload.py'),str(S)])
 for name,p in [('payload-manifest.json',pack/'manifest.json'),('inspection.json',S/'inspection.json')]:shutil.copy2(p,V/name)
 artifact=S/'EXPCdelete.exe'
 inputs={str(p.relative_to(R)):sha(p) for folder in ['src','tests','scripts','assets'] for p in (R/folder).rglob('*') if p.is_file() and not any(part in ('bin','obj') for part in p.relative_to(R).parts)}
 (V/'producer-inputs.json').write_text(json.dumps(inputs,indent=2),encoding='utf-8')
 manifest={'exe':str(D),'bytes':artifact.stat().st_size,'sha256':sha(artifact),'producerHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=R).decode().strip(),'workingTree':'dirty' if subprocess.check_output(['git','status','--porcelain','--untracked-files=no'],cwd=R).strip() else 'clean','runtime':'Microsoft.WindowsDesktop.App 10.0.12 or later stable 10.0 x64','GUI':'NOT TESTED','execution':'EXE never executed; PE read as data'}
 (V/'artifact.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
 version=__import__('xml.etree.ElementTree',fromlist=['parse']).parse(R/'EXPC.Version.props').findtext('.//Version')
 release={'schema':1,'version':version,'asset':'EXPCdelete.exe','bytes':manifest['bytes'],'sha256':manifest['sha256'],'architecture':'win-x64','runtimeMinimum':'10.0.12','windowsMinimum':'10.0.18362'}
 (V/'EXPCdelete.update.json').write_text(json.dumps(release,indent=2),encoding='utf-8')
 (V/'SHA256SUMS.txt').write_text(manifest['sha256']+'  EXPCdelete.exe\n'+sha(V/'EXPCdelete.update.json')+'  EXPCdelete.update.json\n',encoding='utf-8')
 safe(D);D.parent.mkdir(exist_ok=True)
 if D.exists() and sha(D)!=manifest['sha256']:
  old=sha(D);archive=R/'artifacts/archive/control'/old;archive.mkdir(parents=True,exist_ok=True)
  backup=archive/'EXPCdelete.exe'
  if not backup.exists():shutil.copy2(D,backup)
  assert sha(backup)==old
 # Same-volume atomic replacement only after all gates; the old EXE survives any earlier failure.
 temp=D.with_suffix('.new');assert not temp.exists();shutil.copy2(artifact,temp);assert sha(temp)==manifest['sha256'];os.replace(temp,D)
 print(json.dumps(manifest,indent=2),flush=True)
finally:
 if owns_stage and S.exists():OWNER.write_text(json.dumps({str(p.relative_to(S)):sha(p) for p in S.rglob('*') if p.is_file()},indent=2),encoding='utf-8')
 lock_handle.close();lock.unlink()
 print('Evidence:',V,flush=True)
