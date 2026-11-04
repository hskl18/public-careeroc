variable "name" {
  description = "Prefix for every resource."
  type        = string
  default     = "careeroc"
}

variable "region" {
  description = "AWS region."
  type        = string
  default     = "us-west-2"
}

variable "domain_name" {
  description = "Public hostname that points at the load balancer, for example careeroc.example.com."
  type        = string
}

variable "certificate_arn" {
  description = "ACM certificate for domain_name in the same region. The web app requires HTTPS in production."
  type        = string
}

variable "image_tag" {
  description = "Tag pushed to the three ECR repositories."
  type        = string
  default     = "latest"
}

variable "demo_mode" {
  description = "Run the public demo: one-click demo sign-in, fictional inboxes, no Google OAuth."
  type        = bool
  default     = true
}

variable "allowed_email_domains" {
  description = "Google accounts allowed to sign in when demo_mode is false. Ignored in demo mode."
  type        = list(string)
  default     = []
}

variable "google_client_id" {
  description = "Google OAuth client id. Required when demo_mode is false."
  type        = string
  default     = ""
}

variable "google_client_secret" {
  description = "Google OAuth client secret. Required when demo_mode is false."
  type        = string
  default     = ""
  sensitive   = true
}

variable "agent_checkpoint_database_url" {
  description = "Postgres URL for the agent runtime's checkpoints, for a role that cannot reach the careeroc database (see deploy/postgres/agent-checkpoints.sql). Empty keeps checkpoints in memory."
  type        = string
  default     = ""
  sensitive   = true
}

variable "agent_provider" {
  description = "Agent runtime model provider: deterministic, ollama, or openai."
  type        = string
  default     = "deterministic"
}

variable "desired_count" {
  description = "Tasks per service."
  type        = number
  default     = 1
}

variable "db_instance_class" {
  description = "RDS instance class."
  type        = string
  default     = "db.t4g.micro"
}
