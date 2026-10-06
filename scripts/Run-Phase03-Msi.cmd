@echo off
if /i not "%USERNAME%"=="WDAGUtilityAccount" exit /b 99
if not exist C:\EXPC-Test\ALLOW-SANDBOX-TESTS exit /b 99
mkdir C:\EXPC-MsiRun
xcopy C:\EXPC-Payload\* C:\EXPC-MsiRun\ /E /I /Y > C:\EXPC-Results\msi-copy.txt
C:\EXPC-MsiRun\ExpcSandboxTests.exe msi-only > C:\EXPC-Results\msi-console.txt 2>&1
echo %ERRORLEVEL% > C:\EXPC-Results\msi-exit.txt
