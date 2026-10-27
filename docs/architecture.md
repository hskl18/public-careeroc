# Architecture

This document describes how the code is organized and how data moves through it.
[DESIGN.md](../DESIGN.md) explains why it is built this way, [UI.md](../UI.md) covers the interface, and [routing.md](routing.md) lists the routes.

## Projects and layers

| Project | Owns |
|---|---|
| `src/Careeroc.Domain` | Entities and enums, with no persistence or transport concerns |
| `src/Careeroc.Contracts` | API DTOs shared by the endpoints and the web app |
| `src/Careeroc.Application` | Workflow decisions, agent services, query services, and the abstractions they need |
| `src/Careeroc.Infrastructure` | EF Core and migrations, the Gmail client, the demo inbox, parsers, the database queue and leases, the agent runtime client |
| `apps/api` | HTTP endpoints, auth middleware, the MCP server, composition, and the background workers |
| `apps/web` | The Next.js public site, the workspace under `/app`, and the signed API proxy |
| `services/agent-runtime` | The LangGraph review graph, its checkpoints in a database of their own, and the A2A endpoint |

Dependencies point inward.
Domain knows nothing about EF Core, Gmail, HTTP, models, or Next.js.
Application depends only on Domain, Contracts, and its own abstractions; when it needs a side effect, it declares an interface in `Application/Abstractions` and Infrastructure implements it.
Endpoints resolve the user, validate the request shape, call one application service, and return a contract DTO; parsing, matching, reminders, and analytics never live in an endpoint.

### Entities

- Mailbox and pipeline: `User`, `InboxAccount`, `InboxEmail`, `EmailProcessingJob`, `JobApplication`, `JobApplicationEvent`, `Reminder`, `RecruitingContact`, `ApplicationArtifactEvidence`
- Resume and memory: `ResumeDocument`, `ResumeVersion`, `ResumeCorrection`, `ResumeEvaluation`, `ApplicationResumeFit`, `UserMemoryEntry`, `UserSoulProfile`
- Operations: `ModelCallEvent`, `AuditEvent`, `PersonalAccessToken`

Raw email (`InboxEmail`) and the pipeline record (`JobApplication`) are separate on purpose: ingestion can be replayed or corrected without rewriting history.

### Application services

- Mailbox: `EmailSyncService` imports messages and queues analysis; `EmailIntelligenceService` runs the agent chain; `ApplicationWorkflowService` matches or creates applications, moves status, and manages reminders.
- Review and correction: `ManualReviewQueryService` and `ManualReviewService`.
- Read models: `DashboardQueryService`, `ApplicationDetailQueryService`, `AnalyticsQueryService`, `RecruitingContactQueryService`.
- Evidence: `ApplicationArtifactEvidenceService` and `EmailAttachmentEvidenceImportService` turn supported PDFs and images into evidence cards.
- Setup and profile: `InboxSetupService`, `UserProfileService`, `DemoWorkspaceService`.
- Resume and strategy: `ResumeIntelligenceService`, `ResumeMailboxContextBuilder`, `UserMemoryProposalService`, `UserAgentContextSnapshotService`, `CareerOrchestratorService`.
- Assistant access: `PersonalAccessTokenService` and `AgentQueryService`.

## Email pipeline

1. Gmail OAuth (or the demo inbox) creates an `InboxAccount`.
2. The sync worker imports messages into `InboxEmail`; after the first backfill it uses Gmail `historyId` sync and falls back to a bounded search when the cursor expires.
3. Each new or stale message gets an `EmailProcessingJob` through `IEmailProcessingQueue`, which owns duplicate checks and backpressure (at most 5000 active jobs by default, `EmailProcessing__MaxActiveJobs`).
4. Workers claim jobs with `FOR UPDATE SKIP LOCKED`, so several API replicas can drain the queue safely.
   A worker also holds a per-user Postgres advisory lease while it processes a job, so two replicas never match one user's emails at the same time; a job whose user is busy goes back to the queue without spending an attempt.
