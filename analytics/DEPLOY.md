# Deploy analytics API (same GitHub repo → Azure Container Apps)

Frontend stays on **GitHub Pages**. Only the `analytics/` API runs on Azure.

Deploy today uses **Azure CLI** in GitHub Actions. Terraform files live under `infra/` for later (commented out in the workflow).

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
   - Entity: **Branch** → `main`
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
| `AZURE_RESOURCE_GROUP` | `rg-joaosousa-analytics-ne` |
| `AZURE_CONTAINER_APP` | `ca-joaosousa-analytics` |
| `AZURE_LOCATION` | `northeurope` |

### 4. Run the workflow
- Push to `main`, or  
- **Actions** → **Deploy analytics API** → **Run workflow**

Keep **GitHub → Packages → `joao-sousa-analytics` → Public** so Azure can pull without a long-lived PAT.

The job summary prints the public URL, e.g.  
`https://ca-joaosousa-analytics.xxxx.northeurope.azurecontainerapps.io`

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

Import the collection you need:
- Local: `analytics/postman/AnalyticsApi.postman_collection.json` (`localhost:5095` / `local-dev-key`)
- Production: `analytics/postman/AnalyticsApi.prod.postman_collection.json`

On the prod collection, open **Variables** and set `apiKey` to your `ANALYTICS_API_KEY` (leave blank in git).

**C) curl**
```bash
curl -H "X-Api-Key: YOUR_ANALYTICS_API_KEY" https://YOUR-APP.../api/analytics/summary
```

## Cost reminder
Container Apps with **min replicas = 0** (configured in the workflow) scales to zero when idle → usually **€0–2/month** for a personal site. First cold start after idle can take a few seconds.

## Same repo layout
```
joao-sousa-dev/
  index.html                 → GitHub Pages
  analytics/                 → Docker image → Azure Container Apps (az CLI today)
  infra/                     → Terraform (not wired in CI yet)
  .github/workflows/deploy-analytics.yml
```

## Terraform later

When you want to switch:

1. Create Azure remote state storage (see older notes / `infra/backend.azurerm.tf.example`).
2. Import existing RG / env / Container App into Terraform state.
3. In `.github/workflows/deploy-analytics.yml`: comment out the Azure CLI steps and uncomment the Terraform block.
