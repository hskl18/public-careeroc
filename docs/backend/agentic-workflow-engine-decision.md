# Multi-Agent Workflow Engine Decision

Last updated: 2026-10-27

## Decision

Adopt LangGraph v1 as a separate Python agent runtime for bounded multi-agent workflows.
Keep the .NET API as the only owner of authentication, user scope, domain mutation, and durable product records.
Use a typed HTTP contract between the .NET application layer and the LangGraph runtime.
Keep the existing deterministic Career Orchestrator as an explicit fallback when the runtime or model provider is unavailable.

The first migrated workflow is resume and mailbox strategy review.
It runs three specialist agents in parallel, synthesizes their outputs through a supervisor agent that may propose follow-up reminders, and applies a deterministic evidence gate before the result can become confirmed memory.
When the gate is not satisfied, or the review proposes reminders, the graph pauses on a LangGraph interrupt until the user approves or rejects it.
The graph never writes; on approval the API creates the reminders.

## Framework Research

| Framework | Current strengths | Fit for careeroc | Decision |
|---|---|---|---|
| LangGraph v1 | Explicit state graphs, parallel nodes, checkpointing, replay, streaming, subgraphs, and human-in-the-loop interrupts | Strong fit for bounded workflows that mix deterministic services and model agents | Selected |
| LangChain Deep Agents | Planning, filesystem context, skills, and dynamic subagents for open-ended long-running work | Useful for a future research workspace, but too autonomous for the current resume and mailbox decision path | Deferred |
| OpenAI Agents SDK | Small agent abstraction, tools, handoffs, guardrails, sessions, and tracing | Strong option for OpenAI-first conversational handoffs, but less suitable than an explicit graph for the current provider-neutral workflow | Not selected |
| Microsoft Agent Framework | Native C# and Python agents, typed graph workflows, middleware, checkpointing, MCP and A2A, and the successor to AutoGen and Semantic Kernel | Excellent language fit; it was in preview when this was decided and reached 1.0 on April 3, 2026 | Revisit if the review moves into the .NET API |
| Google ADK | Sequential, parallel, loop, and custom workflow agents with strong Google ecosystem integration | Capable, but it adds ecosystem coupling without a product advantage for this workflow | Not selected |
| CrewAI | Simple role-based crews and event-driven flows with memory and guardrails | Good for automation prototypes, but its role abstraction gives less precise state and evidence control than LangGraph | Not selected |

Primary sources:

