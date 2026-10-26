"""Private console fixtures only: never launches the real EXPC executable or GUI."""
import subprocess,os,time,json,uuid,hashlib,shutil,sys
from pathlib import Path
R=Path(__file__).resolve().parent.parent
O=R/'build/lifecycle-fixture'/time.strftime('%Y%m%d-%H%M%S');O.mkdir(parents=True)
V=R/'artifacts/verification'/('lifecycle-'+time.strftime('%Y%m%d-%H%M%S'));V.mkdir(parents=True)
results=[]
def run(args,timeout=120):
 p=subprocess.run(args,cwd=R,capture_output=True,timeout=timeout,creationflags=subprocess.CREATE_NO_WINDOW)
 if p.returncode:raise RuntimeError(p.stdout.decode(errors='replace')[-3000:]+p.stderr.decode(errors='replace')[-1000:])
 return p
vswhere=Path(os.environ['ProgramFiles(x86)'])/'Microsoft Visual Studio/Installer/vswhere.exe'
vs=run([str(vswhere),'-latest','-products','*','-property','installationPath']).stdout.decode().strip()
cmd=O/'compile.cmd';cmd.write_text(f'@call "{vs}\\VC\\Auxiliary\\Build\\vcvars64.bat" >nul\n@rc /nologo /fo "{O}\\fixture.res" "{R}\\tests\\Expc.LifecycleFixture\\fixture.rc"\n@if errorlevel 1 exit /b %errorlevel%\n@cl /nologo /MT /EHsc /std:c++20 /utf-8 "{R}\\tests\\Expc.LifecycleFixture\\native.cpp" /Fo"{O}\\native.obj" /link /OUT:"{O}\\native.exe" "{O}\\fixture.res" bcrypt.lib advapi32.lib user32.lib\n@exit /b %errorlevel%\n')
run(['cmd','/d','/c',str(cmd)])
run(['dotnet','build',str(R/'tests/Expc.LifecycleFixture/Expc.LifecycleFixture.csproj'),'-c','Release','-o',str(O/'managed'),'--nologo'])
managed=['dotnet',str(O/'managed/Expc.LifecycleFixture.dll')];native=[str(O/'native.exe')]
def start(command):return subprocess.Popen(command,cwd=O,stdout=subprocess.PIPE,stderr=subprocess.PIPE,creationflags=subprocess.CREATE_NO_WINDOW)
def ready(marker):
 end=time.time()+10
 while time.time()<end:
  if marker.with_suffix('.ready').exists():return
  time.sleep(.03)
 raise RuntimeError('No fixture ready')
for kind,command in [('managed',managed),('native',native)]:
 prefix='Local\\EXPCfixture-'+uuid.uuid4().hex;mark=O/(kind+'-owner');owner=start(command+[prefix,str(mark)]);ready(mark)
 for i in range(5):
  other=start(command+[prefix,str(O/(kind+str(i)))]);assert other.wait(10)==10,(other.returncode,other.communicate())
 concurrent=[start(command+[prefix,str(O/(kind+'parallel'+str(i)))]) for i in range(6)]
 assert all(p.wait(10)==10 for p in concurrent)
 mark.with_suffix('.stop').touch();assert owner.wait(10)==0
 restart=O/(kind+'restart');p=start(command+[prefix,str(restart)]);ready(restart)
 p.kill();p.wait(10) # Only the PID of our disposable crash fixture, never an app process.
 after=O/(kind+'after-crash');p=start(command+[prefix,str(after)]);ready(after);after.with_suffix('.stop').touch();assert p.wait(10)==0
 results.append({'check':kind+' sequential5/parallel6/exit/restart/crash','pass':True})
