# AGENTS.md

Guidance for contributors and coding agents working in this repository.
Read [DESIGN.md](DESIGN.md) before changing a boundary between the web app, the API, and the agent runtime.

## Build and test commands

```bash
# Whole stack in containers (demo mode, no Google client needed), then the end-to-end smoke test
docker compose --profile full up -d --build --wait
scripts/smoke-demo.sh

# Full local test loop
pnpm web:lint
pnpm --dir apps/web test:unit
pnpm web:build
(cd services/agent-runtime && uv sync)   # once; creates the .venv the next two use
pnpm agent:lint
pnpm agent:test
dotnet test Careeroc.sln
CAREEROC_TEST_POSTGRES="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres" \
  dotnet test Careeroc.sln               # also runs the tests that need real Postgres

# Run a single test class
dotnet test Careeroc.sln --filter "FullyQualifiedName~ClassName"

# Multi-agent browser E2E against a disposable database (CI runs it on every push)
CAREEROC_E2E_CONNECTION_STRING='Host=localhost;Port=5432;Database=careeroc_e2e;Username=postgres;Password=postgres' \
  pnpm e2e:multi-agent

# Development loop: Postgres and the agent runtime in Docker, the API and web app from source
./scripts/dev-up.sh       # Ctrl-C stops the API and web app; `docker compose stop` stops the containers

# Start individually
docker compose up -d postgres agent-runtime
dotnet run --project apps/api                       # port 5114, applies migrations
pnpm --dir apps/web dev                             # port 3000

# EF Core migrations
dotnet ef migrations add MigrationName --project src/Careeroc.Infrastructure
dotnet ef database update --project src/Careeroc.Infrastructure

# Contract and link guardrails (apps/web)
pnpm --dir apps/web gen:api      # regenerate the TS schema from the running API's swagger.json
pnpm --dir apps/web check:api    # fail if apps/web/src/lib/api-schema.ts drifts
```

Copy `.env.example` to `.env` first.
Set `DEMO_MODE=true` and `Demo__Enabled=true` to sign in without Google, or set the Google variables to connect a real Gmail inbox.
In Development, `apps/api` hosts `InboxSyncWorker` itself, so local sync and analysis run without a separate worker process.

Docker fallback if the .NET SDK is not installed:

```bash
docker run --rm -i -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
  -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:10.0 dotnet test Careeroc.sln
```

## Naming

The product name is careeroc, always lowercase in prose, including at the start of sentences and in titles.
Code follows each language's conventions: `Careeroc.*` for .NET namespaces and projects, `careeroc_agent_runtime` for the Python package, `CAREEROC_*` for environment variables, and `careeroc` for the database.
Entities are singular nouns (`InboxEmail`), application services end in `Service`, abstractions are `IXxx` in `Application/Abstractions`, and each EF configuration is `EntityNameConfiguration`.

## Working in this repo

1. Check `git status` and read the docs for the area you are changing.
2. Keep changes inside the layer that owns them.
   If the web app needs a new field, add it to the API contract first and regenerate the client schema.
3. Run the test loop for the parts you touched, and the whole loop for auth, signing, proxy, token, or user-scoping changes.
4. Update the markdown that describes the behavior you changed.

## Architecture

### Layers

- **Domain** (`src/Careeroc.Domain`): entities (`User`, `InboxAccount`, `InboxEmail`, `JobApplication`, `JobApplicationEvent`, `Reminder`, `EmailProcessingJob`, `ResumeDocument`, `ResumeVersion`, `UserMemoryEntry`, `PersonalAccessToken`) and enums.
  No persistence or transport concerns.
- **Application** (`src/Careeroc.Application`): orchestration services and abstractions (`IInboxEmailSource`, `IRecruitingEmailParser`, `IEmailProcessingQueue`, `ILeaseManager`, `ICareerAgentGraphClient`, `IClock`).
  Depends only on Domain and its own abstractions.
