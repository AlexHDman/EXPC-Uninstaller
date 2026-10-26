param()
$ErrorActionPreference='Stop'
& python -X utf8 "$PSScriptRoot\Build-EXPCdelete.py"
if ($LASTEXITCODE -ne 0) { throw "EXPCdelete build failed ($LASTEXITCODE); previous dist preserved" }