# Simultaneous cold starts: no preexisting owner.
for kind,command in [('managed',managed),('native',native)]:
 prefix='Local\\EXPCfixture-'+uuid.uuid4().hex
 marks=[O/(kind+'cold'+str(i)) for i in range(8)]
 processes=[start(command+[prefix,str(m)]) for m in marks]
 end=time.time()+10
 while time.time()<end and sum(p.poll() is not None for p in processes)<7:time.sleep(.03)
 owners=[i for i,m in enumerate(marks) if m.with_suffix('.ready').exists()]
 assert len(owners)==1,owners
 owner=owners[0]
 assert all(p.poll()==10 for i,p in enumerate(processes) if i!=owner)
 marks[owner].with_suffix('.stop').touch();assert processes[owner].wait(10)==0
 results.append({'check':kind+' eight simultaneous cold starts','pass':True})
# Cross-layer: native launcher recognizes managed app lock, no runtime/bootstrap.
prefix='Local\\EXPCfixture-'+uuid.uuid4().hex;mark=O/'cross';p=start(managed+[prefix,str(mark)]);ready(mark)
other=start(native+[prefix,str(O/'cross-native')]);assert other.wait(10)==10,(other.returncode,other.communicate())
mark.with_suffix('.stop').touch();assert p.wait(10)==0
results.append({'check':'native detects direct managed owner','pass':True})
# Real atomic swap and rollback on separate EXE copies, paths with spaces/Cyrillic.
root=O/'обновление with spaces';root.mkdir();origin=root/'EXPCdelete.exe';download=root/'download.exe'
shutil.copy2(O/'native.exe',download);origin.write_bytes(download.read_bytes()+b'old-build')
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
manifest={'schema':1,'version':'6.3.1','asset':'EXPCdelete.exe','bytes':download.stat().st_size,'sha256':sha(download),'architecture':'win-x64','runtimeMinimum':'10.0.12','windowsMinimum':'10.0.18362'}
request={'origin':str(origin),'download':str(download),'oldHash':sha(origin),'manifest':manifest,'appPid':0,'appStart':0,'launcherPid':0,'launcherStart':0,'readyEvent':'Local\\EXPCupdate-'+uuid.uuid4().hex}
requestfile=root/'request.json';requestfile.write_text(json.dumps(request),encoding='utf-8')
run(managed+['swap',str(requestfile)]);assert sha(origin)==request['oldHash']
results.append({'check':'actual swap and rollback; original hash restored','pass':True})
# Locked target must leave the old file intact. Exclusive handle is only our fixture.
import ctypes
k=ctypes.WinDLL('kernel32',use_last_error=True)
k.CreateFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_uint,ctypes.c_uint,ctypes.c_void_p,ctypes.c_uint,ctypes.c_uint,ctypes.c_void_p];k.CreateFileW.restype=ctypes.c_void_p
k.CloseHandle.argtypes=[ctypes.c_void_p]
handle=k.CreateFileW(str(origin),0x80000000,0,None,3,0,None)
assert handle not in (None,ctypes.c_void_p(-1).value)
try:
 locked=subprocess.run(managed+['swap',str(requestfile)],cwd=O,capture_output=True,timeout=20,creationflags=subprocess.CREATE_NO_WINDOW)
 assert locked.returncode!=0
finally:k.CloseHandle(handle)
assert sha(origin)==request['oldHash']
results.append({'check':'locked original preserved; no forced close','pass':True})
# Exact helper code, fixture new executable acknowledges through production protocol.
run(['dotnet','publish',str(R/'src/Expc.SelfUpdate/Expc.SelfUpdate.csproj'),'-c','Release','-o',str(O/'helper'),'--nologo'])
# Nonexistent PID indicates completed fixture processes; choose an impossible PID, never PID 0.
request['appPid']=request['launcherPid']=2147483647;requestfile.write_text(json.dumps(request),encoding='utf-8')
run(['dotnet',str(O/'helper/Expc.SelfUpdate.dll'),str(requestfile)],150)
assert sha(origin)==manifest['sha256'];assert not list(root.glob('*.expc-*'));assert not download.exists()
results.append({'check':'helper replace -> one console fixture launch -> handshake -> backup cleanup','pass':True})
(V/'results.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
print(json.dumps({'results':results,'evidence':str(V)},indent=2),flush=True)
