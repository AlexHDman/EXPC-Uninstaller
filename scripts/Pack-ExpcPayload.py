# Build a single RCDATA payload using Windows LZMS (no executable packer).
import ctypes as c, hashlib, json, struct, sys, re
import xml.etree.ElementTree as ET
from pathlib import Path
source=Path(sys.argv[1]); out=Path(sys.argv[2]); out.mkdir(parents=True,exist_ok=True)
files=sorted(p for p in source.rglob('*') if p.is_file() and p.suffix.lower()!='.pdb')
blob=bytearray(struct.pack('<I',len(files))); manifest=[]; seen=set()
for file in files:
 name=file.relative_to(source).as_posix()
 if re.search(r'(^|/)(local-data|tests?|fixtures?|registry-backups)(/|$)|\.(vault|trx|log|jsonl)$|(^|/)(settings|sources|versions|ignored-updates)\.json$',name,re.I): raise ValueError('Private data: '+name)
 data=file.read_bytes(); digest=hashlib.sha256(data).digest(); encoded=name.encode('utf-8')
 blob+=struct.pack('<I',len(encoded))+encoded+struct.pack('<Q',len(data))+digest+struct.pack('<B',digest not in seen)
 if digest not in seen: blob+=data;seen.add(digest)
 manifest.append({'path':name,'bytes':len(data),'sha256':digest.hex()})
api=c.WinDLL('cabinet.dll',use_last_error=True); handle=c.c_void_p()
api.CreateCompressor.argtypes=[c.c_uint,c.c_void_p,c.POINTER(c.c_void_p)];api.Compress.argtypes=[c.c_void_p,c.c_void_p,c.c_size_t,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)];api.CloseCompressor.argtypes=[c.c_void_p]
api.SetCompressorInformation.argtypes=[c.c_void_p,c.c_int,c.c_void_p,c.c_size_t]
if not api.CreateCompressor(5,None,c.byref(handle)):raise c.WinError(c.get_last_error())
block_size=c.c_uint(64*1024*1024)
if not api.SetCompressorInformation(handle,1,c.byref(block_size),c.sizeof(block_size)):raise c.WinError(c.get_last_error())
raw=bytes(blob);size=c.c_size_t();buffer=c.create_string_buffer(raw)
api.Compress(handle,buffer,len(raw),None,0,c.byref(size));output=c.create_string_buffer(size.value)
if not api.Compress(handle,buffer,len(raw),output,len(output),c.byref(size)):raise c.WinError(c.get_last_error())
api.CloseCompressor(handle);payload=struct.pack('<Q',len(raw))+output.raw[:size.value];(out/'payload.bin').write_bytes(payload)
config=json.loads((source/'EXPC-Uninstaller.runtimeconfig.json').read_text(encoding='utf-8-sig'))['runtimeOptions']
frameworks=config.get('frameworks',config.get('includedFrameworks',[]))
version=next(f['version'] for f in frameworks if f['name']=='Microsoft.WindowsDesktop.App')
if not re.fullmatch(r'10\.0\.[0-9]+',version):raise ValueError('Expected stable Desktop 10.0 patch')
patch=int(version.split('.')[-1])
sha=hashlib.sha256(payload).hexdigest();(out/'payload.h').write_text('#pragma once\n#define RUNTIME_VERSION L"'+version+'"\n#define MIN_PATCH '+str(patch)+'\n#define PAYLOAD_SHA "'+sha+'"\n')
icon=(Path(__file__).resolve().parent.parent/'assets/icon.ico').as_posix();(out/'payload.rc').write_text('101 RCDATA "'+(out/'payload.bin').resolve().as_posix()+'"\n1 ICON "'+icon+'"\n')
# Native file/product versions match the managed application and release manifest.
app_version=ET.parse(Path(__file__).resolve().parent.parent/'EXPC.Version.props').findtext('.//Version')
assert re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+',app_version)
v=app_version.replace('.',',')+',0'
with (out/'payload.rc').open('a') as rc:
 rc.write('1 VERSIONINFO\nFILEVERSION '+v+'\nPRODUCTVERSION '+v+'\nFILEOS 0x40004\nFILETYPE 1\nBEGIN\n BLOCK "StringFileInfo"\n BEGIN\n BLOCK "040904B0"\n BEGIN\n VALUE "FileVersion", "'+app_version+'"\n VALUE "ProductVersion", "'+app_version+'"\n VALUE "ProductName", "EXPC Uninstaller"\n VALUE "OriginalFilename", "EXPCdelete.exe"\n END\n END\n BLOCK "VarFileInfo"\n BEGIN\n VALUE "Translation", 0x409, 1200\n END\nEND\n')
(out/'manifest.json').write_text(json.dumps({'rawBytes':len(raw),'compressedBytes':len(payload),'sha256':sha,'files':manifest},indent=2),encoding='utf-8'); print(json.dumps({'files':len(files),'rawBytes':len(raw),'compressedBytes':len(payload),'sha256':sha}),flush=True)
