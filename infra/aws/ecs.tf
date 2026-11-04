resource "aws_ecs_cluster" "main" {
  name = var.name

  setting {
    name  = "containerInsights"
    value = "enabled"
  }
}

# Service Connect gives every task the short names the apps already use: http://api:8080 and
# http://agent-runtime:8123, the same as in docker-compose.yml.
resource "aws_service_discovery_http_namespace" "main" {
  name = var.name
}

resource "aws_cloudwatch_log_group" "service" {
  for_each          = toset(local.services)
  name              = "/ecs/${var.name}/${each.key}"
  retention_in_days = 30
}

data "aws_iam_policy_document" "ecs_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "execution" {
  name               = "${var.name}-execution"
  assume_role_policy = data.aws_iam_policy_document.ecs_assume.json
}

resource "aws_iam_role_policy_attachment" "execution" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role_policy" "read_app_secret" {
  name = "read-app-secret"
  role = aws_iam_role.execution.id
  policy = jsonencode({
    Version   = "2012-10-17"
    Statement = [{ Effect = "Allow", Action = ["secretsmanager:GetSecretValue"], Resource = [local.secret] }]
  })
}

locals {
  public_url = "https://${var.domain_name}"

  # name => settings for each container; secrets map env name => key in the app secret.
  containers = {
    api = {
      cpu    = 512
      memory = 1024
      env = {
        PORT                                = "8080"
        ASPNETCORE_ENVIRONMENT              = "Production"
        ASPNETCORE_FORWARDEDHEADERS_ENABLED = "true"
        Database__ApplyMigrationsOnStartup  = "true"
        API_PUBLIC_BASE_URL                 = local.public_url
        NEXT_PUBLIC_SITE_URL                = local.public_url
        ACCESS_ALLOWED_DOMAINS              = var.demo_mode ? "demo.careeroc.example" : join(",", var.allowed_email_domains)
        Demo__Enabled                       = tostring(var.demo_mode)
        AgentRuntime__Enabled               = "true"
        AgentRuntime__BaseUrl               = "http://agent-runtime:8123"
        SyncWorker__Enabled                 = tostring(!var.demo_mode)
      }
      secrets = {
        DATABASE_URL                  = "DATABASE_URL"
        CAREEROC_API_AUTH_SECRET      = "CAREEROC_API_AUTH_SECRET"
        CAREEROC_TOKEN_PROTECTION_KEY = "CAREEROC_TOKEN_PROTECTION_KEY"
        AgentRuntime__SharedSecret    = "AGENT_SHARED_SECRET"
        Google__ClientId              = "GOOGLE_CLIENT_ID"
        Google__ClientSecret          = "GOOGLE_CLIENT_SECRET"
      }
    }
    "agent-runtime" = {
      cpu    = 256
      memory = 512
      env = {
        PORT                        = "8123"
        CAREEROC_AGENT_PROVIDER     = var.agent_provider
        CAREEROC_AGENT_API_BASE_URL = "http://api:8080"
        CAREEROC_AGENT_PUBLIC_URL   = local.public_url
      }
      # The runtime never gets DATABASE_URL; checkpoints use their own database when one is given.
      secrets = merge(
        { CAREEROC_AGENT_SHARED_SECRET = "AGENT_SHARED_SECRET" },
        var.agent_checkpoint_database_url == "" ? {} : { CAREEROC_AGENT_CHECKPOINT_DATABASE_URL = "AGENT_CHECKPOINT_DATABASE_URL" },
      )
    }
    web = {
      cpu    = 256
      memory = 512
      env = {
        PORT                   = "3000"
        DEMO_MODE              = tostring(var.demo_mode)
        AUTH_URL               = local.public_url
        NEXT_PUBLIC_SITE_URL   = local.public_url
        API_BASE_URL           = "http://api:8080"
        API_PUBLIC_BASE_URL    = local.public_url
        AGENT_PUBLIC_BASE_URL  = local.public_url
        ACCESS_ALLOWED_DOMAINS = join(",", var.allowed_email_domains)
      }
      secrets = {
        AUTH_SECRET              = "AUTH_SECRET"
        CAREEROC_API_AUTH_SECRET = "CAREEROC_API_AUTH_SECRET"
        AUTH_GOOGLE_ID           = "GOOGLE_CLIENT_ID"
        AUTH_GOOGLE_SECRET       = "GOOGLE_CLIENT_SECRET"
      }
    }
  }
}

resource "aws_ecs_task_definition" "service" {
  for_each                 = local.containers
  family                   = "${var.name}-${each.key}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = each.value.cpu
  memory                   = each.value.memory
  execution_role_arn       = aws_iam_role.execution.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "ARM64"
  }

  container_definitions = jsonencode([{
    name         = each.key
    image        = "${aws_ecr_repository.service[each.key].repository_url}:${var.image_tag}"
    essential    = true
    portMappings = [{ name = "http", containerPort = local.target_ports[each.key], protocol = "tcp" }]
    environment  = [for key, value in each.value.env : { name = key, value = value }]
    secrets      = [for key, field in each.value.secrets : { name = key, valueFrom = "${local.secret}:${field}::" }]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.service[each.key].name
        awslogs-region        = var.region
        awslogs-stream-prefix = each.key
      }
    }
  }])
}

resource "aws_ecs_service" "service" {
  for_each               = local.containers
  name                   = each.key
  cluster                = aws_ecs_cluster.main.id
  task_definition        = aws_ecs_task_definition.service[each.key].arn
  desired_count          = var.desired_count
  launch_type            = "FARGATE"
  enable_execute_command = false

  # The API applies migrations on start; give it time before health checks count.
  health_check_grace_period_seconds = 120

  network_configuration {
    subnets          = aws_subnet.public[*].id
    security_groups  = [aws_security_group.tasks.id]
    assign_public_ip = true
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.service[each.key].arn
    container_name   = each.key
    container_port   = local.target_ports[each.key]
  }

  service_connect_configuration {
    enabled   = true
    namespace = aws_service_discovery_http_namespace.main.arn

    dynamic "service" {
      for_each = each.key == "web" ? [] : [each.key]
      content {
        port_name      = "http"
        discovery_name = each.key
        client_alias {
          dns_name = each.key
          port     = local.target_ports[each.key]
        }
      }
    }
  }

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  depends_on = [aws_lb_listener.https]
}
