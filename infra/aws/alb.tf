# One public origin. Paths decide the service, so a single domain and certificate cover all three:
#   /mcp and /agent/*             -> API
#   /a2a and the A2A agent card   -> agent runtime
#   everything else               -> web app (its own /api routes proxy to the API, and it forwards
#                                    the Gmail OAuth callback signed as the user who started it)
resource "aws_lb" "main" {
  name               = var.name
  load_balancer_type = "application"
  security_groups    = [aws_security_group.alb.id]
  subnets            = aws_subnet.public[*].id
}

locals {
  target_ports  = { web = 3000, api = 8080, "agent-runtime" = 8123 }
  health_checks = { web = "/", api = "/health/ready", "agent-runtime" = "/health/ready" }
}

resource "aws_lb_target_group" "service" {
  for_each    = local.target_ports
  name        = "${var.name}-${each.key == "agent-runtime" ? "agent" : each.key}"
  port        = each.value
  protocol    = "HTTP"
  target_type = "ip"
  vpc_id      = aws_vpc.main.id

  health_check {
    path    = local.health_checks[each.key]
    matcher = "200-399"
  }

  deregistration_delay = 15
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.main.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type = "redirect"

    redirect {
      port        = "443"
      protocol    = "HTTPS"
      status_code = "HTTP_301"
    }
  }
}

resource "aws_lb_listener" "https" {
  load_balancer_arn = aws_lb.main.arn
  port              = 443
  protocol          = "HTTPS"
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"
  certificate_arn   = var.certificate_arn

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.service["web"].arn
  }
}

resource "aws_lb_listener_rule" "api" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 10

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.service["api"].arn
  }

  condition {
    path_pattern {
      values = ["/mcp", "/mcp/*", "/agent/*"]
    }
  }
}

resource "aws_lb_listener_rule" "agent" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 20

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.service["agent-runtime"].arn
  }

  condition {
    path_pattern {
      values = ["/a2a", "/a2a/*", "/.well-known/agent-card.json"]
    }
  }
}
