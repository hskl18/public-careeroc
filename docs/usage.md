# Running careeroc

Everything past the [README](../README.md)'s quick start: building from source, traces, connecting your own assistant, and running on a real model or your own Gmail.

## Build from source and see traces

With `--build`, compiling all three services from source takes several minutes and uses every CPU core Docker is allowed; lower that limit in Docker Desktop under **Settings > Resources** if your machine slows down.

To see traces, start Jaeger as well and open http://localhost:16686:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://jaeger:4318 \
  docker compose --profile full --profile observability up -d
```

## Connect your own assistant

In the app, open **Settings**, create a token under **Connect your own assistant**, and copy it.

MCP, for example in Claude Code:

```bash
claude mcp add --transport http careeroc http://localhost:5114/mcp \
  --header "Authorization: Bearer coc_..."
```

The server exposes three read-only tools: `get_pipeline_summary`, `list_applications`, and `get_application`.

A2A: the agent card is at http://localhost:8123/.well-known/agent-card.json, with two skills, `pipeline_status` and `career_review`.

```bash
curl -s http://localhost:8123/a2a \
  -H "Authorization: Bearer coc_..." -H "A2A-Version: 1.0" -H "content-type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"messageId":"m1","role":"ROLE_USER","parts":[{"text":"Review my search and suggest next steps."}]}}}'
```

Tokens are read-only, expire after 1 to 365 days, are stored only as SHA-256 hashes, and can be revoked at any time.

On Linux, when the API runs from source through `scripts/dev-up.sh`, the agent-runtime container cannot reach an API bound to `localhost`, so its A2A token checks fail.
Put the API on the Docker bridge address as well, as the script's comment shows, and never on `0.0.0.0`, which would expose the Development API to the network:

```bash
API_URLS="http://localhost:5114;http://172.17.0.1:5114" ./scripts/dev-up.sh
```

## Use a real model or your own Gmail

- **Model.** Free Ollama models run both steps with no API key, after `ollama signin`; this is how the hosted demo was recorded:

  ```bash
  export OLLAMA=http://host.docker.internal:11434   # Docker Desktop
  Ollama__Enabled=true Ollama__BaseUrl=$OLLAMA Ollama__Model=gemma4:31b-cloud \
    AGENT_PROVIDER=ollama AGENT_BASE_URL=$OLLAMA AGENT_MODEL=gpt-oss:120b-cloud \
    docker compose --profile full up -d
  ```

  `Ollama__*` sets the pass on each email, which sends the email's text to that model, so use it on real mail only with a provider you trust.
  `AGENT_*` sets the review agents, which see only the bounded snapshot; set `AGENT_PROVIDER=openai` and `AGENT_API_KEY` for an OpenAI-compatible endpoint.
  The free models differ on email: [DESIGN.md](../DESIGN.md#evals) has their scores, and `scripts/eval-model.sh` reproduces them.
  The rules and the deterministic suite stay the fallback.
- **Gmail.** Create a Google OAuth client with two redirect URIs, `<web URL>/api/auth/callback/google` for sign-in and the Gmail one from `.env.example`, copy `.env.example` to `.env`, set `AUTH_GOOGLE_ID` and `AUTH_GOOGLE_SECRET` for the web app, `Google__ClientId` and `Google__ClientSecret` for the API, and an access allowlist (`ACCESS_ALLOWED_EMAILS`, with `ACCESS_ALLOW_ALL_USERS=false`), then run `./scripts/dev-up.sh`, because Compose runs the demo only.
  Enable the Gmail API in the Google Cloud project, and add yourself as a test user on the OAuth consent screen.
  Before you do, replace the `local-compose-only-*` secrets: the API and web app refuse them once real accounts can sign in.
  `dev-up.sh` runs the API with `dotnet run` and the web app with `pnpm`, so it needs the .NET SDK from `global.json`, Node with pnpm, and a finished `pnpm install`; [testing.md](testing.md) lists the versions.
  If the Compose demo is still running, stop its `api` and `web` services first, which hold ports 5114 and 3000, without removing volumes: `docker compose stop api web`.