- [LangGraph v1 release notes](https://docs.langchain.com/oss/python/releases/langgraph-v1)
- [LangChain custom multi-agent workflows](https://docs.langchain.com/oss/python/langchain/multi-agent/custom-workflow)
- [LangChain subagent architecture](https://docs.langchain.com/oss/python/langchain/multi-agent/subagents)
- [LangGraph interrupts](https://langchain-ai.github.io/langgraph/concepts/breakpoints/)
- [OpenAI Agents SDK orchestration](https://openai.github.io/openai-agents-python/multi_agent/)
- [Microsoft Agent Framework overview](https://learn.microsoft.com/en-us/agent-framework/overview/)
- [Microsoft Agent Framework 1.0 release coverage](https://visualstudiomagazine.com/articles/2026/04/06/microsoft-ships-production-ready-agent-framework-1-0-for-net-and-python.aspx)
- [Google ADK multi-agent introduction](https://developers.googleblog.com/agent-development-kit-easy-to-build-multi-agent-applications/)
- [CrewAI documentation](https://docs.crewai.com/)

## Runtime Boundary

```mermaid
flowchart LR
  UI["Next.js workspace"] --> API[".NET API and auth boundary"]
  API --> Snapshot["Bounded typed snapshot"]
  Snapshot --> Graph["LangGraph runtime"]
  Graph --> Resume["Resume Evidence Agent"]
  Graph --> Mailbox["Mailbox Outcome Agent"]
  Graph --> Strategy["Career Strategy Agent"]
  Resume --> Supervisor["Career Supervisor"]
  Mailbox --> Supervisor
  Strategy --> Supervisor
  Supervisor --> Gate["Deterministic evidence gate"]
  Gate -- passes --> API
  Gate -- "proposes reminders or needs a person" --> Pause["interrupt() on a Postgres checkpoint"]
  Pause -- user decision --> API
  API --> Memory["Confirmed or pending user memory"]
  API -- "approved, ownership checked" --> Reminders["Reminders"]
  API -. runtime failure .-> Fallback["Deterministic Career Orchestrator"]
  Fallback --> Memory
```

The sidecar receives only a bounded snapshot.
It does not receive raw email bodies, OAuth tokens, provider credentials, or credentials for careeroc's database.
It connects only to its own checkpoint database, as a role that cannot reach careeroc's.

The .NET API validates that the returned `threadId` matches the request before accepting the response.
The API stores low-confidence, weak-evidence, or reminder-proposing graph results as pending memory, and keeps only proposals for applications the user owns.
A pending review does not replace the last confirmed review, and no review, pending or confirmed, is fed back to the agents as a fact about the user.

## Graph Shape

The first graph uses these nodes:

1. `resume_analyst` evaluates resume evidence and outcome linkage.
2. `mailbox_analyst` evaluates application statuses, positive outcomes, and repeated rejection patterns.
3. `strategy_analyst` proposes bounded and reversible strategy actions from confirmed memory and role patterns.
4. `career_supervisor` reconciles the three specialist outputs with the deterministic baseline and may propose up to three follow-up reminders, each for an application in the snapshot.
5. `evidence_gate` keeps only the citations it can find in the snapshot and scales the confidence by the grounded share, keeps only reminder proposals for applications in the snapshot without an open reminder, and checks application history and resume presence.
   Any kept proposal requires a person.
6. `human_review` runs only when the gate requires it: it calls `interrupt()`, so the run stops on a durable checkpoint and reports `review_required`.
   When the user approves or rejects the pending review, the API creates the approved reminders itself, then resumes the run through `POST /v1/resume-mailbox-review/{threadId}/decision`, and writes the final trace back to the stored review.

The first three nodes execute in the same LangGraph superstep and therefore run concurrently.
The supervisor runs only after all parallel branches complete.
Every run receives a unique thread ID and is checkpointed independently.

## Failure Policy

The runtime does not hide partial model failures.
Each specialist and the supervisor run with a `TimeoutPolicy` per attempt and a `RetryPolicy` of two attempts, so a hung provider call cannot hold the request.
The whole run also has a budget (`CAREEROC_AGENT_RUN_TIMEOUT_SECONDS`, 100 by default) below the API's `AgentRuntime__TimeoutSeconds` (120), so the runtime gives up before the API stops waiting and never checkpoints a review the API did not store.
If a node still fails or the budget runs out, the graph request returns an error.
The .NET client records a warning and returns the existing deterministic review with a visible `completed_with_fallback` status.
The response trace identifies the fallback reason without exposing provider response bodies or secrets.

## Configuration

The runtime is disabled at the .NET boundary by default.
This keeps local and hosted environments safe until the sidecar and provider route are configured.

```bash
# API
AgentRuntime__Enabled=true
AgentRuntime__BaseUrl=http://localhost:8123
AgentRuntime__SharedSecret=replace-with-a-random-shared-secret
AgentRuntime__TimeoutSeconds=120

# Runtime
CAREEROC_AGENT_SHARED_SECRET=replace-with-a-random-shared-secret
CAREEROC_AGENT_CHECKPOINT_DATABASE_URL=postgresql://careeroc_agent:...@postgres:5432/careeroc_agent
CAREEROC_AGENT_MINIMUM_CONFIDENCE=0.65
CAREEROC_AGENT_RUN_TIMEOUT_SECONDS=100
```

`CAREEROC_AGENT_PROVIDER` selects the specialists: `deterministic` (the default in Compose and the demo, no model needed), `ollama`, or `openai`.
The deterministic suite runs the same graph and gate and labels its traces `deterministic/evidence-v1`.
LangGraph checkpoints are stored in PostgreSQL through a connection pool, in a database of their own; without one they live in memory.
The shared secret is required: the runtime refuses its internal endpoints when none is configured, and the API refuses to start without one outside Development.
Run the sidecar on a private service network.

## Interop

The runtime also serves an A2A 1.0 agent with the `a2a-sdk` package: the agent card at `/.well-known/agent-card.json` and JSON-RPC at `/a2a`.
Its two skills are `pipeline_status` and `career_review`.
A caller sends the user's careeroc personal access token; the runtime forwards it to the API's `/agent/v1/*` endpoints, so the API still decides what the caller may read, and the runtime never holds product database credentials.
Each A2A review runs on a fresh checkpoint thread (`a2a:<random id>`), separate from in-app reviews and from other callers.
Tokens are read-only, so an A2A review never pauses: when the gate wants a person, the answer says what is pending and that only the user can approve it in careeroc, and nothing is saved.
Assistants that prefer tools use the API's MCP server at `/mcp` with the same tokens.

## Version Policy

The runtime pins exact production dependency versions in `services/agent-runtime/pyproject.toml` and `uv.lock`.
Framework upgrades must regenerate the lock file and pass Python lint, Python graph tests, .NET tests, frontend checks, Docker health, and the end-to-end orchestrator smoke.

## Deferred Work

The graph proposes one kind of write, follow-up reminders, and the API applies it only after approval.
A graph that proposes outreach, resume edits, or application status changes must follow the same pattern: typed proposals, a gate that checks them against the snapshot, an interrupt, and an API that applies them after checking ownership.
The A2A task store is in memory; long-running skills need a database-backed task store.
LangGraph Platform and LangSmith are optional operational services, not runtime requirements.
