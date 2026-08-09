@echo off
echo Starting DBSvr...
start "DBSvr" /D "D:\战神迁移服务端\loy2版\mud2.0\DBServer" DBSvr.exe
timeout /t 3 /nobreak >nul

echo Starting LoginGate...
start "LoginGate" /D "D:\战神迁移服务端\loy2版\mud2.0\GateServer\logingate" LoginGate.exe
timeout /t 3 /nobreak >nul

echo Starting GameGate...
start "GameGate" /D "D:\战神迁移服务端\loy2版\mud2.0\GateServer\GameGate" GameGate.exe
timeout /t 3 /nobreak >nul

echo.
echo === Port Status ===
timeout /t 5 /nobreak >nul
netstat -ano | findstr "5100 5600 6000 7000 7100"
echo.
echo All services started.
pause
