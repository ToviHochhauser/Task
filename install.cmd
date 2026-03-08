@echo off
echo ============================================
echo   TimeClock System - Setup
echo ============================================
echo.

echo [1/4] Restoring backend NuGet packages...
cd /d "%~dp0backend"
dotnet restore
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Backend restore failed.
    pause
    exit /b 1
)

echo.
echo [2/4] Applying database migrations...
dotnet ef database update
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Database migration failed.
    echo Make sure SQL Server is running and the connection string in appsettings.json is correct.
    pause
    exit /b 1
)

echo.
echo [3/4] Installing frontend dependencies...
cd /d "%~dp0frontend"
call npm install --registry https://registry.npmjs.org
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Frontend install failed.
    pause
    exit /b 1
)

echo.
echo [4/4] Setup complete!
echo.
echo To run the application:
echo   1. Open a terminal in the 'backend' folder and run:  dotnet run
echo   2. Open a terminal in the 'frontend' folder and run: npm run dev
echo.
echo Default admin credentials:
echo   Username: admin
echo   Password: admin123
echo.
pause
