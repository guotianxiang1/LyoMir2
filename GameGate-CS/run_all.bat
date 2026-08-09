@echo off
chcp 65001 >nul
echo ========================================
echo    GameGate All Services Launcher
echo ========================================
echo.

rem Kill existing processes
taskkill /F /IM DBSvr.exe 2>nul
taskkill /F /IM LoginGate.exe 2>nul
taskkill /F /IM GameGate.exe 2>nul
timeout /t 2 /nobreak >nul

rem Step 1: DBSvr
echo [1/3] Starting DBServer (DBSvr v3.0)...
start "DBServer" /D "D:\战神迁移服务端\loy2版\mud2.0\DBServer" DBSvr.exe
if %errorlevel% neq 0 (
    echo   FAILED to start DBSvr!
) else (
    echo   DBSvr launched OK
)
timeout /t 5 /nobreak >nul

rem Step 2: LoginGate
echo [2/3] Starting LoginGate...
start "LoginGate" /D "D:\战神迁移服务端\loy2版\mud2.0\GateServer\logingate" LoginGate.exe
if %errorlevel% neq 0 (
    echo   FAILED to start LoginGate!
) else (
    echo   LoginGate launched OK
)
timeout /t 3 /nobreak >nul

rem Step 3: GameGate
echo [3/3] Starting GameGate...
start "GameGate" /D "D:\战神迁移服务端\loy2版\mud2.0\GateServer\GameGate" GameGate.exe
if %errorlevel% neq 0 (
    echo   FAILED to start GameGate!
) else (
    echo   GameGate launched OK
)

rem Wait and verify
echo.
echo Waiting for services to initialize...
timeout /t 8 /nobreak >nul

echo.
echo ======== Port Status ========
netstat -ano | findstr /C:":5100 " /C:":5600 " /C:":6000 " /C:":7000 " /C:":7100 "
echo.
echo ======== Process Status ========
tasklist | findstr /I "DBSvr LoginGate GameGate"
echo.
echo ======== Done ========
echo.
pause
