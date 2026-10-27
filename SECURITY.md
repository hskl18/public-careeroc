# Security Policy

This policy covers careeroc.

## Scope

This repository is the open-source reference implementation of careeroc.
Auth, data isolation, OAuth, personal access tokens, the agent runtime boundary, and admin operations are treated as security-sensitive.

Only the latest `main` is supported for security fixes.
The trust boundaries and failure modes are described in [DESIGN.md](DESIGN.md#trust-boundaries).

## Reporting

Do not open a public GitHub issue for vulnerabilities involving auth bypass, cross-user access, OAuth flows, tokens, or secrets.

Report them through GitHub's private vulnerability reporting: the **Security** tab of this repository, then **Report a vulnerability**.

When reporting a bug, include:

- affected environment and commit/branch
- exact endpoint, page, or workflow involved
- reproduction steps
- expected impact
- whether secrets, user data, or cross-user access were involved

## Current Controls

Identity and access:

- Google sign-in or the demo provider in `apps/web`, with protected workspace routes and an email or domain allowlist enforced in both the web app and the API
- a same-origin Next.js proxy for all browser-to-API traffic, with HMAC-signed user context using `CAREEROC_API_AUTH_SECRET` and an origin check on writes
- user-scoped ownership checks on every query and mutation, and `users.role` admin checks on `/api/admin/*`
- the Gmail OAuth callback reaches the API through the web app, signed as the signed-in user, and the API completes only a connection that same user started; return paths are limited to this site
- secure Auth.js cookie settings in production (`secure`, `httpOnly` where appropriate, `sameSite=lax`)

Assistants and agents:

- read-only personal access tokens for `/mcp` and `/agent/*`: SHA-256 hashed at rest, shown once, expiring after 1 to 365 days, at most 10 active per user, revocable, rechecked against the allowlist on each use, and refused from browser origins
- an agent runtime that receives bounded snapshots over a required shared secret and refuses every call when none is configured; it never holds careeroc's database credentials, raw email, or OAuth tokens, and keeps its checkpoints in a database of its own
- agents only propose writes: reminders from a review are created by the API after the user approves them and after it checks each application belongs to that user
- A2A callers are authorized by the API through the forwarded token, each caller sees only their own tasks, and A2A reviews never pause for or apply a decision
- the evidence gate keeps only citations it can find in the snapshot and reminder proposals for applications in it, scales confidence by the grounded share, and sends anything else to a person

Abuse and operations:

- per-user rate limits for signed traffic and per-IP limits for anonymous traffic; anonymous and rate-limited rejections are logged, not stored, so they cannot fill the database
- demo identities on a reserved `.example` domain that cannot connect Gmail or change backfill settings, an hourly cap on new demo workspaces, and deletion after 24 hours
- durable audit rows with bounded detail for signed mutations and admin actions, and model-call rows with tokens, cost, latency, and outcome but no prompts or email bodies
- one narrow admin repair action: retrying one failed processing job per confirmed, audited request
- production fail-fast checks for missing or weak secrets, development fallbacks, non-HTTPS public URLs, and a missing access policy
- the `local-compose-only-*` fallback secrets in `docker-compose.yml` are refused by the API and the web app once real accounts can sign in, and Compose publishes ports on 127.0.0.1 only
- corrections are written to the shared parser guidance file only when `Ollama__UserFeedbackFilePath` is set, which is meant for single-user installs

## Deployment Requirements

These controls are necessary but not sufficient on their own.
Production deployments should also enforce:

- HTTPS end to end, or trusted TLS termination directly in front of the app
- WAF/CDN or equivalent edge filtering
- secret storage in a real secret manager
- intentional secret rotation for `AUTH_SECRET`, `CAREEROC_API_AUTH_SECRET`, and the agent shared secret
- centralized log retention and redaction
- locked-down CORS origins
- a configured access allowlist, or demo mode, before the deployment is public
- the agent runtime on a private network, with its checkpoint database reachable only by its own role

## Known Limitations

- no full RBAC permission matrix; admin is a first-party `admin`/`user` role boundary
- the access allowlist is config-backed, not managed from an admin UI
- the MCP server uses static bearer tokens rather than OAuth 2.1 authorization
- the A2A task store is in memory, so task history is lost on restart
- no MFA or device/session management beyond Google account security
- edge abuse protection still depends on deployment infrastructure, not only application code
- one workspace-style auth model, not a multi-tenant admin product

## Secrets

Minimum expectations:

- use random secrets with at least 32 characters
- never expose `CAREEROC_API_AUTH_SECRET` to the browser
- do not commit real `.env` files
- do not reuse the `local-compose-only-*` values from `docker-compose.yml` anywhere other people can reach

## Verification

After changing auth, proxying, OAuth, tokens, or workflow mutation code, rerun at least:

```bash
pnpm --dir apps/web lint
pnpm --dir apps/web build
dotnet test Careeroc.sln
```

Then walk sign-in, the review queue, and application detail end to end, and run `scripts/smoke-demo.sh` against the Compose stack.
