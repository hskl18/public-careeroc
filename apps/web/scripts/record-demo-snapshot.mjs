// Records the API responses the hosted read-only demo serves, by running the real pipeline once.
//
//   export OLLAMA=http://host.docker.internal:11434   # a signed-in local Ollama, no key
//   Ollama__Enabled=true Ollama__BaseUrl=$OLLAMA Ollama__Model=gemma4:31b-cloud \
//     AGENT_PROVIDER=ollama AGENT_BASE_URL=$OLLAMA AGENT_MODEL=gpt-oss:120b-cloud \
//     CAREEROC_VERSION=snapshot-recording docker compose -p careeroc-snapshot up -d --build --wait api
//   node apps/web/scripts/record-demo-snapshot.mjs
//   docker compose -p careeroc-snapshot down -v
//
// Run it on an empty database: an existing workspace would carry over any earlier changes.
import { createHash, createHmac } from "node:crypto";
import { writeFileSync } from "node:fs";

const apiUrl = process.env.API_URL ?? "http://127.0.0.1:5114";
const secret = process.env.CAREEROC_API_AUTH_SECRET ?? "local-compose-only-api-signing-secret-0001";
const email = "alex.rivera@demo.careeroc.example";
const name = "Alex Rivera";
const outputUrl = new URL("../src/lib/demo-snapshot.json", import.meta.url);

const dashboardViews = ["needs-action", "needs-review", "active", "waiting", "interviewing", "offer", "rejected", "all"];
const analyticsRanges = ["all", "30d", "90d"];

async function call(method, pathAndQuery) {
  const timestamp = new Date().toISOString();
  const bodyHash = createHash("sha256").update("", "utf8").digest("hex");
  const signature = createHmac("sha256", secret)
    .update([method, pathAndQuery, email, name, timestamp, bodyHash].join("\n"))
    .digest("hex");
  const response = await fetch(`${apiUrl}${pathAndQuery}`, {
    method,
    headers: {
      "X-Careeroc-User-Email": email,
      "X-Careeroc-User-Name": name,
      "X-Careeroc-Timestamp": timestamp,
      "X-Careeroc-Body-SHA256": bodyHash,
      "X-Careeroc-Signature": signature,
    },
  });
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : null };
}

const responses = {};

async function record(method, pathAndQuery, key = `${method} ${pathAndQuery}`) {
  const result = await call(method, pathAndQuery);
  // An application may lack resume fit; that status is part of the demo.
  if (result.status >= 500 || result.status === 401) {
    throw new Error(`${method} ${pathAndQuery} returned ${result.status}`);
  }
  responses[key] = result;
  return result.body;
}

const workspace = await call("POST", "/api/demo/workspace");
if (workspace.status !== 200) {
  throw new Error(`Demo workspace provisioning returned ${workspace.status}`);
}
if (!workspace.body.created) {
  throw new Error("The demo workspace already exists. Record on an empty database.");
}

// The welcome toast sends visitors to Gmail setup, which the hosted demo cannot offer.
await call("POST", "/api/profile/welcome-dismissed");

for (const path of [
  "/api/profile",
  "/api/profile/memory",
  "/api/profile/memory/proposals",
  "/api/profile/access-tokens",
  "/api/inbox-accounts",
  "/api/review",
  "/api/recruiting-contacts?limit=50",
]) {
  await record("GET", path);
}
await record("POST", "/api/dashboard/inbox-check");

for (const range of analyticsRanges) {
  await record("GET", range === "all" ? "/api/analytics" : `/api/analytics?range=${range}`);
}

let applications = [];
for (const view of dashboardViews) {
  const dashboard = await record("GET", `/api/dashboard?view=${view}&limit=250`, `GET /api/dashboard?view=${view}`);
  if (dashboard.nextCursor) {
    throw new Error(`The ${view} view has more than one page.`);
  }
  if (view === "all") {
    applications = dashboard.applications;
  }
}
if (applications.length === 0) {
  throw new Error("The demo inbox was not processed into applications.");
}
const inbox = responses[`GET /api/dashboard?view=${dashboardViews[0]}`].body.inbox;
if (inbox.backfillStatus !== "complete" || inbox.lastAnalysisError) {
  throw new Error(`The demo inbox did not finish processing: ${inbox.backfillStatus}, ${inbox.lastAnalysisError}`);
}

for (const { id } of applications) {
  await record("GET", `/api/applications/${id}`);
  await record("GET", `/api/applications/${id}/resume-fit`);
}

await record("GET", "/api/resumes");

// Analytics links filter by canonical role, which application cards do not carry.
const applicationIdsByRole = {};
for (const { label } of responses["GET /api/analytics"].body.roleBreakdown) {
  const { body } = await call("GET", `/api/dashboard?view=all&limit=250&roleCanonical=${encodeURIComponent(label)}`);
  applicationIdsByRole[label] = body.applications.map(({ id }) => id);
}

const snapshot = JSON.stringify({ recordedAt: new Date().toISOString(), applicationIdsByRole, responses }, null, 2);
// The API describes a per-visitor workspace, which the hosted demo is not.
writeFileSync(outputUrl, `${snapshot.replaceAll("a demo workspace that is deleted after 24 hours", "a read-only demo workspace")}\n`);
console.log(`Recorded ${Object.keys(responses).length} responses from ${applications.length} applications.`);
