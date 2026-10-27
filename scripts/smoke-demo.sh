#!/usr/bin/env bash
# Smoke test for a running careeroc stack in demo mode, used by CI for Docker Compose and Kubernetes.
# Walks the path a visitor and their assistant take: demo sign-in, the processed pipeline,
# a multi-agent review and its approval, a personal access token, MCP, A2A, and revoking the token.
#
#   WEB_URL=http://localhost:3000 API_URL=http://localhost:5114 AGENT_URL=http://localhost:8123 scripts/smoke-demo.sh
#
# SITE_ORIGIN is the web app's configured public origin (AUTH_URL); browsers send it as Origin on writes.
set -euo pipefail

WEB_URL=${WEB_URL:-http://localhost:3000}
API_URL=${API_URL:-http://localhost:5114}
AGENT_URL=${AGENT_URL:-http://localhost:8123}
SITE_ORIGIN=${SITE_ORIGIN:-$WEB_URL}

jar=$(mktemp)
trap 'rm -f "$jar"' EXIT

pass() { printf 'ok   %s\n' "$*"; }
fail() { printf 'FAIL %s\n' "$*" >&2; exit 1; }
# Reads JSON on stdin and prints a Python expression over it (bound to d).
json() { python3 -c "import json, sys; d = json.load(sys.stdin); print($1)"; }
web() { curl -fsS -b "$jar" -c "$jar" "$@"; }

curl -fsS -o /dev/null --retry 60 --retry-delay 2 --retry-all-errors "$WEB_URL/" || fail "web app at $WEB_URL"
curl -fsS -o /dev/null --retry 30 --retry-delay 2 --retry-all-errors "$API_URL/health/ready" || fail "API at $API_URL"
curl -fsS -o /dev/null --retry 30 --retry-delay 2 --retry-all-errors "$AGENT_URL/health/ready" || fail "agent runtime at $AGENT_URL"
pass "web, API, and agent runtime are up"

csrf=$(web "$WEB_URL/api/auth/csrf" | json 'd["csrfToken"]')
location=$(web -o /dev/null -w '%{redirect_url}' -X POST "$WEB_URL/api/auth/callback/demo" \
  --data-urlencode "csrfToken=$csrf" --data-urlencode "callbackUrl=$SITE_ORIGIN/app")
case "$location" in *error=*) fail "demo sign-in redirected to $location" ;; esac
email=$(web "$WEB_URL/api/auth/session" | json 'd["user"]["email"]')
case "$email" in *@demo.careeroc.example) pass "signed in as $email" ;; *) fail "unexpected session user: $email" ;; esac

kestrel=$(web "$WEB_URL/api/careeroc/api/applications/lookup?q=Kestrel&limit=1" | json 'len(d["items"])')
[ "$kestrel" -ge 1 ] || fail "the demo inbox was not processed into applications"
pass "demo inbox processed into applications"

review=$(web -d "" -H "Origin: $SITE_ORIGIN" "$WEB_URL/api/careeroc/api/orchestrator/resume-mailbox-review")
runtime=$(printf '%s' "$review" | json 'd["runtimePath"]')
status=$(printf '%s' "$review" | json 'd["status"]')
case "$runtime" in langgraph.*) ;; *) fail "review did not run on the LangGraph runtime: $runtime / $status" ;; esac
[ "$status" = review_required ] || fail "the demo review should wait for approval, got $status"
pass "multi-agent review waits for approval"

proposal=$(web "$WEB_URL/api/careeroc/api/profile/memory/proposals" | json 'next(e["id"] for e in d if e["kind"] == "orchestrator")')
created=$(web -X PATCH -H "Origin: $SITE_ORIGIN" -H 'content-type: application/json' -d '{"action":"confirm"}' \
  "$WEB_URL/api/careeroc/api/profile/memory/proposals/$proposal" | json 'json.loads(d["value"])["remindersCreated"]')
[ "$created" -ge 1 ] || fail "approving the review created no reminders"
pass "approved the review: $created reminders created"

created_token=$(web -X POST -H "Origin: $SITE_ORIGIN" -H 'content-type: application/json' \
  -d '{"name":"smoke test","expiresInDays":1}' "$WEB_URL/api/careeroc/api/profile/access-tokens")
token=$(printf '%s' "$created_token" | json 'd["token"]')
token_id=$(printf '%s' "$created_token" | json 'd["details"]["id"]')
case "$token" in coc_*) pass "created a personal access token" ;; *) fail "unexpected token format" ;; esac

mcp() {
  curl -sS -o "$2" -w '%{http_code}' "$API_URL/mcp" -H "Authorization: Bearer $1" \
    -H 'content-type: application/json' -H 'accept: application/json, text/event-stream' \
    -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_pipeline_summary","arguments":{}}}'
}
body=$(mktemp)
[ "$(mcp coc_not-a-real-token "$body")" = 401 ] || fail "MCP accepted an invalid token"
[ "$(mcp "$token" "$body")" = 200 ] || fail "MCP rejected a valid token: $(cat "$body")"
# Streamable HTTP may answer as JSON or as one server-sent event; take the JSON either way.
total=$(sed -n 's/^data: //p; /^{/p' "$body" | head -1 | json 'd["result"]["structuredContent"]["totalApplications"]')
rm -f "$body"
[ "$total" -ge 1 ] || fail "MCP pipeline summary is empty"
pass "MCP get_pipeline_summary: $total applications"

card=$(curl -fsS "$AGENT_URL/.well-known/agent-card.json" | json 'd["name"]')
[ "$card" = careeroc ] || fail "unexpected agent card name: $card"
for prompt in "Where do my applications stand?" "Review my search and suggest next steps."; do
  task=$(curl -fsS "$AGENT_URL/a2a" -H "Authorization: Bearer $token" -H 'A2A-Version: 1.0' \
    -H 'content-type: application/json' \
    -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"SendMessage\",\"params\":{\"message\":{\"messageId\":\"smoke\",\"role\":\"ROLE_USER\",\"parts\":[{\"text\":\"$prompt\"}]}}}")
  result=$(printf '%s' "$task" | json 'd["result"]["task"]["status"]["state"] + " " + d["result"]["task"]["artifacts"][0]["name"]')
  case "$result" in TASK_STATE_COMPLETED\ *) pass "A2A $result" ;; *) fail "A2A task for '$prompt': $result" ;; esac
done

# Browsers send a bodyless DELETE without Content-Length; the proxy must still forward it.
web -X DELETE -H "Origin: $SITE_ORIGIN" "$WEB_URL/api/careeroc/api/profile/access-tokens/$token_id" -o /dev/null
body=$(mktemp)
[ "$(mcp "$token" "$body")" = 401 ] || fail "MCP still accepted a revoked token"
rm -f "$body"
pass "revoked the token"

pass "smoke test passed"
