output "resource_group_name" {
  value = azurerm_resource_group.analytics.name
}

output "container_app_name" {
  value = azurerm_container_app.analytics.name
}

output "fqdn" {
  description = "Public hostname of the Container App (no https://)."
  value       = azurerm_container_app.analytics.ingress[0].fqdn
}

output "url" {
  description = "Public HTTPS base URL."
  value       = "https://${azurerm_container_app.analytics.ingress[0].fqdn}"
}

output "health_url" {
  value = "https://${azurerm_container_app.analytics.ingress[0].fqdn}/health"
}
