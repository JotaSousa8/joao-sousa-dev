# Analytics API — visits tracker for joaosousadev.me

Minimal ASP.NET Core (.NET 10) API. Page views are stored in **Supabase Postgres** (persistent, free tier).

## Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| `POST` | `/api/analytics/pageview` | none (CORS + rate limit) | Record a visit |
| `GET` | `/api/analytics/summary` | header `X-Api-Key` | Totals, paths, recent events |
| `GET` | `/api/analytics/schema` | header `X-Api-Key` | Tables / columns |
| `POST` | `/api/analytics/query` | header `X-Api-Key` | Read-only SQL |
| `GET` | `/health` | none | Liveness |

Main table: `page_views`.

## Run locally

1. Create a free [Supabase](https://supabase.com) project and copy the Database URI.
2. Open `AnalyticsApi.sln` in Visual Studio, or set the connection string:

```bash
cd analytics
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Analytics" "postgresql://postgres....@....supabase.co:5432/postgres"
dotnet run --launch-profile http
```

Default URL: `http://localhost:5095`

On localhost the static site auto-targets `http://localhost:5095`. Production uses the `analytics-endpoint` meta tag.

Summary example:

```bash
curl -H "X-Api-Key: local-dev-key" http://localhost:5095/api/analytics/summary
```

## Azure / GitHub

See [DEPLOY.md](DEPLOY.md). Required secret: `ANALYTICS_CONNECTION_STRING` (Supabase URI).

## Cost

| Piece | Cost |
|--------|------|
| Supabase free tier | €0 (within free quotas) |
| Azure Container Apps `minReplicas=0` | usually €0–2/month idle |

Visits survive container restarts/deploys because Postgres lives in Supabase, not on the container disk.
