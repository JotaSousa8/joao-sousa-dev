resource "azurerm_resource_group" "analytics" {
  name     = var.resource_group_name
  location = var.location
}

# No Log Analytics workspace — same idea as `az containerapp env create --logs-destination none`
# (trial subscriptions often cannot create new LAW in some regions).
resource "azurerm_container_app_environment" "analytics" {
  name                = "${var.container_app_name}-env"
  location            = azurerm_resource_group.analytics.location
  resource_group_name = azurerm_resource_group.analytics.name
}

resource "azurerm_container_app" "analytics" {
  name                         = var.container_app_name
  resource_group_name          = azurerm_resource_group.analytics.name
  container_app_environment_id = azurerm_container_app_environment.analytics.id
  revision_mode                = "Single"

  # Public GHCR image — no registry block (avoids needing a PAT).
  # Package must stay Public on GitHub Packages.

  secret {
    name  = "analytics-api-key"
    value = var.analytics_api_key
  }

  secret {
    name  = "analytics-ip-salt"
    value = var.analytics_ip_salt
  }

  template {
    min_replicas = var.min_replicas
    max_replicas = var.max_replicas

    container {
      name   = "analytics"
      image  = var.image
      cpu    = var.cpu
      memory = var.memory

      env {
        name        = "Analytics__ApiKey"
        secret_name = "analytics-api-key"
      }

      env {
        name        = "Analytics__IpSalt"
        secret_name = "analytics-ip-salt"
      }

      env {
        name  = "Analytics__DbPath"
        value = "/data/analytics.db"
      }

      env {
        name  = "ASPNETCORE_URLS"
        value = "http://+:8080"
      }
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }
}
