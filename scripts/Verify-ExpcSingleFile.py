"""Compatibility name: static payload verification only; never start an EXE."""
from pathlib import Path
import subprocess,sys
root=Path(__file__).resolve().parent.parent
stage=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else root/'build/staging'
raise SystemExit(subprocess.run([sys.executable,str(root/'scripts/Inspect-ExpcPayload.py'),str(stage),'EXPCdelete.exe'],timeout=120).returncode)