- **Infrastructure** (`src/Careeroc.Infrastructure`): EF Core with Npgsql and snake_case names, the Gmail client, the demo inbox, the heuristic parser, the database-backed queue and leases, the agent runtime client, and migrations.
- **Contracts** (`src/Careeroc.Contracts`): API DTOs shared with the web app.

Dependencies point inward.
Do not import Infrastructure from Application, or EF Core, `DbContext`, or HTTP clients into Domain.
If Application needs a side effect, add an interface in `Application/Abstractions` and implement it in Infrastructure.

### Hosts

| Host | Purpose |
|---|---|
| `apps/api` | Minimal API (port 5114 locally, 8080 in containers). Owns auth checks, the email pipeline, every write, personal access tokens, the MCP server at `/mcp`, demo workspaces, and the background workers. Swagger at `/swagger` in Development. |
| `apps/web` | Next.js 16 and React 19. Auth.js 5 with Google OAuth or the demo provider. Browsers reach the API only through its same-origin proxy. |
| `services/agent-runtime` | Python LangGraph runtime (port 8123): the multi-agent review graph with Postgres checkpoints in their own database, human-review interrupts, and the A2A 1.0 endpoint. |

### Email pipeline

1. The user connects Gmail through OAuth, or the demo creates a fictional inbox.
2. The API's background loop pulls mail; after the first backfill it uses Gmail `historyId` sync and falls back to a bounded search when Google expires the cursor.
3. Each message is stored as an `InboxEmail` with an `EmailProcessingJob`.
4. The same host drains jobs, one user at a time across replicas: triage, then extraction of company, role, category, action, and due date.
5. `ApplicationWorkflowService` matches or creates the `JobApplication`, writes `JobApplicationEvent` rows, and creates or closes reminders.
6. The review step sends low-confidence or risky evidence to the review queue before anything unsafe enters the pipeline.

The agent services under `src/Careeroc.Application/Services/Agents`:

- `InboxTriageAgentService`: is this email about recruiting?
- `WorkflowExtractionAgentService`: company, role, category, action, due date
- `RecruiterIdentityAgentService`: who is driving the thread
- `ScamCheckerAgentService`: payment, equipment, or identity requests that must never move the pipeline
- `ReviewEvidenceAgentService`: deterministic baseline plus an optional model second pass
- `FollowUpTaskAgentService`: deterministic follow-up planning

Agent traces are shown in the review queue and on application detail, not hidden in logs.

### Resume, memory, and the orchestrator

- `ResumeIntelligenceService`: resume ingestion (PDF, image, markdown, text), extraction, evaluation, and correction patching; backs `/api/resumes*`.
  It is large; split it into ingestion, extraction, evaluation, and mapping before adding more behavior.
- `UserProfileService`, `UserMemoryProposalService`, and `UserAgentContextSnapshotService`: profile, durable memory (pending, then confirmed), and the context snapshot agents read; backs `/api/profile*`.
- `CareerOrchestratorService`: builds the bounded snapshot for the LangGraph review and stores the result as confirmed or pending memory.
  When the user approves a pending review, it creates the reminders the agents proposed, after checking each application belongs to the user, and resumes the paused graph so the trace ends with the decision.
  It falls back to a deterministic review when the runtime fails.
- `PersonalAccessTokenService` and `AgentQueryService`: tokens and the read-only queries behind `/mcp` and `/agent/v1/*`.

### Trust boundary

The Next.js app is the only browser-facing caller of the API.
Browsers go through the same-origin proxy at `/api/careeroc/*`, which signs each request with `CAREEROC_API_AUTH_SECRET`; the API verifies the signature and rate-limits per user.

- Never expose `CAREEROC_API_AUTH_SECRET` to the browser.
- User identity comes from the signed request or a personal access token, never from an `X-User-*` header.
  Every mutation verifies the signature and checks ownership.
- Personal access tokens authorize only `/mcp` and `/agent/*`, and those endpoints never write.
- The agent runtime gets bounded snapshots and a shared secret, never careeroc's database credentials, raw email, or OAuth tokens.
  Agents only propose writes; the API applies them after a person approves.
