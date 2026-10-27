# Web E2E

Unauthenticated specs run with the default Playwright config.

Workspace specs need a stored NextAuth browser session.
For deterministic local screenshots that do not need Google OAuth:

1. Run `pnpm --dir apps/web e2e:auth:demo`.
2. Run authenticated specs with `PLAYWRIGHT_STORAGE_STATE=e2e/.auth/demo-user.json pnpm --dir apps/web e2e`.

For a real signed-in demo account:

1. Start the web app dependencies you need for the route under test.
2. Run `pnpm --dir apps/web e2e:auth`.
3. Sign in through the opened browser.
4. Keep the generated `apps/web/e2e/.auth/user.json` local.
5. Run authenticated specs with `PLAYWRIGHT_STORAGE_STATE=e2e/.auth/user.json pnpm --dir apps/web e2e`.

If Google OAuth expires or the session secret changes, regenerate the storage state with the same command.
An expired storage state usually redirects workspace routes to `/?next=/app`; treat that as an auth fixture problem, not as a dashboard route regression.

## Multi-agent system E2E

The dedicated multi-agent E2E runs the real browser, Next.js BFF, signed .NET API, and PostgreSQL persistence path against a sandbox agent-runtime contract fixture.
It verifies the sidecar request, the human-review gate, the rendered specialist and supervisor trace, and persistence after a browser reload.
It never calls a live model provider.

Point it at a disposable PostgreSQL database and run it from the repository root:

```bash
CAREEROC_E2E_CONNECTION_STRING='Host=localhost;Port=5432;Database=careeroc_e2e;Username=postgres;Password=postgres' pnpm e2e:multi-agent
```

The harness refuses the default `careeroc` database name, owns ports 3000, 5114, and 8123 for the duration of the run, and saves service logs under `.artifacts/e2e/multi-agent/`.
Playwright retains a trace, video, and screenshot when the spec fails.
