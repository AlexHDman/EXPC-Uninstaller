param([switch]$CompactOnly)
# Compatibility entry point; one stable output, no per-fix directories.
& "$PSScriptRoot\Build-EXPCdelete.ps1"
exit $LASTEXITCODE
