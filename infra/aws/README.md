# careeroc on AWS

OpenTofu (or Terraform 1.6+) for the whole stack on ECS Fargate:

- **Network**: a VPC with two public subnets for the load balancer and tasks and two private subnets for Postgres.
  Tasks get public IPs instead of a NAT gateway, which saves about 33 USD a month; their security group still accepts traffic only from the load balancer and from each other.
- **Compute**: three Fargate services on ARM64 (Graviton): `web`, `api`, and `agent-runtime`.
  ECS Service Connect gives them the same short names as `docker-compose.yml` (`http://api:8080`, `http://agent-runtime:8123`).
- **Entry**: one Application Load Balancer and one domain.
  `/mcp` and `/agent/*` go to the API; `/a2a` and the A2A agent card go to the agent runtime; everything else, including the Gmail OAuth callback, goes to the web app.
- **Data**: RDS for PostgreSQL 17, encrypted, private, with 7-day backups and deletion protection.
  The agent runtime never receives `DATABASE_URL`; to keep its checkpoints across restarts, create a database and role for them with [agent-checkpoints.sql](../../deploy/postgres/agent-checkpoints.sql) and set `agent_checkpoint_database_url`, or leave it empty to keep them in memory.
- **Secrets**: generated at apply time and stored in Secrets Manager; ECS injects them, so they never appear in task definitions.

App Runner is not an option: it [closed to new customers on 2026-04-30](https://docs.aws.amazon.com/apprunner/latest/dg/apprunner-availability-change.html).
[ECS Express Mode](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-overview.html), its recommended replacement, provisions one service at a time with its own URL; careeroc needs three services behind one origin with private service-to-service names, so this uses standard ECS on Fargate with an Application Load Balancer and Service Connect.

## Deploy

1. Request an ACM certificate for your domain in the target region.
2. `cp terraform.tfvars.example terraform.tfvars` and fill it in.
3. Create the registries first, then push images for ARM64:

   ```bash
   tofu init
   tofu apply -target=aws_ecr_repository.service
   aws ecr get-login-password | docker login --username AWS --password-stdin "$(tofu output -json ecr_repositories | jq -r '.api' | cut -d/ -f1)"
   for service in web api agent-runtime; do
     repo=$(tofu output -json ecr_repositories | jq -r --arg s "$service" '.[$s]')
     case $service in
       web) docker buildx build --platform linux/arm64 -f apps/web/Dockerfile -t "$repo:$TAG" --push ../.. ;;
       api) docker buildx build --platform linux/arm64 -f apps/api/Dockerfile -t "$repo:$TAG" --push ../.. ;;
       agent-runtime) docker buildx build --platform linux/arm64 -t "$repo:$TAG" --push ../../services/agent-runtime ;;
     esac
   done
   ```

4. `tofu apply`, then point your domain at `load_balancer_dns_name`.

## Cost at the defaults

Roughly 60 to 70 USD a month in us-west-2: three small Fargate tasks, an ALB, and a `db.t4g.micro` instance.
Scale with `desired_count` and `db_instance_class`.

This configuration is validated with `tofu validate`; it has not been applied to a live AWS account by this project.
