#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

WEB_PORT="${CAREEROC_E2E_WEB_PORT:-3000}"
API_PORT="${CAREEROC_E2E_API_PORT:-5114}"
AGENT_PORT="${CAREEROC_E2E_AGENT_PORT:-8123}"
WEB_URL="http://localhost:${WEB_PORT}"
API_URL="http://localhost:${API_PORT}"
AGENT_URL="http://127.0.0.1:${AGENT_PORT}"
AUTH_SECRET="careeroc-e2e-auth-secret-keep-this-fixture-only"
AGENT_SECRET="careeroc-e2e-agent-secret"
CONNECTION_STRING="${CAREEROC_E2E_CONNECTION_STRING:-}"
ARTIFACT_DIR="$ROOT_DIR/.artifacts/e2e/multi-agent"

if [ -z "$CONNECTION_STRING" ]; then
  echo "CAREEROC_E2E_CONNECTION_STRING is required and must point to a disposable PostgreSQL database." >&2
  exit 64
fi

if printf '%s' "$CONNECTION_STRING" | grep -Eqi 'Database=careeroc(;|$)'; then
  echo "Refusing to run against the default Careeroc database. Use a disposable E2E database." >&2
  exit 64
fi

for command_name in curl dotnet node pnpm; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "$command_name is required." >&2
    exit 1
  fi
done

mkdir -p "$ARTIFACT_DIR"
rm -f "$ARTIFACT_DIR"/*.log

declare -a pids=()

cleanup() {
  local status=$?
  trap - EXIT INT TERM

  for pid in "${pids[@]:-}"; do
    pkill -TERM -P "$pid" 2>/dev/null || true
    kill "$pid" 2>/dev/null || true
  done
  wait || true

  if [ "$status" -ne 0 ]; then
    for log_file in "$ARTIFACT_DIR"/*.log; do
      [ -f "$log_file" ] || continue
      echo "Last 80 lines from $log_file:" >&2
      tail -n 80 "$log_file" >&2
    done
  fi

  exit "$status"
}

trap cleanup EXIT INT TERM

port_is_listening() {
  local port=$1

  if command -v lsof >/dev/null 2>&1; then
    lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1
    return
  fi

  if command -v ss >/dev/null 2>&1; then
    ss -ltn "sport = :$port" 2>/dev/null | tail -n +2 | grep -q .
    return
  fi

  return 1
}

for port in "$WEB_PORT" "$API_PORT" "$AGENT_PORT"; do
  if port_is_listening "$port"; then
    echo "Port $port is already in use. Stop the existing process or choose a CAREEROC_E2E_*_PORT override." >&2
    exit 1
  fi
done

wait_for_url() {
  local name=$1
  local url=$2
  local pid=$3

  for _ in {1..120}; do
    if curl -fsS "$url" >/dev/null 2>&1; then
      return 0
    fi
    if ! kill -0 "$pid" 2>/dev/null; then
      echo "$name stopped before becoming ready." >&2
      return 1
    fi
    sleep 1
  done

  echo "$name did not become ready at $url." >&2
  return 1
}

CAREEROC_E2E_AGENT_PORT="$AGENT_PORT" \
CAREEROC_E2E_AGENT_SECRET="$AGENT_SECRET" \
  node apps/web/e2e/fixtures/fake-agent-runtime.mjs >"$ARTIFACT_DIR/agent-fixture.log" 2>&1 &
agent_pid=$!
pids+=("$agent_pid")
wait_for_url "Agent fixture" "$AGENT_URL/health" "$agent_pid"

ASPNETCORE_ENVIRONMENT=Development \
DOTNET_ENVIRONMENT=Development \
ConnectionStrings__Careeroc="$CONNECTION_STRING" \
Database__ApplyMigrationsOnStartup=true \
SyncWorker__Enabled=false \
AgentRuntime__Enabled=true \
AgentRuntime__BaseUrl="$AGENT_URL" \
AgentRuntime__SharedSecret="$AGENT_SECRET" \
CAREEROC_API_AUTH_SECRET="$AUTH_SECRET" \
ACCESS_ALLOWED_EMAILS=demo@example.com \
Ollama__Enabled=false \
  dotnet run --project apps/api --no-launch-profile --urls "$API_URL" >"$ARTIFACT_DIR/api.log" 2>&1 &
api_pid=$!
pids+=("$api_pid")
wait_for_url "Careeroc API" "$API_URL/health/ready" "$api_pid"

AUTH_SECRET="$AUTH_SECRET" \
NEXTAUTH_SECRET="$AUTH_SECRET" \
DEMO_AUTH_EMAIL=demo@example.com \
  pnpm --dir apps/web e2e:auth:demo >"$ARTIFACT_DIR/auth-fixture.log" 2>&1

API_BASE_URL="$API_URL" \
API_PUBLIC_BASE_URL="$API_URL" \
AUTH_URL="$WEB_URL" \
NEXTAUTH_URL="$WEB_URL" \
AUTH_SECRET="$AUTH_SECRET" \
NEXTAUTH_SECRET="$AUTH_SECRET" \
CAREEROC_API_AUTH_SECRET="$AUTH_SECRET" \
ACCESS_ALLOWED_EMAILS=demo@example.com \
AUTH_GOOGLE_ID=e2e-unused-google-client \
AUTH_GOOGLE_SECRET=e2e-unused-google-secret \
  pnpm --dir apps/web dev --port "$WEB_PORT" >"$ARTIFACT_DIR/web.log" 2>&1 &
web_pid=$!
pids+=("$web_pid")
wait_for_url "Careeroc web" "$WEB_URL" "$web_pid"

CAREEROC_MULTI_AGENT_E2E=1 \
CAREEROC_E2E_AGENT_FIXTURE_URL="$AGENT_URL" \
PLAYWRIGHT_BASE_URL="$WEB_URL" \
PLAYWRIGHT_SKIP_WEB_SERVER=1 \
PLAYWRIGHT_STORAGE_STATE=e2e/.auth/demo-user.json \
  pnpm --dir apps/web e2e:multi-agent
