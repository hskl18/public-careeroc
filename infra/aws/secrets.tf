# Generated once and stored in Secrets Manager; ECS injects them, so they never sit in task
# definitions or state outputs.
resource "random_password" "app" {
  for_each = toset(["api_signing", "token_protection", "agent_shared", "auth"])
  length   = 48
  special  = false
}

resource "aws_secretsmanager_secret" "app" {
  name                    = "${var.name}/app"
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "app" {
  secret_id = aws_secretsmanager_secret.app.id
  secret_string = jsonencode({
    DATABASE_URL                  = "postgresql://${aws_db_instance.main.username}:${random_password.database.result}@${aws_db_instance.main.address}:5432/${aws_db_instance.main.db_name}?sslmode=require"
    CAREEROC_API_AUTH_SECRET      = random_password.app["api_signing"].result
    CAREEROC_TOKEN_PROTECTION_KEY = random_password.app["token_protection"].result
    AGENT_SHARED_SECRET           = random_password.app["agent_shared"].result
    AUTH_SECRET                   = random_password.app["auth"].result
    GOOGLE_CLIENT_ID              = var.google_client_id
    GOOGLE_CLIENT_SECRET          = var.google_client_secret
    AGENT_CHECKPOINT_DATABASE_URL = var.agent_checkpoint_database_url
  })
}

locals {
  secret = aws_secretsmanager_secret.app.arn
}