- The Gmail OAuth callback reaches the API through the web app, signed as the user who started the connection.
- Every `/api/*` route needs a signed user; a route that must work without one needs an exemption in `ApiUserContextMiddleware` and a partition in the global rate limiter in `Program.cs`.
- Never log OAuth tokens, secrets, or email bodies.

After changing auth, signing, proxy routes, OAuth, tokens, rate limiting, or user scoping, rerun the full test loop and walk sign-in, review, and application detail end to end.

### Key design decisions

- **Raw email is separate from workflow state**: `InboxEmail` is ingestion; `JobApplication` is the pipeline record.
- **Extraction is replaceable**: the heuristic parser sits behind `IRecruitingEmailParser`; an optional model pass is kept only when it is available and confident.
- **The queue is a table**: `IEmailProcessingQueue` claims jobs with `FOR UPDATE SKIP LOCKED` and can move to a managed queue later.
- **One API host**: sync and processing run inside the API; split them out only if throughput requires it.
- **Manual correction is first-class**: review corrections rerun the same workflow rules as automation.
- **User scope is the product boundary**: signed user context, allowlists, role checks, and ownership checks protect every query.
  Do not trust browser-supplied identity or bypass user-scoped queries.

## Conventions

### Shared

- Comment only the reason the code cannot show; do not narrate what it does.
- No compatibility shims or feature flags without a written reason; delete unused code.
- Three similar lines beat a premature abstraction.

### Backend

- Inject `IClock` instead of calling `DateTime.UtcNow`; persisted timestamps are UTC with `*AtUtc` names.
- Every persisted entity has an EF configuration class in `Infrastructure/Persistence/Configurations`; column names stay snake_case through the context's convention.
- Do not edit a migration that has shipped; add a new one.
- Do not swallow exceptions; on parser or model failure, log a warning or error and use the deterministic path.
- After a failed save in a loop that shares a `DbContext`, call `ChangeTracker.DiscardPendingChanges()` before saving anything else.

### Frontend

- Client components live in `apps/web/src/components` and start with `"use client"`.
- Server data fetching lives in `apps/web/src/lib/*-server.ts`; pass data to client components as props.
- Browser-to-API calls go through `/api/careeroc/*`, never to the API directly.
- Workspace pages live under `/app/*`; top-level `/analytics` and `/review` stay redirects.
- Use semantic CSS tokens such as `bg-[var(--surface-elevated)]`, not hex colors.
- Show statuses and categories through `formatEnumLabel`, and times that depend on the viewer through `LocalTime` or `useHydrated`, so server and browser render the same HTML.
- `typedRoutes` is on; cast dynamic paths through `Route` from `next`.

### API contract

When adding or changing a read endpoint, chain `.Produces<ResponseType>()` and return `TypedResults.Ok<T>(...)` so the schema lands in `/swagger/v1/swagger.json`.
Then run `pnpm --dir apps/web gen:api` against the running API, and `pnpm --dir apps/web check:api` in CI.

### Testing

- .NET tests live in `tests/Careeroc.Tests`, one class per production class, named `<ClassUnderTest>Tests`.
- `TestDbContextFactory` gives each test an isolated EF Core in-memory `DbContext`.
  Do not mock `DbContext`; each test creates its own data.
- Behavior the in-memory provider cannot model (bulk deletes, column limits, `SKIP LOCKED`) goes in a `[PostgresFact]` test using `PostgresTestDatabase`; it runs when `CAREEROC_TEST_POSTGRES` is set.
- `PipelineEvaluationTests` scores the pipeline against `eval/pipeline-labels.json`.
  Change a label only when the old label was wrong, never to make a regression pass.
