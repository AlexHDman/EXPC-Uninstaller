"""Read PE resources as data; never execute the candidate or any embedded program."""
import ctypes as c
import hashlib, json, re, struct, sys
from pathlib import Path

root = Path(sys.argv[1]).resolve()
exe = root / 'EXPC-Uninstaller-Compact.exe'
expected = json.loads((root/'B-payload/manifest.json').read_text())
k = c.WinDLL('kernel32', use_last_error=True)
k.LoadLibraryExW.argtypes = [c.c_wchar_p,c.c_void_p,c.c_uint]; k.LoadLibraryExW.restype = c.c_void_p
k.FindResourceW.argtypes = [c.c_void_p,c.c_void_p,c.c_void_p]; k.FindResourceW.restype = c.c_void_p
k.SizeofResource.argtypes = [c.c_void_p,c.c_void_p]; k.SizeofResource.restype = c.c_uint
k.LoadResource.argtypes = [c.c_void_p,c.c_void_p]; k.LoadResource.restype = c.c_void_p
k.LockResource.argtypes = [c.c_void_p]; k.LockResource.restype = c.c_void_p
k.FreeLibrary.argtypes = [c.c_void_p]
# LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE | LOAD_LIBRARY_AS_IMAGE_RESOURCE; DllMain is not called.
module = k.LoadLibraryExW(str(exe),None,0x60)
assert module, c.get_last_error()
try:
    resource = k.FindResourceW(module,101,10); assert resource
    size = k.SizeofResource(module,resource)
    address = k.LockResource(k.LoadResource(module,resource)); assert address
    payload = c.string_at(address,size)
finally:
    k.FreeLibrary(module)
assert hashlib.sha256(payload).hexdigest() == expected['sha256']
length, = struct.unpack_from('<Q',payload)
api = c.WinDLL('cabinet',use_last_error=True)
api.CreateDecompressor.argtypes=[c.c_uint,c.c_void_p,c.POINTER(c.c_void_p)]
api.Decompress.argtypes=[c.c_void_p,c.c_void_p,c.c_size_t,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)]
api.CloseDecompressor.argtypes=[c.c_void_p]
handle=c.c_void_p(); assert api.CreateDecompressor(5,None,c.byref(handle))
output=c.create_string_buffer(length); written=c.c_size_t(); packed=c.create_string_buffer(payload[8:])
try: assert api.Decompress(handle,packed,len(payload)-8,output,length,c.byref(written)) and written.value==length
finally: api.CloseDecompressor(handle)
raw=output.raw; offset=0
def take(n):
    global offset
    assert offset+n<=len(raw)
    value=raw[offset:offset+n];offset+=n;return value
def number(fmt): return struct.unpack(fmt,take(struct.calcsize(fmt)))[0]
count=number('<I'); contents={}; files={}
for _ in range(count):
    name=take(number('<I')).decode('utf-8'); size=number('<Q'); digest=take(32).hex(); unique=number('<B')
    assert name not in files and not re.search(r'(^|/)\.\.?(/|$)|^/|[:\\]',name), name
    assert not re.search(r'(^|/)(local-data|tests?|fixtures?|registry-backups)(/|$)|\.(expc-vault|vault|trx|log|jsonl|pdb)$|(^|/)(settings|sources|versions|ignored-updates)\.json$',name,re.I),name
    data=take(size) if unique else contents[digest]
    assert len(data)==size and hashlib.sha256(data).hexdigest()==digest,name
    assert b'-----BEGIN PRIVATE KEY-----' not in data,name
    contents[digest]=data;files[name]=dict(path=name,bytes=size,sha256=digest)
assert offset==len(raw) and list(files.values())==expected['files']
for required in ['EXPC-Uninstaller.dll','EXPC-Uninstaller.runtimeconfig.json','Expc.Native.dll','NOTICE','Licence.txt','package-probe/Expc.PackageProbe.exe']:
    assert required in files,required
assert 'coreclr.dll' not in files and 'hostfxr.dll' not in files
report=dict(exe=str(exe),bytes=exe.stat().st_size,MiB=round(exe.stat().st_size/1048576,4),
            sha256=hashlib.sha256(exe.read_bytes()).hexdigest(),files=count,payloadIntegrity='PASS',
            personalDataMarkers='not found',mode='PE resource read only; no execution',GUI='NOT TESTED')
(root/'inspection.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
