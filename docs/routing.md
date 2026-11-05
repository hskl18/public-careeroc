# Routes

Public pages, the signed-in workspace, and the API stay in separate namespaces.
New workspace pages go under `/app/*`; top-level aliases such as `/analytics` and `/review` only redirect.

## Web app

| Route | Purpose |
|---|---|
| `/` | Overview and the entry to the demo or Google sign-in |
| `/tech` | Architecture (`/technology` redirects here) |
| `/app` | Dashboard |
| `/app/applications` | Application explorer |
| `/app/applications/[applicationId]` | Evidence-first application detail |
| `/app/review` | Review queue for blocked automation |
| `/app/updates` | Activity timeline |
| `/app/analytics` | Funnel and outcome analytics |
| `/app/resume` | Pasted resume text, what careeroc read from it, and its evaluation |
| `/app/memory` | Memory, the multi-agent review, and approving or rejecting what it proposes |
| `/app/inbox` | Gmail connection, backfill, sync health, contacts, and mailbox controls |
| `/app/settings` | Candidate context, preferences, and assistant access tokens |
| `/app/settings/data` | Imported-data deletion and inbox purge |
| `/analytics`, `/review`, `/app/profile` | Redirect to `/app/analytics`, `/app/review`, and `/app/settings` |
| `/judge-demo`, `/applications`, `/resume`, `/settings`, `/notifications`, `/agents` | Redirect to `/`, `/app/applications`, `/app/resume`, `/app/settings`, `/app`, and `/tech`, so links to the retired sample demo keep working |

## API

Browsers call the same-origin proxy at `/api/careeroc/*`, which signs the user context and forwards to the .NET API.
The API's user-scoped endpoints are grouped by product noun:

```text
/api/dashboard, /api/dashboard/inbox-check
/api/inbox-accounts
/api/applications/*
/api/review
/api/analytics
/api/profile, /api/profile/memory, /api/profile/memory/proposals, /api/profile/access-tokens
/api/recruiting-contacts
/api/resumes
/api/orchestrator/resume-mailbox-review
/api/demo/workspace          (only when Demo:Enabled)
/api/inbox-accounts/gmail/oauth/callback   (reached through the web app's route of the same path)
```

Assistants use personal access tokens instead of the proxy:

```text
/mcp                         MCP server on the API
/agent/v1/*                  read-only snapshots on the API
/a2a                         A2A JSON-RPC on the agent runtime
/.well-known/agent-card.json A2A agent card on the agent runtime
```

The full request and response shapes are in the OpenAPI document at `/swagger` when the API runs in Development.
