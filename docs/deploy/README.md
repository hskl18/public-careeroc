# Deploying careeroc

Every target runs the same three images with the same service names: `web` (port 3000), `api` (port 8080), and `agent-runtime` (port 8123), plus PostgreSQL 17.
Only `web` must be public.
Expose `api` too if assistants should reach `/mcp`, and `agent-runtime` if other agents should reach `/a2a`.

| Target | Where | How it is verified |
|---|---|---|
| Docker Compose | [docker-compose.yml](../../docker-compose.yml) | CI starts it and runs [scripts/smoke-demo.sh](../../scripts/smoke-demo.sh) |
| Kubernetes | [deploy/helm/careeroc](../../deploy/helm/careeroc/README.md) | CI installs it on kind and runs the same smoke test ([scripts/smoke-kubernetes.sh](../../scripts/smoke-kubernetes.sh)) |
| Vercel (web only) | steps below | Not tested |
| Vercel (read-only demo) | steps below | Runs [careeroc.vercel.app](https://careeroc.vercel.app); not in CI |

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
Docker Compose runs that file on first start.
On Kubernetes, add `AGENT_CHECKPOINT_DATABASE_URL` to the secret.
Without it, checkpoints live in memory: a review that waits for approval across a runtime restart still applies its decision, but its trace does not get the final human-review step.

Public URLs must use HTTPS in production.
Internal service URLs such as `http://api:8080` may use HTTP when the host is private.

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

## Vercel (read-only demo)

With `DEMO_SNAPSHOT=true`, `apps/web` needs no API, database, or agent runtime.
Every visitor signs in as one fictional account, API reads are answered from `apps/web/src/lib/demo-snapshot.json`, and writes are refused.
Use the settings above, and set only `DEMO_MODE=true`, `DEMO_SNAPSHOT=true`, `AUTH_SECRET`, and `AUTH_URL` and `NEXT_PUBLIC_SITE_URL` as the Vercel URL.
The app refuses to start if Google credentials are also set.
Deploy from the repository root, because the root `.vercelignore` decides what is uploaded.

Re-record the snapshot on an empty database when an API response changes shape:

```bash
CAREEROC_VERSION=snapshot-recording docker compose -p careeroc-snapshot up -d --build --wait api
node apps/web/scripts/record-demo-snapshot.mjs
docker compose -p careeroc-snapshot down -v
```

Dates in the snapshot move forward by whole days when served, so ages and deadlines stay current.

## Observability

Set `OTEL_EXPORTER_OTLP_ENDPOINT` on all three services to any OTLP/HTTP collector, such as Jaeger, Grafana Tempo, Honeycomb, or an OpenTelemetry Collector.
Service names default to `careeroc-web`, `careeroc-api`, and `careeroc-agent-runtime`.
