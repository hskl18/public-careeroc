output "load_balancer_dns_name" {
  description = "Point domain_name at this with a CNAME or alias record."
  value       = aws_lb.main.dns_name
}

output "public_url" {
  value = local.public_url
}

output "mcp_url" {
  value = "${local.public_url}/mcp"
}

output "agent_card_url" {
  value = "${local.public_url}/.well-known/agent-card.json"
}

output "ecr_repositories" {
  description = "Push the three images here with the tag in image_tag."
  value       = { for name, repo in aws_ecr_repository.service : name => repo.repository_url }
}
