# Khadra

Car rental marketplace for Jordan. .NET 10 modular monolith (DDD + CQRS) with a BFF-protected Angular 22 dashboard.

```text
Browser ──cookie + XSRF──▶ Khadra.Bff ──bearer JWT──▶ Khadra.WebAPI ──EF Core──▶ PostgreSQL
                              │ Redis (encrypted sessions, key ring)
Flutter app ──────────────bearer JWT─────────────────▶ Khadra.WebAPI
```

## Prerequisites

- .NET SDK 10.0.400 (`global.json`), Node 24 + npm 11, Docker Desktop
- `dotnet tool restore` (installs `dotnet-ef`), `npm i -g @angular/cli@22`

## First run

```powershell
copy .env.example .env            # set POSTGRES_PASSWORD
docker compose up -d              # postgres, redis, mailpit (http://localhost:8025)

dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=khadra;Username=khadra;Password=<POSTGRES_PASSWORD>" --project Khadra.WebAPI
dotnet user-secrets set "Authentication:Jwt:SigningKey" "<at least 32 random bytes>" --project Khadra.WebAPI
dotnet user-secrets set "BffSecurity:RedisConnection" "localhost:6379" --project Khadra.Bff
dotnet dev-certs https --trust

dotnet build Khadra.slnx
dotnet test Khadra.slnx
dotnet run --project Khadra.WebAPI --launch-profile https   # https://localhost:7012  (migrations auto-apply in Development)
dotnet run --project Khadra.Bff --launch-profile https      # https://localhost:7243
cd Khadra.Dashboard; npm start                               # http://localhost:4200 → proxies /bff and /api to the BFF
```

API reference (Development only): https://localhost:7012/scalar/v1

## Auth endpoints (`/api/v1/auth`)

| Method | Route | Auth | Success | Notes |
|---|---|---|---|---|
| POST | `register` | anon | 201 `{userId,email}` | Customer role; sends verification email |
| POST | `login` | anon | 200 tokens | 403 `auth.email_not_verified` until verified |
| POST | `refresh` | anon | 200 tokens | rotates; replay revokes the family |
| POST | `logout` | bearer | 204 | `{refreshToken, allDevices}` |
| POST | `verify-email` | anon | 200 | `{token}` from the email link |
| POST | `resend-verification` | anon | 202 | always 202 |
| POST | `forgot-password` | anon | 202 | always 202 |
| POST | `reset-password` | anon | 200 | `{token,newPassword}`; revokes sessions |
| POST | `change-password` | bearer | 200 tokens | revokes other sessions |
| GET | `me` | bearer | 200 user | |

Errors are RFC 9457 ProblemDetails with a stable `code` (e.g. `auth.invalid_credentials`) and `traceId`. See `Khadra.WebAPI/Khadra.WebAPI.http` for ready-made requests.

## Repository rules

Read `CLAUDE.md` and `.claude/rules/` before contributing. Business numbers live in configuration (`BusinessRules`), never in code.
