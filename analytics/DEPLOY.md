# Deploy analytics API (same GitHub repo → Azure Container Apps)

Frontend stays on **GitHub Pages**. Only the `analytics/` API runs on Azure.

## One-time Azure setup

### 1. Azure CLI login
```bash
az login
az account show
```

### 2. Create an App Registration for GitHub OIDC
```bash
# Replace SUBSCRIPTION_ID and keep the output client/tenant IDs
az ad app create --display-name "github-joao-sousa-analytics"
# Or use Azure Portal → App registrations → New → then Federated credentials
```

Easier path in Portal:

1. **Microsoft Entra ID** → **App registrations** → **New registration**  
   Name: `github-joao-sousa-analytics`
2. **Certificates & secrets** → **Federated credentials** → **GitHub Actions deploying Azure resources**
   - Org: `JotaSousa8`
   - Repo: `joao-sousa-dev`
   - Entity: `Branch` → `main`
3. **Subscriptions** → your sub → **Access control (IAM)** → add role **Contributor** to that app on a resource group (or subscription for first create).

### 3. GitHub repo secrets
Repo → **Settings** → **Secrets and variables** → **Actions**:

| Secret | Value |
|--------|--------|
| `AZURE_CLIENT_ID` | Application (client) ID |
| `AZURE_TENANT_ID` | Directory (tenant) ID |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID |
| `ANALYTICS_API_KEY` | Long random string (summary auth) |
| `ANALYTICS_IP_SALT` | Long random string (visitor hashing) |

Optional **Variables**:

| Variable | Default |
|----------|---------|
| `AZURE_RESOURCE_GROUP` | `rg-joaosousa-analytics` |
| `AZURE_CONTAINER_APP` | `ca-joaosousa-analytics` |
| `AZURE_LOCATION` | `northeurope` |

### 4. Run the workflow
- Push a change under `analytics/` to `main`, or  
- **Actions** → **Deploy analytics API** → **Run workflow**

After the first successful image push, open **GitHub → Packages → `joao-sousa-analytics` → Package settings → Change visibility → Public** so Azure can pull without a long-lived PAT.

The job summary prints the public URL, e.g.  
`https://ca-joaosousa-analytics.xxxx.westeurope.azurecontainerapps.io`

### 5. Point the website at the API
In `index.html` add (production):

```html
<meta name="analytics-endpoint" content="https://YOUR-APP.xxxx.azurecontainerapps.io" />
```

Commit + push that change (Pages only). Until this meta exists on production, tracking stays off on `joaosousadev.me` (localhost still uses `http://localhost:5095` automatically).

### 6. Read stats

**A) Site admin page** (no nav link — open directly):

`https://joaosousadev.me/#/admin`  
(or locally `http://localhost:5174/#/admin`)

Enter `ANALYTICS_API_KEY`. The key is stored only in your browser `localStorage`.

**B) Postman**

Import `analytics/postman/AnalyticsApi.postman_collection.json`.

Variables:
- `baseUrl` → `http://localhost:5095` or your Container Apps URL  
- `apiKey` → your `ANALYTICS_API_KEY`

**C) curl**
```bash
curl -H "X-Api-Key: YOUR_ANALYTICS_API_KEY" https://YOUR-APP.../api/analytics/summary
```

## Cost reminder
Container Apps with **min replicas = 0** (configured in the workflow) scales to zero when idle → usually **€0–2/month** for a personal site. First cold start after idle can take a few seconds.

## Same repo layout
```
joao-sousa-dev/
  index.html          → GitHub Pages
  analytics/          → Docker image → Azure Container Apps
  .github/workflows/deploy-analytics.yml
```
