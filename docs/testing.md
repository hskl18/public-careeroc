# Testing

How to run each suite locally, what CI runs, and how far the evals go.

You need the .NET SDK from `global.json`, Node 26 with pnpm 10, and Python 3.14 with uv; `pnpm install` once first.

```bash
dotnet test Careeroc.sln                       # .NET: pipeline, API, MCP, tokens, evals
pnpm --dir apps/web lint && pnpm --dir apps/web test:unit && pnpm web:build
cd services/agent-runtime && uv run ruff check . && uv run ruff format --check . && uv run pytest
```

Set `CAREEROC_TEST_POSTGRES` to a Postgres connection string to also run the tests that need real Postgres behavior.

Two CI jobs start the whole system and drive it like a visitor and their assistant would, with [scripts/smoke-demo.sh](../scripts/smoke-demo.sh): demo sign-in, the processed pipeline, a multi-agent review and its approval, a personal access token, MCP, A2A, and revoking the token.
One runs it on Docker Compose; the other installs the Helm chart on a kind Kubernetes cluster ([scripts/smoke-kubernetes.sh](../scripts/smoke-kubernetes.sh)).
A third drives the review in Chromium against the real API with a scripted agent runtime ([scripts/e2e-multi-agent.sh](../scripts/e2e-multi-agent.sh)).
The frontend job also builds the hosted demo with `DEMO_SNAPSHOT` and walks it in Chromium on desktop and phone sizes, including the refused writes ([hosted-demo.spec.ts](../apps/web/e2e/hosted-demo.spec.ts)).
CI also validates the Helm chart with `helm lint` and `kubeconform`.

The pipeline eval in [eval/pipeline-results.json](../eval/pipeline-results.json) is 1.0 on every metric.
The labeled set was written while fixing the defects it found, so treat it as a regression guard rather than a benchmark.
The evidence-gate eval and its cases are in [DESIGN.md](../DESIGN.md#evals).
