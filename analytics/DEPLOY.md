# Deploy analytics API (same GitHub repo → Azure Container Apps)

Frontend stays on **GitHub Pages**. Only the `analytics/` API runs on Azure.

Infra is defined with **Terraform** under `infra/`. GitHub Actions builds the image and runs `terraform apply`.

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
| `AZURE_RESOURCE_GROUP` | `rg-joaosousa-analytics-ne` |
| `AZURE_CONTAINER_APP` | `ca-joaosousa-analytics` |
| `AZURE_LOCATION` | `northeurope` |

### 4. Terraform remote state (needed for CI)

GitHub Actions runners are ephemeral. Without remote state, every run thinks nothing exists and tries to create the RG again.

One-time bootstrap (change the storage account name if it is taken — must be globally unique):

```bash
az group create --name rg-joaosousa-tfstate --location northeurope
az storage account create \
  --name stjoaosousatfstate \
  --resource-group rg-joaosousa-tfstate \
  --location northeurope \
  --sku Standard_LRS
az storage container create \
  --name tfstate \
  --account-name stjoaosousatfstate
```

Then either:

- Commit `infra/backend.azurerm.tf` (copy from `backend.azurerm.tf.example`), **or**
- Leave only the example file — the workflow copies it automatically on CI.

If you renamed the storage account, edit `backend.azurerm.tf.example` to match.

### 5. Local Terraform (learning loop)

Install [Terraform](https://developer.hashicorp.com/terraform/install) (≥ 1.5).

```bash
cd infra
cp terraform.tfvars.example terraform.tfvars
# Edit terraform.tfvars: image + analytics_api_key + analytics_ip_salt

az login
terraform init
terraform plan
terraform apply
terraform output url
```

If Azure already has the RG / env / app from the old `az` scripts, **import** them once instead of recreating:

```bash
cd infra
terraform init
terraform import azurerm_resource_group.analytics /subscriptions/<SUB_ID>/resourceGroups/rg-joaosousa-analytics-ne
terraform import azurerm_container_app_environment.analytics /subscriptions/<SUB_ID>/resourceGroups/rg-joaosousa-analytics-ne/providers/Microsoft.App/managedEnvironments/ca-joaosousa-analytics-env
terraform import azurerm_container_app.analytics /subscriptions/<SUB_ID>/resourceGroups/rg-joaosousa-analytics-ne/providers/Microsoft.App/containerApps/ca-joaosousa-analytics
terraform plan   # should show small or no changes
```

### 6. Run the workflow
- Push to `main`, or  
- **Actions** → **Deploy analytics API** → **Run workflow**

Keep **GitHub → Packages → `joao-sousa-analytics` → Public** so Azure can pull without a PAT.

The job summary prints the public URL.

### 7. Point the website at the API
In `index.html` add (production):

```html
<meta name="analytics-endpoint" content="https://YOUR-APP.xxxx.azurecontainerapps.io" />
```

Commit + push that change (Pages only). Until this meta exists on production, tracking stays off on `joaosousadev.me` (localhost still uses `http://localhost:5095` automatically).

### 8. Read stats

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
Container Apps with **min replicas = 0** (configured in Terraform) scales to zero when idle → usually **€0–2/month** for a personal site. First cold start after idle can take a few seconds.

The tfstate storage account is cheap (cents/month) when nearly empty.

## Same repo layout
```
joao-sousa-dev/
  index.html                 → GitHub Pages
  analytics/                 → Docker image
  infra/                     → Terraform (RG, env, Container App)
  .github/workflows/deploy-analytics.yml
```
