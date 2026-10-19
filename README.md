# CareerOC

[![CareerOC demo checks](https://github.com/hskl18/public-careeroc/actions/workflows/public-ci.yml/badge.svg)](https://github.com/hskl18/public-careeroc/actions/workflows/public-ci.yml)

![CareerOC judge demo: recruiting mailbox evidence becomes a review-gated application pipeline](docs/media/judge-demo.png)

CareerOC is the public name for this recruiting-inbox workflow.
It turns bounded email evidence into proposed application updates, then requires review before risky changes reach tracker state.
The separately hosted product lives at `careeroc.com`; this repository is its sanitized hackathon demo and does not contain the hosted product source.

![CareerOC architecture: bounded mailbox evidence, agent extraction, review gate, and application state](docs/media/architecture.png)

Project links:

- [Public judge demo](https://public-careeros.vercel.app/judge-demo)
- [Demo video](https://youtu.be/PTrMwv0bF4E)
- [Hosted product](https://www.careeroc.com)
- [Source repository](https://github.com/hskl18/public-careeroc)

Run it with:

```bash
pnpm install
pnpm dev
```

Then open `http://localhost:3000/judge-demo` for the credential-free judge
demo. No Gmail account, Ollama Cloud key, hosted database, Docker runtime, or
model download is required to inspect the full agentic workflow.

What judges should see immediately:

- sanitized mailbox evidence
- Gemma via Ollama Cloud as the optional model path
- deterministic fallback when no key is configured
- evidence -> extraction -> review gate -> application/reminder loop
- model traces and review gates instead of a generic chatbot transcript

## 20-second reviewer path

1. Open the [credential-free judge demo](https://public-careeros.vercel.app/judge-demo).
2. Follow one sanitized recruiter thread from evidence to a typed proposal.
3. Confirm that the review gate blocks the proposed update before tracker mutation.
4. Inspect the deterministic fixture result and the open error analysis below.

| Judging signal | What this repo proves |
| --- | --- |
| Real-world impact | Job-search state is trapped in recruiting email; CareerOC turns it into reminders, application stages, and reviewable actions. |
| Agent path | Bounded triage, extraction, evidence review, resume context, and notification layers produce inspectable handoffs rather than a chat transcript. |
| Safety boundary | Risky updates stop at review gates; traces retain provider, confidence, bounded evidence, and fallback status without raw Gmail bodies or provider keys. |
| Reproducibility | `pnpm eval:generate && pnpm eval:pipeline` regenerates 130 labeled deterministic fixtures, metrics, error cases, JSON results, and the proof graph. |
| Current result | 118/130 contracts pass. All 12 failures are false review routes for recruiter replies; unsafe automatic mutation remains 0%. |

## Eval Proof

![CareerOC pipeline eval results](docs/media/eval-results.png)

`pnpm eval:pipeline` runs 130 versioned, sanitized synthetic fixtures across application receipts, recruiter replies, assessments, interviews, offers, rejections, ambiguous updates, suspicious jobs, adversarial content, and non-recruiting noise.
Current deterministic result: **118/130 passed** with a 90.8% contract pass rate and a 95% Wilson interval of 84.6% to 94.6%.
The runner observed 0 unsafe automatic mutations.
It also found 12 false review routes in recruiter-reply cases, which remain checked in as error-analysis evidence.

The eval writes machine-readable results to `eval/results.json` and renders the
graph with `tools/render_eval_graph.py` using Python/matplotlib. If a local
machine does not have matplotlib, run `python3 -m pip install matplotlib` before
regenerating the graph. GitHub Actions installs it before `pnpm ci:public`.

This is a deterministic product-loop regression eval, not a live-model or open-domain accuracy benchmark.
No provider API is called, so observed model cost is $0 by construction and is reported separately from any future live-model run.
See [evaluation methodology and error analysis](docs/eval.md) and the [end-to-end case study](docs/case-study.md).

## Local judge path

1. Clone the repo and run `pnpm install && pnpm dev`.
2. Open `http://localhost:3000/judge-demo`.
3. Watch the sanitized recruiter thread become an extracted application update.
4. Check the trace panel: the credential-free fixture names the deterministic provider, reports optional Gemma readiness separately, and stops risky state changes at review.
5. Open `/agents` for the agent contracts and can/cannot-do boundaries.

## Public Vercel Demo

The judge-facing deployment should be public and credential-free:

- deploy the Next.js app to Vercel with public access
- set no Gmail OAuth values unless you intentionally want live Gmail sync
- set no `OLLAMA_API_KEY` unless you intentionally want live Gemma checks
- keep `/judge-demo` as the primary Kaggle link
- optionally set `NEXT_PUBLIC_SITE_URL=https://your-vercel-domain`

The app renders the judge route without provider keys.
On Vercel, workspace state falls back to ephemeral `/tmp/.careeros-data` storage unless `CAREEROS_DATA_DIR` is set, so the public demo can render without a database or writable repo directory.
Without `OLLAMA_API_KEY`, model status shows deterministic fallback; the video
or local `pnpm smoke:ollama` can separately prove the live Ollama Cloud path.

Runtime pinning:

- `package.json` uses `"engines": { "node": "24.x" }` so Vercel stays on
  Node 24 instead of auto-upgrading to a future major Node release.
- `.node-version` also pins local and CI tooling to Node 24.

CareerOC turns recruiting email into structured application state with a multi-agent workflow:

1. Mailbox triage
2. Workflow extraction
3. Evidence and review gate
4. Resume/context analysis
5. Reminder and notification generation
6. Model routing through Gemma via Ollama Cloud

The implementation scope is one Next.js app with local JSON state, optional Gmail readonly sync, optional Ollama Cloud analysis, deterministic fallback, and no separate backend stack.

## Agent Contract

The public demo keeps the original CareerOC operating model:

| Agent layer | Can do | Cannot do |
| --- | --- | --- |
| Mailbox triage | Classify recruiting relevance, urgency, company, role, and action from bounded mailbox snippets | Store full inbox bodies or mutate application state |
| Workflow extraction | Propose typed application updates such as OA, interview, rejection, offer, deadline, or follow-up | Apply model output directly |
| Evidence/review | Attach bounded evidence, confidence, source message ids, and block risky/model-backed changes | Hide low-confidence changes from the user |
| Resume/context | Use local target roles, skills, preferences, and resume keywords as candidate context | Upload resume text to hosted providers in this demo |
| Reminder/notification | Derive due dates, follow-ups, review blockers, connector health, and model status from reviewed state | Become a second source of truth |
| Model router/provider | Use deterministic fallback first, or Gemma through Ollama Cloud when `OLLAMA_API_KEY` is configured | Store provider keys in workspace state, auto-download models, or mark roadmap providers as shipped |

Prompt and memory boundaries:

- Gemma prompts use bounded snippets and ask for strict JSON.
- Model output is schema-validated and review-gated before mutation.
- Agent memory is local state: applications, bounded evidence, review items,
  candidate context, compact correction facts, model traces, and notifications
  under `.careeros-data`.
- Review corrections feed back as compact local memory facts. Later imports and
  Gemma prompts can use those hints, but they still cannot bypass validation or
  review gates.
- Model traces keep provider, model tag, task, latency, confidence, fallback,
  and bounded diagnostics. They do not store raw prompts, raw responses, full
  Gmail bodies, OAuth tokens, or provider keys.
- First run has zero model cost and starts as a clean workspace. Connect
  readonly Gmail to create real pipeline records; use `/judge-demo` for the
  sanitized sample story without touching local workspace state.

## Quick Start

```bash
pnpm install
pnpm dev
```

Open:

```text
http://localhost:3000
```

First run opens a clean workspace. To process your real job pipeline, configure
Gmail OAuth, connect readonly Gmail, then sync recruiting mail. To see the
sanitized Kaggle story without credentials or local workspace writes, open
`/judge-demo`.

## Optional: Gemma Through Ollama Cloud

CareerOC calls the Ollama Cloud API from the Next.js server. Users do not set
up a desktop model runtime. Create an Ollama Cloud API key, then create
`.env.local`:

```bash
CAREEROS_OLLAMA_ENABLED=true
CAREEROS_OLLAMA_BASE_URL=https://ollama.com
CAREEROS_GEMMA_MODEL=gemma4:31b
OLLAMA_API_KEY=your-ollama-cloud-api-key
```

Restart `pnpm dev`, then open `/settings` and click **Save and verify Ollama Cloud API**.

This demo allows only the Ollama Cloud base URL `https://ollama.com`; server
code derives API calls under `https://ollama.com/api`.

For a real key-backed smoke test without starting the app:

```bash
pnpm smoke:ollama
```

`pnpm smoke:ollama` reads `.env.local` when present. Without an API key it
passes the public no-key gate with a `skipped_no_key` diagnostic; with a key it
checks the configured Gemma model through Ollama Cloud.

Mental model:

- `http://localhost:3000` is the CareerOC Next.js app.
- `https://ollama.com/api` is the remote Ollama Cloud API.
- The browser never sees `OLLAMA_API_KEY`; server routes read it from
  `.env.local`, call Ollama Cloud, validate JSON, and send proposals to review.

## Optional: Gmail Readonly Sync

Create a Google OAuth Web Application client:

- Authorized JavaScript origin: `http://localhost:3000`
- Authorized redirect URI:
  `http://localhost:3000/api/connectors/gmail/callback`
- Scope used by this demo: `https://www.googleapis.com/auth/gmail.readonly`

Add the local env values:

```bash
CAREEROS_GMAIL_CONNECTOR_ENABLED=true
CAREEROS_GMAIL_CLIENT_ID=your-google-oauth-client-id
CAREEROS_GMAIL_CLIENT_SECRET=your-google-oauth-client-secret
CAREEROS_GMAIL_REDIRECT_URI=http://localhost:3000/api/connectors/gmail/callback
CAREEROS_GMAIL_QUERY='newer_than:90d (recruiter OR application OR assessment OR interview OR "next steps" OR offer OR OA)'
CAREEROS_GMAIL_MAX_RESULTS=10
```

Restart `pnpm dev`, open `/settings?section=gmail`, and confirm the **Google
callback URL** shown in the app exactly matches the Google OAuth **Authorized
redirect URI**. `localhost`, `127.0.0.1`, port, and path must be identical.
Then click **Connect Gmail**, finish Google OAuth, and click **Sync recruiting
mail**.

Token boundary: the Gmail token is stored as a version 2 AES-GCM envelope at `.careeros-data/gmail-oauth.json`.
The envelope includes a key identifier but never stores key material or token plaintext.
Set `CAREEROS_TOKEN_KEY_ID` with `CAREEROS_TOKEN_SECRET` for an explicit current key.
During rotation, set `CAREEROS_TOKEN_PREVIOUS_KEY_ID` and `CAREEROS_TOKEN_PREVIOUS_SECRET` long enough for CareerOC to recover and atomically rewrite the envelope with the current key.
If neither key can recover the envelope, Settings shows `reconnect required` without exposing provider or token details.

Sync requests readonly Gmail metadata and bounded snippets, then converts them into import records without persisting full Gmail bodies.
Each request processes one bounded page, atomically checkpoints the next cursor with imported state, and can be paused or resumed after restart.
Message and thread merges are idempotent, so retrying a page suppresses duplicate evidence and review mutations.
The OAuth callback and sync state machine write compact audit events with redacted diagnostic codes only.
The local data directory is gitignored.
Do not commit real `.env.local`, Gmail data, screenshots with private email, or local state.

For the credential-free connector suite, run `pnpm build && pnpm e2e:gmail`.
The suite starts a loopback fake Gmail service and covers authorization, pagination, duplicate messages, thread updates, pause and restart recovery, rate limiting, reconnect, review decisions, export, delete, keyboard navigation, and mobile layout.

## Routes

| Route | Purpose |
| --- | --- |
| `/` | Local pipeline console |
| `/judge-demo` | Kaggle judge story and agent handoff demo |
| `/agents` | Agent contracts, local memory, prompting, and runtime boundaries |
| `/applications` | Application state and mailbox evidence |
| `/applications/[id]` | Application detail, timeline, evidence, review blockers |
| `/review` | Accept, dismiss, or correct review-gated updates |
| `/resume` | Deterministic + optional Gemma resume analysis |
| `/notifications` | Local notification queue |
| `/settings` | Ollama Cloud/Gemma, Gmail, local data, imports |
| `/api/pipeline` | Inspectable multi-agent pipeline JSON |
| `/api/providers` | Implemented/roadmap model provider metadata |
| `/api/version` | Server-owned package name and version diagnostics |
| Metrics API | Local effort metrics for future reporting surfaces |

## Repo Structure

```text
app/              Next.js App Router pages and route handlers
components/       Small reusable UI components
styles/           Layered global CSS split by surface and responsibility
lib/              Pipeline, persistence, provider, Gmail, review, and view helpers
lib/agent-constraints.ts
                  Machine-readable handoff, guardrail, prompt, trace, and review-gate constraints
public/           Agent and mascot SVG assets served by the app
docs/             Architecture, release, writeup, and submission docs
docs/media/       Curated judge/Kaggle media assets
eval/             Pipeline eval fixtures and generated results
tools/            Local validation and smoke-test entrypoints
tests/            Vitest coverage for pipeline, routes, persistence, and safety
scripts/          Public-release safety checks
```

## Local Data

Default state is stored in:

```text
.careeros-data/state.json
```

Use `/settings?section=local-data` to export, import, or delete local data.
Delete requires the exact confirmation phrase. You can also delete
`.careeros-data` while the dev server is stopped; the next app read recreates
an empty workspace.

## Commands

```bash
pnpm dev       # run the local Next.js app
pnpm check     # TypeScript
pnpm test      # Vitest
pnpm build     # production build
pnpm smoke:browser # headless Chrome route/layout smoke
pnpm smoke:ollama  # no-key public gate, or live Ollama Cloud smoke with OLLAMA_API_KEY
pnpm eval:pipeline # dataset-mapped pipeline eval + graph output
pnpm ci:public # public CI gate without requiring provider secrets
pnpm release:check # public safety + check + test + build + browser + Ollama + diff check
pnpm run ci    # check + test + build
```

## Public Safety

Before publishing, verify:

- No real `.env*` files except `.env.example`
- No `.careeros-data`, local DBs, Gmail exports, OAuth tokens, or screenshots
  with private email
- No hosted provider dashboard URLs or local machine paths

## Docs

- [Security](SECURITY.md)
- [Architecture](docs/architecture.md)
- [API spec](docs/api-spec.md)
- [Browser smoke and screenshot proof](docs/browser-smoke.md)
- [Pipeline eval proof](docs/eval.md)
- [Gemma pipeline diagram](docs/media/architecture.png)
- [Design](docs/design.md)
- [Public media assets](docs/media/)
- [Hackathon writeup](docs/hackathon-writeup.md)
- [Roadmap](docs/roadmap.md)
- [Release summary](docs/release-summary.md)

## License

MIT — see [LICENSE](LICENSE).
