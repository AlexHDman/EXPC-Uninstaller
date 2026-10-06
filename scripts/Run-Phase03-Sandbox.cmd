@echo off
if /i not "%USERNAME%"=="WDAGUtilityAccount" exit /b 99
if not exist C:\EXPC-Test\ALLOW-SANDBOX-TESTS exit /b 99
mkdir C:\EXPC-Run
xcopy C:\EXPC-Payload\* C:\EXPC-Run\ /E /I /Y > C:\EXPC-Results\copy.txt
C:\EXPC-Run\ExpcSandboxTests.exe > C:\EXPC-Results\console.txt 2>&1
echo %ERRORLEVEL% > C:\EXPC-Results\exit-code.txt
start C:\EXPC-Run\EXPC-Uninstaller.exe
