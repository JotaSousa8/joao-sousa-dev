variable "location" {
  type        = string
  description = "Azure region for all resources."
  default     = "northeurope"
}

variable "resource_group_name" {
  type        = string
  description = "Resource group name."
  default     = "rg-joaosousa-analytics-ne"
}

variable "container_app_name" {
  type        = string
  description = "Container App name (environment becomes <name>-env)."
  default     = "ca-joaosousa-analytics"
}

variable "image" {
  type        = string
  description = "Full container image reference (e.g. ghcr.io/user/joao-sousa-analytics:abc1234)."
}

variable "analytics_api_key" {
  type        = string
  description = "API key for /api/analytics/summary."
  sensitive   = true
}

variable "analytics_ip_salt" {
  type        = string
  description = "Salt used to hash visitor IPs."
  sensitive   = true
}

variable "analytics_connection_string" {
  type        = string
  description = "Supabase / Postgres connection string."
  sensitive   = true
}

variable "cpu" {
  type        = number
  description = "vCPU for the container."
  default     = 0.25
}

variable "memory" {
  type        = string
  description = "Memory for the container (e.g. 0.5Gi)."
  default     = "0.5Gi"
}

variable "min_replicas" {
  type        = number
  description = "Minimum replicas (0 = scale to zero when idle)."
  default     = 0
}

variable "max_replicas" {
  type        = number
  description = "Maximum replicas."
  default     = 1
}
