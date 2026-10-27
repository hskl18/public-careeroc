# Deploying careeroc

Every target runs the same three images with the same service names: `web` (port 3000), `api` (port 8080), and `agent-runtime` (port 8123), plus PostgreSQL 17.
Only `web` must be public.
Expose `api` too if assistants should reach `/mcp`, and `agent-runtime` if other agents should reach `/a2a`.

| Target | Where | How it is verified |
|---|---|---|
| Docker Compose | [docker-compose.yml](../../docker-compose.yml) | CI starts it and runs [scripts/smoke-demo.sh](../../scripts/smoke-demo.sh) |
| Kubernetes | [deploy/helm/careeroc](../../deploy/helm/careeroc/README.md) | CI installs it on kind and runs the same smoke test ([scripts/smoke-kubernetes.sh](../../scripts/smoke-kubernetes.sh)) |
| Railway | [.railway/railway.ts](../../.railway/railway.ts), steps below | Deployed once by hand; not in CI |
| AWS ECS Fargate | [infra/aws](../../infra/aws/README.md) | `tofu validate` in CI; never applied by the maintainers |
| Vercel (web only) | steps below | Not tested |

## Secrets

Generate each with `openssl rand -hex 32`, and never reuse the local fallbacks from `docker-compose.yml`.

| Secret | Used by | Purpose |
|---|---|---|
| `CAREEROC_API_AUTH_SECRET` | web, api | Signs every web-to-API request |
| `CAREEROC_TOKEN_PROTECTION_KEY` | api | Encrypts stored Gmail OAuth tokens |
| `AgentRuntime__SharedSecret` / `CAREEROC_AGENT_SHARED_SECRET` | api, agent-runtime | Authenticates the API to the runtime; required, at least 32 characters |
| `AUTH_SECRET` | web | Encrypts Auth.js sessions |

## Demo or real accounts

- **Demo**: set `DEMO_MODE=true` on web and `Demo__Enabled=true` on the API, and allow the demo domain with `ACCESS_ALLOWED_DOMAINS=demo.careeroc.example`.
  No Google client is needed.
- **Google sign-in and Gmail**: set `AUTH_GOOGLE_ID` and `AUTH_GOOGLE_SECRET` on web and `Google__ClientId` and `Google__ClientSecret` on the API, and set `ACCESS_ALLOWED_EMAILS` or `ACCESS_ALLOWED_DOMAINS`.
  Register `https://<web host>/api/inbox-accounts/gmail/oauth/callback` as the redirect URI and set `NEXT_PUBLIC_SITE_URL` on the API to the web URL.
  The callback goes through the web app so it reaches the API signed as the user who started the connection; without that URL, Gmail sign-in is refused.

## Agent checkpoints

The agent runtime never receives `DATABASE_URL`, so it cannot read careeroc's tables.
It stores LangGraph checkpoints in `CAREEROC_AGENT_CHECKPOINT_DATABASE_URL`, a separate database owned by its own role; [agent-checkpoints.sql](../../deploy/postgres/agent-checkpoints.sql) creates both.
Docker Compose runs that file on first start, and Railway gives the runtime a second Postgres service.
On Kubernetes, add `AGENT_CHECKPOINT_DATABASE_URL` to the secret; on AWS, set `agent_checkpoint_database_url`.
Without it, checkpoints live in memory: a review that waits for approval across a runtime restart still applies its decision, but its trace does not get the final human-review step.

Public URLs must use HTTPS in production.
Internal service URLs such as `http://api:8080` may use HTTP when the host is private.

## Railway

[.railway/railway.ts](../../.railway/railway.ts) declares two Postgres services (careeroc data and agent checkpoints) and the three services, and wires values between them with Railway references such as `${{api.CAREEROC_API_AUTH_SECRET}}`.
Secrets are marked `preserve()`, so you set them once and later applies keep them.

```bash
pnpm install
railway login
railway init --name careeroc-demo
railway config apply --yes

set_secret() { openssl rand -hex 32 | railway variable set --service "$1" --stdin --skip-deploys "$2"; }
set_secret api CAREEROC_API_AUTH_SECRET
set_secret api CAREEROC_TOKEN_PROTECTION_KEY
set_secret agent-runtime CAREEROC_AGENT_SHARED_SECRET
set_secret web AUTH_SECRET

for service in agent-runtime api web; do
  railway domain --service "$service"
  railway up --service "$service" --detach
done
```

Run `railway config plan` before each later `railway config apply` to preview changes.

## Vercel (web only)

Vercel can host `apps/web` while the API and runtime run on one of the other targets.

| Setting | Value |
|---|---|
| Framework preset | Next.js |
| Root directory | `apps/web` |
| Build command | `pnpm build` |

Set `AUTH_URL` and `NEXT_PUBLIC_SITE_URL` to the Vercel URL, `API_BASE_URL` and `API_PUBLIC_BASE_URL` to the public HTTPS URL of the API, and `CAREEROC_API_AUTH_SECRET` to the API's value.
Add `AGENT_PUBLIC_BASE_URL` so the settings page can show the A2A agent card.
On the API, set `NEXT_PUBLIC_SITE_URL` to the Vercel URL as well, so Gmail sign-in returns through the web app.

## Observability

Set `OTEL_EXPORTER_OTLP_ENDPOINT` on all three services to any OTLP/HTTP collector, such as Jaeger, Grafana Tempo, Honeycomb, or an OpenTelemetry Collector.
Service names default to `careeroc-web`, `careeroc-api`, and `careeroc-agent-runtime`.
