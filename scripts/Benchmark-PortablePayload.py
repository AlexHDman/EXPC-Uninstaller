import subprocess,time,json,os,hashlib,statistics
from pathlib import Path
R=Path(__file__).resolve().parent.parent;O=R/'artifacts/verification/restructure-20261026';B=R/'build/extraction-check'/time.strftime('%Y%m%d-%H%M%S');B.mkdir(parents=True,exist_ok=False)
vs=subprocess.check_output([str(Path(os.environ['ProgramFiles(x86)'])/'Microsoft Visual Studio/Installer/vswhere.exe'),'-latest','-products','*','-property','installationPath']).decode().strip()
variants=[('before',R/'artifacts/archive/control/71d8390e', 'EXPC-Uninstaller-Compact.exe'),('after',R/'build/staging','EXPCdelete.exe')];results=[]
for label,root,name in variants:
 header=root/'B-payload'
 if label=='before':
  header=B/'baseline-header';header.mkdir();m=json.loads((root/'payload-manifest.json').read_text())
  (header/'manifest.json').write_text(json.dumps(m));(header/'payload.h').write_text('#pragma once\n#define RUNTIME_VERSION L"10.0.12"\n#define MIN_PATCH 12\n#define PAYLOAD_SHA "'+m['sha256']+'"\n')
 exe=root/name;harness=B/(label+'.exe');cmd=B/(label+'.cmd')
 cmd.write_text(f'''@call "{vs}\\VC\\Auxiliary\\Build\\vcvars64.bat" >nul
@cl /nologo /MT /O2 /EHsc /std:c++20 /utf-8 /DUNICODE /D_UNICODE /DFRAMEWORK_DEPENDENT /I"{header}" "{R}\\tests\\PortablePackaging\\ExtractionHarness.cpp" /Fo"{B}\\{label}.obj" /link /OUT:"{harness}" bcrypt.lib cabinet.lib shell32.lib ole32.lib advapi32.lib user32.lib comctl32.lib gdi32.lib
@exit /b %errorlevel%
''',encoding='utf-8')
 p=subprocess.run(['cmd','/d','/c',str(cmd)],capture_output=True,timeout=120);(O/(label+'-harness-build.log')).write_bytes(p.stdout+p.stderr);assert p.returncode==0,p.stdout
 def run(cache,expected=0):
  start=time.perf_counter();p=subprocess.run([str(harness),str(exe),str(cache)],capture_output=True,timeout=120,creationflags=subprocess.CREATE_NO_WINDOW);assert p.returncode==expected,p.stderr;return round((time.perf_counter()-start)*1000,3)
 timings=[]
 for i in range(3):
  cache=B/(label+'-cache-'+str(i));assert not cache.exists(),'Need fresh cache for measurement';timings.append({'coldMs':run(cache),'warmMs':run(cache)})
 manifest=json.loads((header/'manifest.json').read_text());folder=B/(label+'-cache-0')/manifest['sha256']
 for f in manifest['files']:assert hashlib.sha256((folder/f['path']).read_bytes()).hexdigest()==f['sha256']
 notice=folder/'NOTICE';old=notice.read_bytes();notice.write_bytes(old+b'\nfixture tamper\n')
 try:run(B/(label+'-cache-0'),1)
 finally:notice.write_bytes(old)
 cache=B/(label+'-parallel');assert not cache.exists()
 children=[subprocess.Popen([str(harness),str(exe),str(cache)],stdout=subprocess.PIPE,stderr=subprocess.PIPE,creationflags=subprocess.CREATE_NO_WINDOW) for _ in range(2)]
 assert all(p.communicate(timeout=120)[0]==b'' and p.returncode==0 for p in children)
 results.append({'variant':label,'exeBytes':exe.stat().st_size,'sha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'fileBytes':sum(f['bytes'] for f in manifest['files']),'payloadBytes':manifest['compressedBytes'],'timings':timings,'tamperRejected':True,'parallel':'2/2','scope':'same Extract code in separate console harness; candidate PE data only; not window/list startup'})
 print(results[-1],flush=True)
(O/'extraction-benchmark.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
