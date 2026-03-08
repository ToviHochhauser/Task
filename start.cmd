@echo off
echo ============================================
echo   TimeClock System - Starting...
echo ============================================
echo.

echo Starting backend (http://localhost:5000)...
start "TimeClock Backend" cmd /c "cd /d "%~dp0backend" && dotnet run"

echo Starting frontend (http://localhost:5173)...
start "TimeClock Frontend" cmd /c "cd /d "%~dp0frontend" && npm run dev"

echo.
echo Both servers are starting. Open http://localhost:5173 in your browser.
echo.
echo Default admin credentials:
echo   Username: admin
echo   Password: admin123
echo.
pause
