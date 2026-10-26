"""Publish only a verified exact-commit build to the EXPC repository, draft first."""
import argparse,hashlib,json,subprocess,urllib.request,time
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('evidence',type=Path);p.add_argument('--publish',action='store_true');a=p.parse_args()
R=Path(__file__).resolve().parent.parent;E=a.evidence.resolve();repo='AlexHDman/EXPC-Uninstaller'
def run(args):return subprocess.check_output(args,cwd=R,timeout=180,stderr=subprocess.STDOUT).decode('utf-8')
def sha(file):return hashlib.sha256(file.read_bytes()).hexdigest()
artifact=json.loads((E/'artifact.json').read_text(encoding='utf-8-sig'));manifest=json.loads((E/'EXPCdelete.update.json').read_text(encoding='utf-8-sig'))
exe=R/'dist/EXPCdelete.exe';tag='v'+manifest['version'];commit=artifact['producerHead']
assert artifact['workingTree']=='clean','Require clean exact-commit producer'
assert run(['git','rev-parse','HEAD']).strip()==commit
assert run(['git','remote','get-url','origin']).strip().removesuffix('.git').rstrip('/')=='https://github.com/'+repo
assert sha(exe)==manifest['sha256']==artifact['sha256'] and exe.stat().st_size==manifest['bytes']==artifact['bytes']
assert run(['git','rev-list','-n','1',tag]).strip()==commit
assets=[exe,E/'EXPCdelete.update.json',E/'SHA256SUMS.txt']
notes=E/'release-notes.md'
assert notes.is_file()
run(['gh','release','create',tag,'--repo',repo,'--verify-tag','--draft','--title','EXPC Uninstaller '+manifest['version'],'--notes-file',str(notes),*[str(x) for x in assets]])
back=E/'draft-download';back.mkdir()
run(['gh','release','download',tag,'--repo',repo,'--dir',str(back)])
for local in assets:
 fetched=back/local.name
 assert fetched.stat().st_size==local.stat().st_size and sha(fetched)==sha(local),'Draft asset mismatch: '+local.name
receipt={'version':manifest['version'],'commit':commit,'tag':tag,'draftAssetsVerified':True,'published':False}
(E/'release-receipt.json').write_text(json.dumps(receipt,indent=2))
if a.publish:
 run(['gh','release','edit',tag,'--repo',repo,'--draft=false','--latest'])
 receipt['published']=True
 # No Authorization header and no GitHub CLI: public API/download verification.
 def public(url):
  req=urllib.request.Request(url,headers={'User-Agent':'EXPC-release-verification','Accept':'application/vnd.github+json'})
  with urllib.request.urlopen(req,timeout=90) as response:return response.read()
 latest=json.loads(public('https://api.github.com/repos/'+repo+'/releases/latest'))
 assert latest['tag_name']==tag and not latest['draft'] and not latest['prerelease']
 for local in assets:
  asset=next(x for x in latest['assets'] if x['name']==local.name)
  data=public(asset['browser_download_url'])
  assert len(data)==local.stat().st_size==asset['size'] and hashlib.sha256(data).hexdigest()==sha(local)
 receipt.update(publicApiVerified=True,release=latest['html_url'],exeUrl=next(x['browser_download_url'] for x in latest['assets'] if x['name']=='EXPCdelete.exe'))
 (E/'release-receipt.json').write_text(json.dumps(receipt,indent=2))
print(json.dumps(receipt,indent=2))
