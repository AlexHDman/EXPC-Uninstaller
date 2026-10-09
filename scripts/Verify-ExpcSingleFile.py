"""Verify final EXE payloads without starting EXPC, installing dependencies or showing UI."""
import hashlib, json, re, subprocess, sys, time
from pathlib import Path

root = Path(sys.argv[1]).resolve()
cache = root / 'verification-cache'
report = []
def run(exe, target):
    start = time.perf_counter()
    result = subprocess.run([str(exe), '--verify-payload', str(target)], timeout=120,
                            creationflags=subprocess.CREATE_NO_WINDOW)
    return result.returncode, round((time.perf_counter()-start)*1000)

for variant, name in [('A', 'SelfContained'), ('B', 'Compact')]:
    exe = root / ('EXPC-Uninstaller-' + name + '.exe')
    manifest = json.loads((root / (variant+'-payload') / 'manifest.json').read_text())
    timings = []
    for _ in range(2):
        code, ms = run(exe, cache)
        assert code == 0, (variant, code)
        timings.append(ms)
    extracted = cache / manifest['sha256']
    names = {entry['path'] for entry in manifest['files']}
    for required in ['EXPC-Uninstaller.dll', 'Expc.Native.dll', 'Licence.txt', 'NOTICE',
                     'package-probe/Expc.PackageProbe.exe']:
        assert required in names, required
    assert ('coreclr.dll' in names) == (variant == 'A')
    for entry in manifest['files']:
        path = entry['path']
        assert not re.search(r'(^|/)(local-data|tests?|fixtures?|registry-backups)(/|$)|\.(expc-vault|vault|trx|log|jsonl|pdb)$|(^|/)(settings|sources|versions|ignored-updates)\.json$', path, re.I), path
        data = (extracted / path).read_bytes()
        assert len(data) == entry['bytes'] and hashlib.sha256(data).hexdigest() == entry['sha256'], path
        assert b'-----BEGIN PRIVATE KEY-----' not in data, path
    # Tamper only with a disposable extracted fixture, then restore it exactly.
    fixture = extracted / 'NOTICE'
    original = fixture.read_bytes()
    try:
        fixture.write_bytes(original + b'\nfixture tamper\n')
        assert run(exe, cache)[0] == 1, 'Modified cache must fail closed'
    finally:
        fixture.write_bytes(original)
    report.append(dict(variant=variant, exe=str(exe), bytes=exe.stat().st_size,
                       MiB=round(exe.stat().st_size/1048576, 4),
                       sha256=hashlib.sha256(exe.read_bytes()).hexdigest(),
                       files=len(names), extractionOnlyMs=timings, tamperRejected=True))

compact = root / 'EXPC-Uninstaller-Compact.exe'
children = [subprocess.Popen([str(compact), '--verify-payload', str(root/'parallel-verification')],
                            creationflags=subprocess.CREATE_NO_WINDOW) for _ in range(2)]
assert all(p.wait(timeout=120) == 0 for p in children)
output = dict(artifacts=report, parallelExtraction='2/2 PASS', GUI='NOT TESTED',
              windowAndListStartup='NOT MEASURED')
(root/'verification.json').write_text(json.dumps(output, indent=2), encoding='utf-8')
print(json.dumps(output, indent=2))
