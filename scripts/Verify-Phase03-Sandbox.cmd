@echo off
if /i not "%USERNAME%"=="WDAGUtilityAccount" exit /b 99
if not exist C:\EXPC-Test\ALLOW-SANDBOX-TESTS exit /b 99
mkdir C:\EXPC-FinalRun
xcopy C:\EXPC-Payload\* C:\EXPC-FinalRun\ /E /I /Y > C:\EXPC-Results\final-copy.txt
C:\EXPC-FinalRun\ExpcSandboxTests.exe > C:\EXPC-Results\final-console.txt 2>&1
echo %ERRORLEVEL% > C:\EXPC-Results\final-exit.txt
copy C:\EXPC-Results\results.json C:\EXPC-Results\results-final.json
xcopy C:\EXPC-FinalRun\local-data\* C:\EXPC-Results\final-local-data\ /E /I /Y > C:\EXPC-Results\final-copy-log.txt
