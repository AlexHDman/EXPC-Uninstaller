"""Production helper tests on disposable no-window fixtures; never run the EXPC GUI."""
import ctypes as c, hashlib, json, os, shutil, subprocess, sys, time, uuid
from pathlib import Path
R=Path(__file__).resolve().parent.parent
fixture=sorted((R/'build/lifecycle-fixture').iterdir())[-1]
O=R/'build/selfupdate-scenarios'/time.strftime('%Y%m%d-%H%M%S');O.mkdir(parents=True)
V=R/'artifacts/verification'/('selfupdate-'+time.strftime('%Y%m%d-%H%M%S'));V.mkdir(parents=True)
helper=fixture/'helper/Expc.SelfUpdate.dll';binary=fixture/'native.exe';results=[]
k=c.WinDLL('kernel32',use_last_error=True)
k.CreateEventW.argtypes=[c.c_void_p,c.c_int,c.c_int,c.c_wchar_p];k.CreateEventW.restype=c.c_void_p
k.SetEvent.argtypes=[c.c_void_p];k.CloseHandle.argtypes=[c.c_void_p]
k.WaitForSingleObject.argtypes=[c.c_void_p,c.c_uint]
k.GetCurrentProcess.restype=c.c_void_p
k.GetProcessTimes.argtypes=[c.c_void_p,c.POINTER(c.c_ulonglong),c.POINTER(c.c_ulonglong),c.POINTER(c.c_ulonglong),c.POINTER(c.c_ulonglong)]
a,b,d,e=[c.c_ulonglong() for _ in range(4)];assert k.GetProcessTimes(k.GetCurrentProcess(),c.byref(a),c.byref(b),c.byref(d),c.byref(e));ticks=a.value+504911232000000000
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def make(name):
 root=O/name;root.mkdir();origin=root/'старый EXPC.exe';cache=root/'cache';cache.mkdir();download=cache/'EXPCdelete.exe'
 shutil.copy2(binary,download);origin.write_bytes(binary.read_bytes()+b'old-build')
 manifest=dict(schema=1,version='6.3.1',asset='EXPCdelete.exe',bytes=download.stat().st_size,sha256=sha(download),architecture='win-x64',runtimeMinimum='10.0.12',windowsMinimum='10.0.18362')
 req=dict(origin=str(origin),download=str(download),oldHash=sha(origin),manifest=manifest,appPid=2147483647,appStart=0,launcherPid=2147483647,launcherStart=0,readyEvent='Local\\EXPCupdate-'+uuid.uuid4().hex)
 return root,origin,download,cache/'request.json',req
def start(file,req,env=None,component=helper):
 file.write_text(json.dumps(req),encoding='utf-8')
 return subprocess.Popen(['dotnet',str(component),str(file)],cwd=O,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,creationflags=subprocess.CREATE_NO_WINDOW)
def done(p,seconds=85):
 code=p.wait(seconds);out,err=p.communicate();return code,(out+err).decode(errors='replace')
def record(name,**extra):
 results.append(dict(check=name,**extra));(V/'results.json').write_text(json.dumps(results,indent=2),encoding='utf-8');print(name,extra,flush=True)
