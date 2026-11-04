#!/usr/bin/env bash
# Development loop: Postgres and the agent runtime in Docker (built from this checkout), the API and
# the web app from source with prefixed logs. Settings come from .env (copy .env.example).
# Ctrl-C stops the API and web app; `docker compose stop` stops the containers.
set -euo pipefail
cd "$(dirname "$0")/.."

# .env values can contain ';' (connection strings), so it is parsed line by line instead of sourced.
if [ -f .env ]; then
  while IFS= read -r line || [ -n "$line" ]; do
    line="${line%$'\r'}"
    case "$line" in "" | \#*) continue ;; esac
    key="${line%%=*}"
    [[ "$line" == *=* && "$key" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] && export "$key=${line#*=}"
  done < .env
fi

WEB_PORT="${PORT:-3000}"
API_PORT="${API_PORT:-5114}"
export API_BASE_URL="http://localhost:${API_PORT}" API_PUBLIC_BASE_URL="http://localhost:${API_PORT}"
export AUTH_URL="http://localhost:${WEB_PORT}" NEXT_PUBLIC_SITE_URL="http://localhost:${WEB_PORT}"
export ConnectionStrings__Careeroc="${ConnectionStrings__Careeroc:-Host=localhost;Port=5432;Database=careeroc;Username=postgres;Password=postgres}"
export AgentRuntime__Enabled="${AgentRuntime__Enabled:-true}"
export AgentRuntime__BaseUrl="${AgentRuntime__BaseUrl:-http://localhost:8123}"
# The runtime container and the API must share this secret, or every review silently falls back.
export AgentRuntime__SharedSecret="${AgentRuntime__SharedSecret:-local-compose-only-agent-shared-secret-01}"
export AGENT_SHARED_SECRET="$AgentRuntime__SharedSecret"

docker compose up -d --build --wait postgres agent-runtime

trap 'trap - EXIT; kill 0' EXIT INT TERM
dotnet run --project apps/api --urls "http://localhost:${API_PORT}" 2>&1 | sed -u 's/^/[api] /' &
pnpm --dir apps/web dev --port "$WEB_PORT" 2>&1 | sed -u 's/^/[web] /' &
echo "careeroc is starting at http://localhost:${WEB_PORT}"
wait
