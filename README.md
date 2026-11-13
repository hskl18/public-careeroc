# careeroc

> The current careeroc is a job search skill for your agent: [hskl18/careeroc-skill](https://github.com/hskl18/careeroc-skill), with its site at [careeroc.com](https://www.careeroc.com).
> This repository is the earlier hosted product, kept as a reference implementation.

An open-source, multi-agent job search workspace.
careeroc reads recruiting email and keeps an evidence-backed pipeline of applications, deadlines, and next actions.
Specialist agents review the search, a person approves what they propose, and your own assistant can read the pipeline over MCP or A2A.

**Live demo: [careeroc.vercel.app](https://careeroc.vercel.app).** It needs no sign-up and opens a read-only workspace recorded from the real pipeline on a fictional inbox, with free Ollama models behind it: `gemma4:31b` on each email and `gpt-oss:120b` for the multi-agent review.
The recording is kept as it came out, and approving the review on the dashboard replays the end of the graph in your browser.

Run the whole system with one command, no accounts or API keys: `docker compose --profile full up -d`, then open http://localhost:3000.
There, **Open the demo** gives each visitor a private workspace, run through the real pipeline and removed from the product database after 24 hours, including the multi-agent review, approvals, MCP, and A2A that the hosted demo cannot offer.

![careeroc dashboard](docs/media/dashboard.png)

careeroc ran as a hosted product with a small beta, and I gave up on it.
It is a good fit for my own job search and a poor fit for a product, mainly because agents can now do this work themselves.
This repository is its complete source, published as a reference implementation.
It is a working system rather than a template: a .NET 10 API, a Next.js 16 web app, and a Python LangGraph runtime, with tests, evals, traces, and deployment code for Compose and Kubernetes.

## What it shows

| Area | What is in the code |
|---|---|
| Email to pipeline | Deterministic-first extraction in .NET: triage, company, role, stage, deadlines, thread continuity, and reminders. An optional model pass is used only when it is available and confident; otherwise the deterministic result stands. |
| Review gates | Uncertain or risky evidence (agencies, scams, weak company matches) pauses in a review queue instead of changing the pipeline. |
| Multi-agent review | A LangGraph graph runs resume, mailbox, and strategy specialists in parallel, a supervisor merges them and proposes follow-up reminders, and an evidence gate checks every citation and proposal against the user's own data. |
| Human in the loop | A review that proposes a write pauses on a LangGraph `interrupt()` with a Postgres checkpoint. When the user approves it, the API, never the agents, creates the reminders, and the graph resumes so the decision lands in its trace. |
| Bring your own assistant | Read-only personal access tokens, an MCP server at `/mcp`, and an A2A 1.0 agent with a public agent card. |
| Observability | OpenTelemetry from the browser-facing web app through the API into each agent node, using the GenAI `invoke_agent` span conventions. |
| Evals | The 48 inbound emails of the 51-email demo inbox, labeled by hand and scored in CI with the rules alone for triage, category, company, status, and review precision and recall; the same scores for each free Ollama model, run by hand with `scripts/eval-model.sh`; and a model-free eval of the evidence gate against invented citations and prompt injection. |
| Deploy | Docker Compose and a Helm chart for any Kubernetes, both started and smoke tested in CI, and multi-arch images on GHCR. |

## Screenshots

| | |
|---|---|
| ![Applications](docs/media/applications.png) **Applications.** 51 emails become 20 tracked applications. | ![Application detail](docs/media/application-detail.png) **Application detail.** Pipeline stage, evidence, the next move, and resume fit. |
| ![Review queue](docs/media/review-queue.png) **Review queue.** The agency and the scam email wait for a person. | ![Agent review](docs/media/agent-review.png) **Multi-agent review.** The agents propose reminders, the gate drops the citations it cannot find, and nothing is created until the user approves. |
| ![Assistant access](docs/media/assistant-access.png) **Connect your own assistant.** Tokens, the MCP command, and the A2A card. | ![Landing page](docs/media/landing.png) **Landing.** One click opens the demo. |

One "Run review" click, traced across all three services in Jaeger:

![Trace across web, API, and agents](docs/media/trace.png)

## Architecture

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/architecture-dark.svg">
  <img alt="The browser talks to the Next.js 16 web app, which sends HMAC-signed requests to the .NET 10 API. The API reads the Gmail API or the demo inbox and stores data in PostgreSQL 17. It sends a bounded snapshot to the LangGraph runtime in Python 3.14, which returns proposals, keeps checkpoints in its own database, and reads back with a scoped token. Your own assistant reaches the API over MCP with a token, other agents reach the runtime over A2A with a token, and all three services can send traces to OpenTelemetry and Jaeger." src="docs/architecture-light.svg">
</picture>

- **The API owns every write.** Agents receive a bounded snapshot with no email bodies, OAuth tokens, or database credentials, and return typed results and proposals; the API applies a proposal only after the user approves it and the application is theirs.
- **Deterministic first.** The email pipeline is rule-based and tested; models enrich it and are never required.
  The agent runtime ships a deterministic suite, so local runs and CI need no model or API key; the trace above comes from it, and CI's evals use no model at all.
  The hosted demo is one unedited recording with real models behind both steps, with the citations the gate dropped still on show.
- **Agent output is visible.** Each step's summary, reason, confidence, and evidence are shown on the dashboard, and low-confidence results and proposed writes stay pending until a person decides.

[DESIGN.md](DESIGN.md) explains the decisions, the rejected alternatives, trust boundaries, and failure modes.

## Where to start reading

1. [graph.py](services/agent-runtime/src/careeroc_agent_runtime/graph.py): the review graph and the evidence gate in about 300 lines.
2. [test_evidence_gate_eval.py](services/agent-runtime/tests/test_evidence_gate_eval.py): what a careless or manipulated model can and cannot get past the gate.
3. [CareerOrchestratorService.cs](src/Careeroc.Application/Services/CareerOrchestratorService.cs): the snapshot the agents see, and the approval that turns a proposal into a write.
4. [ApplicationWorkflowService.cs](src/Careeroc.Application/Services/ApplicationWorkflowService.cs): how an email matches an application and moves its status.
5. [ApiRequestSignatureValidator.cs](apps/api/Security/ApiRequestSignatureValidator.cs): the signed requests that carry user identity from the web app to the API.

## Quick start

You need Docker.

```bash
git clone https://github.com/hskl18/public-careeroc.git
cd public-careeroc
docker compose --profile full up -d --wait
```

Open http://localhost:3000 and choose **Open the demo**.
Compose downloads the prebuilt images of the latest release for your CPU (amd64 or arm64), so nothing is compiled; set `CAREEROC_VERSION` (for example `1.3.1`) to pin a release, run `docker compose pull` to move to a newer one, or add `--build` to run this checkout.
Running, the four containers use about 600 MB of memory and almost no CPU.
Their ports are published on 127.0.0.1 only.

The [usage guide](docs/usage.md) covers building from source, traces, connecting your own assistant over MCP or A2A, and running on a real model or your own Gmail.

## Documentation

- [DESIGN.md](DESIGN.md) - system design, decisions, and trade-offs
- [docs/usage.md](docs/usage.md) - building from source, traces, your own assistant, a real model, and Gmail
- [docs/architecture.md](docs/architecture.md) - layers, repository layout, and processing flows
- [docs/testing.md](docs/testing.md) - local suites, what CI runs, and the evals
- API reference - the OpenAPI document at `/swagger` when the API runs in Development; [apps/web/src/lib/api-schema.ts](apps/web/src/lib/api-schema.ts) is generated from it and checked for drift in CI
- [UI.md](UI.md) - product surfaces and the UI contract
- [docs/routing.md](docs/routing.md) - web routes and API endpoints
- [docs/backend/agentic-workflow-engine-decision.md](docs/backend/agentic-workflow-engine-decision.md) - why LangGraph, and how the graph is shaped
- [docs/adr](docs/adr) - architecture decision records
- [docs/deploy/README.md](docs/deploy/README.md) - Compose, Kubernetes, and Vercel targets, images, and secrets
- [SECURITY.md](SECURITY.md) - controls and how to report a vulnerability
- [CONTRIBUTING.md](CONTRIBUTING.md) - what changes are welcome and how to send them
- [AGENTS.md](AGENTS.md) - commands and conventions for contributors and coding agents
- [Releases](https://github.com/hskl18/public-careeroc/releases) - what changed in each version

## License

[MIT](LICENSE)