5. The agent chain runs in order, each step writing structured output for the next:
   - inbox triage: is this about recruiting?
   - workflow extraction: category, company, role, action, due date, confidence
   - recruiter identity: who is driving the thread
   - scam check: payment, equipment, banking, or identity requests never move the pipeline
   - application matching: thread first, then company and role
   - review evidence: is it safe to write, or does it need a person?
   - follow-up planning: the reminder, if action is required
6. The workflow service writes the status transition, an event, reminders, and contact links.
   An offer closes open interview and assessment reminders and asks the user to review the offer; a rejection or closure closes every open reminder.
7. Supported attachments (PDF, PNG, JPEG, WebP) are imported as evidence cards only after the email is linked to an application; raw bytes are never stored.
8. Query services shape the result for each page.

Every step is a narrow, typed decision, not a free-form agent, so a wrong result can be traced to one stage.
The specs for each agent are in [agents/](agents/README.md).

### Rules that protect the pipeline

- **Company and role** pollute analytics and timelines when wrong.
  Never infer a company from the user's school or mailbox domain, ignore no-reply and mail-infrastructure domains unless the body confirms the employer, preserve display names while normalizing suffixes for matching, and send uncertain cases to review.
- **Review queue** entries are for ambiguity only: low classification or match confidence, an unextractable role, a likely scam, or an agency that hides the employer.
  Receipts, thank-you notes, and security codes never go there.
  A correction reruns the same workflow rules as automation.
- **Reminders** are work, not a log.
  Terminal applications keep no open reminders, far-future and stale reminders stay out of "needs attention", and duplicates refresh instead of multiplying.
- **Idempotency**: importing or processing the same message updates or skips; it never duplicates applications, events, or reminders, and an admin can retry a failed job.
- **One bad email stays one failure**: header values are clipped to their columns, a failed save discards its pending changes before the failure is recorded, and a job that times out on its last attempt is marked failed so it shows up for retry.

## Background work and freshness

The sync worker, recovery of jobs whose worker died, demo cleanup, and queue draining all run inside the API host.
In Development the sync worker runs every 300 seconds; hosted deployments should use 900 seconds or more and `SyncWorker__RunOnStartup=false`.
The web app can ask for a bounded inbox check, but it never owns the pipeline.
Dashboard reads are cached per user for `Dashboard__ResponseCacheSeconds`, and a refresh after a change reloads the whole dashboard once.
Gmail push notifications are not implemented; [backend/gmail-push-plan.md](backend/gmail-push-plan.md) describes the design.

## Resume intelligence

1. The user uploads a PDF or image, or pastes text, on `/app/resume`.
2. The API stores a `ResumeDocument`; PDF extraction keeps link annotations as an allow-list, so a model cannot invent GitHub or LinkedIn URLs.
3. `ResumeIntelligenceService` runs schema-locked extraction into a `ResumeVersion`.
4. User edits become `ResumeCorrection` rows or structured patches, both user-scoped and audited.
5. Evaluation reviews the version against target roles and the mailbox evidence from `ResumeMailboxContextBuilder`, without writing to applications.

## Memory

Memory lives in the database, not in prompt files.
Each `UserMemoryEntry` has a source, a confidence, a confirmation state, and timestamps; `UserSoulProfile` holds communication and goal preferences.
Agents and the mailbox may propose memory, but only the user's own edits and confirmations are authoritative, and model inferences stay pending until confirmed.
`memory.md` and `soul.md` are rendered from those rows when an agent needs compact context, and `/app/memory` lets the user inspect, edit, confirm, reject, export, or clear them.

Recruiting-email guidance for the optional model pass is a markdown playbook in [agent-memory/recruiting-email-playbook.md](agent-memory/recruiting-email-playbook.md), plus an optional, untracked feedback file of the user's corrections (`Ollama__UserFeedbackFilePath`, on by default only in Development).
The file is shared by everyone on an install, so leave it unset when several people or demo visitors use one install.
Guidance is developer-owned context; email content can never override it.

## Multi-agent review and interop

