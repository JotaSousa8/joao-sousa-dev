# Analytics API — local visits tracker for joaosousadev.me

Minimal ASP.NET Core (.NET 10) API that stores anonymised page views in SQLite.

## Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| `POST` | `/api/analytics/pageview` | none (CORS + rate limit) | Record a visit |
| `GET` | `/api/analytics/summary` | header `X-Api-Key` | Totals, paths, recent events |
| `GET` | `/health` | none | Liveness |

Stored fields: UTC timestamp, path, referrer, truncated user-agent, **daily visitor hash** (not raw IP), optional country header.

## Run locally

```bash
cd analytics
dotnet run --launch-profile http
```

Default URL: `http://localhost:5095`

In the static site, local tracking auto-targets `http://localhost:5095` when you open the site on localhost. Optionally override with:

```html
<meta name="analytics-endpoint" content="http://localhost:5095" />
```

For production, set that meta to your deployed API URL (until then, tracking stays off on joaosousadev.me).

Summary example:

```bash
curl -H "X-Api-Key: local-dev-key" http://localhost:5095/api/analytics/summary
```

## Azure costs (realistic for a portfolio)

| Option | Approx. cost | Notes |
|--------|----------------|-------|
| **Azure Container Apps** (consumption) | often **€0–2/mês** at low traffic | Pay per use; good default |
| **Azure Functions** Consumption | often **€0–1/mês** | Rewrite as functions if you prefer |
| **App Service Free F1** | **€0** | Sleeps, limited; OK for experiments only |
| **App Service B1** | ~**€10–15/mês** | Always-on, simpler |
| **SQLite on disk** | **€0** extra | Fine for low volume; use durable mount or move to Table Storage later |
| **Azure SQL** | usually **too expensive** for this | Skip for now |

**Recommendation:** Container Apps + SQLite (or free-tier Postgres elsewhere) keeps cost near zero for personal traffic. Set strong `Analytics__ApiKey` and `Analytics__IpSalt` as app settings. Never commit production secrets.

GitHub Pages stays free for the frontend; only the API is billed.

## Deploy from this repo

See **[DEPLOY.md](./DEPLOY.md)** — GitHub Actions builds `analytics/` and deploys to Azure Container Apps. Same repo; Pages unchanged.
