@echo off
if /i not "%USERNAME%"=="WDAGUtilityAccount" exit /b 99
tasklist > C:\EXPC-Results\processes.txt
xcopy C:\EXPC-Run\local-data\* C:\EXPC-Results\local-data\ /E /I /Y > C:\EXPC-Results\copy-log.txt
copy C:\EXPC-Run\startup-metrics.jsonl C:\EXPC-Results\startup-metrics.jsonl
