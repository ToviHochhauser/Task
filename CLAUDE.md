# TimeClock Project

## Project Structure
- `backend/` — ASP.NET Core 9 Web API (.NET 9 SDK)
- `frontend/` — React 19 + TypeScript + Vite 7
- `install.cmd` — One-click setup script
- `start.cmd` — Starts both servers

## Running
- Backend: `cd backend && dotnet run` → http://localhost:5000
- Frontend: `cd frontend && npm run dev` → http://localhost:5173
- Default admin: `admin` / `admin123`

## First-Time Dev Setup (after cloning)
The `Jwt:Key` is excluded from version control. Set it via dotnet user-secrets:
```
cd backend
dotnet user-secrets set "Jwt:Key" "your-secret-key-at-least-32-chars"
```
The `UserSecretsId` is already in `backend.csproj`. ASP.NET Core loads user-secrets automatically in the Development environment.

## Key Technical Details
- **npm registry**: User's global npm points to `registry.npmmirror.com` (broken). Frontend has `.npmrc` that overrides to `registry.npmjs.org`. Always use `--registry https://registry.npmjs.org` if installing outside the frontend folder.
- **Time API**: WorldTimeAPI is unreachable from this machine. TimeAPI.io (`https://timeapi.io/api/time/current/zone?timeZone=Europe/Zurich`) works. Fallback chain: WorldTimeAPI → TimeAPI.io → server-side TimeZoneInfo conversion.
- **SQL Server**: Local instance, Windows Auth. Connection: `Data Source=.;Initial Catalog=TimeClockDb;Integrated Security=True;TrustServerCertificate=True`
- **EF Core**: `dotnet-ef` tool is installed globally (v10). Use `dotnet-ef` (not `dotnet ef`) to run migrations.
- **NuGet packages**: Must pin to `9.0.*` versions — latest defaults to .NET 10 which is incompatible.

## Architecture
- Controllers → Services → EF Core DbContext → SQL Server
- JWT auth with roles (Admin/Employee)
- Global exception middleware maps exception types to HTTP status codes
- Frontend uses Axios with JWT interceptor, React Router, AuthContext