# Hold a genuine live PID: helper must time out, not kill it, and reject a parallel helper.
root,origin,download,file,req=make('wait and duplicate');req.update(appPid=os.getpid(),appStart=ticks)
p=start(file,req);time.sleep(.7)
r2,o2,d2,f2,q2=make('parallel');q=start(f2,q2);code,err=done(q,15);assert code==1,(code,err);assert sha(o2)==q2['oldHash']
record('parallel helper refused, original preserved',passed=True)
code,err=done(p,75);assert code==1,(code,err);assert sha(origin)==req['oldHash'];assert 'Old instance has not exited' in Path(str(file)+'.result.txt').read_text()
record('old process timeout, no replacement or kill',passed=True)
# Real handshake timeout and atomic rollback. The no-window fixture deliberately does not acknowledge.
root,origin,download,file,req=make('handshake rollback');env=dict(os.environ,EXPC_FIXTURE_NO_ACK='1');p=start(file,req,env);code,err=done(p);assert code==1,(code,err);assert sha(origin)==req['oldHash'];assert not list(root.glob('rollback-*.exe'))
record('handshake timeout, exited candidate rolled back',passed=True)
# Retry the exact failed request with an acknowledging child.
p=start(file,req);code,err=done(p,20);assert code==0,(code,err);assert sha(origin)==req['manifest']['sha256'];assert not download.exists();assert not list(root.glob('replace-*.exe')) and not list(root.glob('rollback-*.exe'))
record('retry after failure, handshake, cleanup',passed=True)
# Explicit access denial using the readonly attribute on only our copied fixture.
root,origin,download,file,req=make('readonly target');os.chmod(origin,0o444)
try:
 p=start(file,req,dict(os.environ,EXPC_FIXTURE_NO_ACK='1'));code,err=done(p,20);assert code==1,(code,err);assert sha(origin)==req['oldHash']
finally:os.chmod(origin,0o666)
record('readonly target denied, original preserved',passed=True)
# Preserve a preexisting notification; fixtures must never leave a fake user outcome.
logroot=Path(os.environ['LOCALAPPDATA'])/'EXPC/Uninstaller/Logs'
outcome=logroot/'self-update-result.json';previous=outcome.read_bytes() if outcome.exists() else None
journals=[]
try:
 # Current handshake protocol: acknowledgement alone must not authorize replacement.
 root,origin,download,file,req=make('cancel handoff');req['journalId']=uuid.uuid4().hex;journals.append(req['journalId'])
 accepted=k.CreateEventW(None,True,False,req['readyEvent']+'-helper');commit=k.CreateEventW(None,True,False,req['readyEvent']+'-commit');assert accepted and commit
 try:
  p=start(file,req);assert k.WaitForSingleObject(accepted,10000)==0;code,err=done(p,25);assert code==1,(code,err);assert sha(origin)==req['oldHash']
 finally:k.CloseHandle(accepted);k.CloseHandle(commit)
 record('accepted helper without committed handoff never replaces',passed=True)
 # Complete current handoff with real helper and fixture, never a working EXE.
 root,origin,download,file,req=make('committed handoff');req['journalId']=uuid.uuid4().hex;journals.append(req['journalId'])
 accepted=k.CreateEventW(None,True,False,req['readyEvent']+'-helper');commit=k.CreateEventW(None,True,False,req['readyEvent']+'-commit');assert accepted and commit
 try:
  p=start(file,req);assert k.WaitForSingleObject(accepted,10000)==0;assert k.SetEvent(commit);code,err=done(p,20);assert code==0,(code,err);assert sha(origin)==req['manifest']['sha256']
 finally:k.CloseHandle(accepted);k.CloseHandle(commit)
 record('accepted and committed helper replaces once, handshake succeeds',passed=True)
finally:
 # Only our exact journal IDs are copied to evidence and removed; preserve prior outcome.
 for identity in journals:
  log=logroot/('self-update-'+identity+'.jsonl')
  for owned in (log,Path(str(log)+'.result.txt')):
   if owned.exists():shutil.copy2(owned,V/owned.name);owned.unlink()
 if outcome.exists() and json.loads(outcome.read_text())['journal'] in journals:
  if previous is None:outcome.unlink()
  else:outcome.write_bytes(previous)
# Optional exact old helper; caller supplies the known 6.3.2 payload, never GUI.
if len(sys.argv)>1:
 old=Path(sys.argv[1]);root,origin,download,file,req=make('old 6.3.2 helper');p=start(file,req,component=old);code,err=done(p,20);assert code==0,(code,err);assert sha(origin)==req['manifest']['sha256']
 record('exact old 6.3.2 helper replacement with no-window fixture',passed=True,helperSha256=sha(old))
record('physical GUI update and real distinct-volume replacement',status='NOT TESTED')
print(V,flush=True)