`CareerOrchestratorService` builds a bounded snapshot and calls the LangGraph runtime through `ICareerAgentGraphClient`.
The graph can propose follow-up reminders; a review that proposes any, or that fails the evidence gate, pauses on an interrupt and is stored as pending memory.
When the user approves it, the API creates the reminders itself, after checking each application belongs to the user, writes the decision back to the stored review, and resumes the paused graph so its trace ends with the decision.
A pending review never replaces the last confirmed one until the user approves it, and earlier reviews are not fed back to the agents as facts about the user.
The graph, its human-review interrupt, and the A2A endpoint are described in [DESIGN.md](../DESIGN.md#multi-agent-review) and [backend/agentic-workflow-engine-decision.md](backend/agentic-workflow-engine-decision.md).
The MCP server (`apps/api/Mcp`) and the `/agent/v1/*` endpoints are read-only and authorized by personal access tokens through `PersonalAccessTokenMiddleware`.

## Models and fallback

- Email parsing runs deterministic heuristics first; the optional Ollama pass replaces the result only when it is available, returns valid JSON, and is above `Ollama:MinimumConfidence`.
- Review evidence starts from a deterministic decision; the model can only add evidence.
- Artifact extraction produces evidence cards and never changes stage or reminders directly.
- Calls are bounded by retries, concurrency limits, timeouts, and a circuit breaker, and each is recorded as a `ModelCallEvent` without prompts or email bodies.
- There is one model route; when it fails, the deterministic result stands rather than a second model being tried.

## Read models

Query services return page-specific DTOs, never raw entities.
The dashboard gets near-term actions, ranked recent changes, a compact sync overview, and capped application summaries.
Application detail gets the full evidence, source messages, open reminders, and correction affordances.
Analytics gets aggregates and breakdowns and never mutates workflow state.
Filtering of irrelevant or stale data happens in these services, not only in the UI.

## Web app

Route files in `apps/web/src/app` stay thin: they load data, handle redirects, and pass a stable snapshot to a workspace component in `src/components`.
Server data comes from `src/lib/*-server.ts` helpers, deduplicated with React `cache()` when the layout and the page need the same snapshot.
Client components exist only for interaction, polling, or local UI state.

Browsers never call the API directly.
They call the same-origin proxy at `/api/careeroc/*`, which checks the session and the request origin, signs the user context, and forwards the call; server components use `careeroc-api-server.ts` the same way.
To add an API call: add or reuse the DTO in `src/Careeroc.Contracts`, regenerate `src/lib/api-schema.ts` with `pnpm --dir apps/web gen:api`, and fetch through a server helper or the proxy.

Polling is narrow.
The header sync pill polls slowly and only triggers an inbox check while the first sync or analysis is incomplete; the inbox page polls faster only while analysis is active; resume work runs from explicit actions.
Pages skip polling while the tab is hidden.

Hydration must be stable: render the same navigation and text on server and client, pass ISO timestamps and format them after hydration with `LocalTime`, never branch on `window` during render, and use `useHydrated` for other client-only values such as relative times.

## Observability

Every API response carries `X-Careeroc-Correlation-Id`, and processing jobs and model calls store bounded run and correlation IDs.
Setting `OTEL_EXPORTER_OTLP_ENDPOINT` turns on OTLP/HTTP tracing in all three services; spans cover dashboard queries, sync, email processing, attachment import, model calls, and each agent node.
Maintenance rules for sync, reminders, and company names are in [backend/evidence-pipeline-maintenance.md](backend/evidence-pipeline-maintenance.md).

## Change checklist

- DTOs go in Contracts, workflow logic in Application, providers and models in Infrastructure, and endpoints stay thin.
- Every automated decision the user sees has evidence, a confidence, or a trace, and a deterministic fallback or a review path.
- Background work is safe to retry, and new reminders are deduplicated.
- Admin endpoints stay under `/api/admin/*` with an API-side role check and sanitized DTOs.
- Migrations match entity changes, and memory writes keep their source.
- In the web app, route files stay thin, authenticated calls go through the proxy, the first render is hydration-stable, and empty, loading, fallback, stale, and blocked states are explicit.