- `services/agent-runtime/tests/test_evidence_gate_eval.py` scripts what a careless or manipulated model could return and checks what the evidence gate lets through; add a case for every new gate rule.
- Use `TestClock`, and fakes for Gmail, the model provider, and the agent runtime; tests make no network calls.
- Prefer specific assertions such as `Assert.Equal(expected, actual)`, and cover each service's happy path plus at least one failure or fallback.
- The agent runtime tests use the deterministic suite and an in-memory checkpointer, plus one Postgres test when `CAREEROC_AGENT_TEST_DATABASE_URL` is set.
- Web unit tests use Vitest for pure helpers and UI logic; do not add UI snapshot tests.

## Environment

The API loads the root `.env` through `EnvironmentFileLoader`; `.env.example` lists the variables the code reads.
The main ones:

- `ConnectionStrings__Careeroc` or `DATABASE_URL`: Postgres
- `AUTH_GOOGLE_ID` / `AUTH_GOOGLE_SECRET` (web) and `Google__ClientId` / `Google__ClientSecret` (API): Google sign-in and Gmail
- `NEXT_PUBLIC_SITE_URL` (API and web): the web app's public URL, which Gmail sign-in returns to
- `CAREEROC_API_AUTH_SECRET`: the web-to-API signing secret, server-only and identical on both sides
- `CAREEROC_TOKEN_PROTECTION_KEY`: encrypts stored Gmail tokens
- `ACCESS_ALLOWED_EMAILS` / `ACCESS_ALLOWED_DOMAINS`: required outside Development; `ACCESS_ALLOW_ALL_USERS=true` only for an intentionally open deployment
- `DEMO_MODE` (web) and `Demo__Enabled` (API): per-visitor demo workspaces; set both together
- `AgentRuntime__Enabled`, `AgentRuntime__BaseUrl`, `AgentRuntime__SharedSecret`: the API's connection to the agent runtime; the secret is required outside Development
- `CAREEROC_AGENT_PROVIDER`: `deterministic`, `ollama`, or `openai` for the runtime's specialists
- `CAREEROC_AGENT_CHECKPOINT_DATABASE_URL`: the runtime's own checkpoint database; without it, checkpoints live in memory
- `Ollama__Enabled`, `Ollama__BaseUrl`, `Ollama__Model`, `Ollama__ApiKey`: optional model pass in the email pipeline
- `OTEL_EXPORTER_OTLP_ENDPOINT`: OTLP/HTTP collector for all three services

If Auth.js reports `JWTSessionError: no matching decryption secret` after `AUTH_SECRET` changes, the browser has a stale session cookie; `http://localhost:3000/api/auth/clear` clears it locally.

## Adding an API endpoint

1. Add DTOs in `src/Careeroc.Contracts`.
2. Add or extend a service in `src/Careeroc.Application/Services`.
3. Add the endpoint in `apps/api/Endpoints/` and map it in `apps/api/Program.cs`.
4. If the browser needs it, it is reachable through the existing `/api/careeroc/*` proxy; add it to the route coverage list in `ApiRoutingSecurityTests`.

## Changing the database schema

1. Update entities in `src/Careeroc.Domain/Entities`.
2. Update EF configurations in `src/Careeroc.Infrastructure/Persistence/Configurations`.
3. Run `dotnet ef migrations add DescriptiveName --project src/Careeroc.Infrastructure`.
4. Migrations apply on API startup in Development, and anywhere `Database__ApplyMigrationsOnStartup=true` is set.

## Documentation

- `DESIGN.md`: system design, decisions with rejected alternatives, trust boundaries, failure modes, evals
- `SECURITY.md`: security controls and vulnerability reporting
- `docs/architecture.md`: layers and processing flows
- `UI.md`: product surfaces and the UI contract
- `docs/routing.md`: web routes and API endpoints
- API reference: the OpenAPI document at `/swagger` when the API runs in Development, and the TypeScript types generated from it in `apps/web/src/lib/api-schema.ts`, which CI checks for drift
- `docs/agents/`: one spec per agent and the review graph's nodes; `docs/adr/`: decision records
- `docs/deploy/README.md`: deployment targets, secrets, and the Railway steps
