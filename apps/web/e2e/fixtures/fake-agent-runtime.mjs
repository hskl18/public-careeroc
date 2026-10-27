import { createServer } from "node:http";

const host = "127.0.0.1";
const port = Number(process.env.CAREEROC_E2E_AGENT_PORT ?? 8123);
const sharedSecret = process.env.CAREEROC_E2E_AGENT_SECRET ?? "careeroc-e2e-agent-secret";

let acceptedRequests = [];

function sendJson(response, statusCode, payload) {
  response.writeHead(statusCode, {
    "content-type": "application/json; charset=utf-8",
  });
  response.end(`${JSON.stringify(payload)}\n`);
}

async function readJson(request) {
  const chunks = [];
  for await (const chunk of request) {
    chunks.push(chunk);
  }

  return JSON.parse(Buffer.concat(chunks).toString("utf8"));
}

const server = createServer(async (request, response) => {
  if (request.method === "GET" && request.url === "/health") {
    sendJson(response, 200, { status: "ok", fixture: true });
    return;
  }

  if (request.method === "GET" && request.url === "/__e2e/state") {
    sendJson(response, 200, {
      acceptedRequestCount: acceptedRequests.length,
      lastRequest: acceptedRequests.at(-1) ?? null,
    });
    return;
  }

  if (request.method !== "POST" || request.url !== "/v1/resume-mailbox-review") {
    sendJson(response, 404, { error: "not_found" });
    return;
  }

  if (request.headers["x-careeroc-agent-secret"] !== sharedSecret) {
    sendJson(response, 401, { error: "invalid_agent_secret" });
    return;
  }

  try {
    const body = await readJson(request);
    acceptedRequests.push(body);

    sendJson(response, 200, {
      threadId: body.threadId,
      status: "review_required",
      runtimePath: "langgraph.resume_mailbox_review.v1",
      modelPath: "fixture/e2e-structured-output",
      summary: "The multi-agent review found a focused strategy that needs human approval.",
      findings: [
        "Resume evidence and mailbox outcomes were reviewed by separate specialists.",
        "The supervisor preserved an evidence-backed human review gate.",
      ],
      nextActions: [
        "Approve or reject the proposed strategy before it becomes confirmed memory.",
      ],
      requiresHumanReview: true,
      confidence: 0.88,
      trace: [
        {
          stage: "specialist",
          agentName: "resume_analyst",
          summary: "Reviewed the bounded resume snapshot.",
          reason: null,
          facts: [{ label: "resumeVersions", value: String(body.evidence.resumeVersions) }],
        },
        {
          stage: "specialist",
          agentName: "mailbox_analyst",
          summary: "Reviewed application outcomes without raw email bodies.",
          reason: null,
          facts: [{ label: "applications", value: String(body.evidence.totalApplications) }],
        },
        {
          stage: "specialist",
          agentName: "strategy_analyst",
          summary: "Built next actions from the specialist evidence.",
          reason: null,
          facts: [],
        },
        {
          stage: "supervisor",
          agentName: "career_supervisor",
          summary: "Synthesized the specialist reports and applied the evidence gate.",
          reason: "A person must approve this strategy before memory reuse.",
          facts: [{ label: "reviewGate", value: "human review required" }],
        },
      ],
    });
  } catch (error) {
    sendJson(response, 400, {
      error: "invalid_json",
      detail: error instanceof Error ? error.message : String(error),
    });
  }
});

server.listen(port, host, () => {
  console.log(`Careeroc E2E agent fixture listening on http://${host}:${port}`);
});

function shutdown() {
  server.close(() => process.exit(0));
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
