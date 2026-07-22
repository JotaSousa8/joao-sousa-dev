terraform {
  required_version = ">= 1.5.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  # Default: local state (fine for learning on your machine).
  # For GitHub Actions, copy backend.azurerm.tf.example → backend.azurerm.tf
  # after creating the storage account (see DEPLOY.md).
}

provider "azurerm" {
  features {}
  # Auth: Azure CLI locally (`az login`), or OIDC in GitHub Actions
  # via ARM_CLIENT_ID / ARM_TENANT_ID / ARM_SUBSCRIPTION_ID / ARM_USE_OIDC.
}
