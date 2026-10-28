"""Retry only verified staging promotion after a locked dist; never launches the EXE."""
import hashlib,json,os,shutil,sys
from pathlib import Path
R=Path(__file__).resolve().parent.parent
E=Path(sys.argv[1]).resolve();assert E.is_relative_to(R/'artifacts/verification')
m=json.loads((E/'artifact.json').read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
source=R/'build/staging/EXPCdelete.exe';dest=R/'dist/EXPCdelete.exe';pending=dest.with_suffix('.new')
assert source.stat().st_size==m['bytes'] and sha(source)==m['sha256']
for p in (source,dest,pending):
 assert p.resolve().is_relative_to(R)
 for q in [p,*p.parents]:
  if q==R:break
  if q.exists():assert not q.lstat().st_file_attributes&1024
if dest.exists():
 old=sha(dest);backup=R/'artifacts/archive/control'/old/'EXPCdelete.exe';backup.parent.mkdir(parents=True,exist_ok=True)
 if not backup.exists():shutil.copy2(dest,backup)
 assert sha(backup)==old
if pending.exists():assert sha(pending)==m['sha256'],'Different pending candidate; preserve and investigate'
else:shutil.copy2(source,pending)
assert sha(pending)==m['sha256']
os.replace(pending,dest)
assert sha(dest)==m['sha256']
print(json.dumps({'promoted':str(dest),'sha256':sha(dest),'bytes':dest.stat().st_size}))
