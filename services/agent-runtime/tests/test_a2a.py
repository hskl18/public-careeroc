from __future__ import annotations

import httpx
import pytest
from langgraph.checkpoint.memory import InMemorySaver

from careeroc_agent_runtime.app import create_app
from careeroc_agent_runtime.deterministic import DeterministicAgentSuite
from careeroc_agent_runtime.settings import AgentRuntimeSettings
from tests.test_graph import build_request

TOKEN = "coc_test-token"
OTHER_TOKEN = "coc_other-token"
SUMMARY = {
    "totalApplications": 3,
    "applicationsByStatus": {"interview": 1, "applied": 2},
    "openReminders": [
        {
            "company": "Kestrel AI",
            "role": "Backend Engineer",
            "title": "Prepare for interview with Kestrel AI",
            "dueAtUtc": "2026-10-29T00:00:00+00:00",
            "isOverdue": False,
        }
    ],
    "recentlyActive": [],
    "generatedAtUtc": "2026-10-27T00:00:00+00:00",
}


def fake_careeroc_api(request: httpx.Request) -> httpx.Response:
    token = request.headers.get("authorization", "").removeprefix("Bearer ")
    if token not in (TOKEN, OTHER_TOKEN):
        return httpx.Response(401)
    if request.url.path == "/agent/v1/me":
        return httpx.Response(200, json={"owner": f"owner-of-{token}"})
    if request.url.path == "/agent/v1/pipeline-summary":
        return httpx.Response(200, json=SUMMARY)
    if request.url.path == "/agent/v1/review-request":
        return httpx.Response(200, json=build_request().model_dump(mode="json", by_alias=True))
    return httpx.Response(404)


def build_runtime():
    return create_app(
        settings=AgentRuntimeSettings(
            provider="deterministic",
            api_base_url="https://api.careeroc.test",
            public_url="https://agent.careeroc.test",
        ),
        suite=DeterministicAgentSuite(),
        checkpointer=InMemorySaver(),
        a2a_http_client_factory=lambda: httpx.AsyncClient(
            transport=httpx.MockTransport(fake_careeroc_api)
        ),
    )


async def call(
    client: httpx.AsyncClient, method: str, params: dict, token: str | None = TOKEN
) -> httpx.Response:
    headers = {"A2A-Version": "1.0"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return await client.post(
        "/a2a",
        headers=headers,
        json={"jsonrpc": "2.0", "id": 1, "method": method, "params": params},
    )


async def send(client: httpx.AsyncClient, text: str, token: str | None = TOKEN) -> dict:
    message = {"messageId": "m-1", "role": "ROLE_USER", "parts": [{"text": text}]}
    response = await call(client, "SendMessage", {"message": message}, token)
    assert response.status_code == 200
    return response.json()


@pytest.mark.asyncio
async def test_agent_card_declares_bearer_auth_and_both_skills() -> None:
    app = build_runtime()
    async with app.router.lifespan_context(app):
        transport = httpx.ASGITransport(app=app)
        async with httpx.AsyncClient(transport=transport, base_url="http://runtime") as client:
            card = (await client.get("/.well-known/agent-card.json")).json()

    assert card["supportedInterfaces"][0]["url"] == "https://agent.careeroc.test/a2a"
    assert card["securitySchemes"]["careerocToken"]["httpAuthSecurityScheme"]["scheme"] == "bearer"
    assert [skill["id"] for skill in card["skills"]] == ["pipeline_status", "career_review"]


@pytest.mark.asyncio
async def test_pipeline_status_answers_from_the_users_snapshot() -> None:
    app = build_runtime()
    async with app.router.lifespan_context(app):
        transport = httpx.ASGITransport(app=app)
        async with httpx.AsyncClient(transport=transport, base_url="http://runtime") as client:
            result = await send(client, "Where do my applications stand?")

    task = result["result"]["task"]
    assert task["status"]["state"] == "TASK_STATE_COMPLETED"
    assert "3 applications" in task["artifacts"][0]["parts"][0]["text"]


@pytest.mark.asyncio
async def test_career_review_runs_the_multi_agent_graph() -> None:
    app = build_runtime()
    async with app.router.lifespan_context(app):
        transport = httpx.ASGITransport(app=app)
        async with httpx.AsyncClient(transport=transport, base_url="http://runtime") as client:
            result = await send(client, "Review my search and suggest next steps.")

    artifact = result["result"]["task"]["artifacts"][0]
    review = artifact["parts"][1]["data"]
    assert artifact["name"] == "career_review"
    assert review["modelPath"] == "deterministic/evidence-v1"
    assert review["threadId"].startswith("a2a:")
    # The run proposes a reminder, but an A2A caller cannot approve it, so nothing pauses or saves.
    assert review["status"] == "review_required"
    assert review["proposedReminders"]
    assert "only you can approve in careeroc" in artifact["parts"][0]["text"]
    assert "human_review" not in [step["stage"] for step in review["trace"]]


@pytest.mark.asyncio
async def test_requests_without_a_valid_token_are_rejected_before_any_task_exists() -> None:
    app = build_runtime()
    async with app.router.lifespan_context(app):
        transport = httpx.ASGITransport(app=app)
        async with httpx.AsyncClient(transport=transport, base_url="http://runtime") as client:
            missing = await call(client, "ListTasks", {}, token=None)
            wrong = await call(client, "ListTasks", {}, token="coc_wrong")

    for response in (missing, wrong):
        assert response.status_code == 401
        assert response.headers["www-authenticate"].startswith("Bearer")


@pytest.mark.asyncio
async def test_each_caller_sees_only_their_own_tasks() -> None:
    app = build_runtime()
    async with app.router.lifespan_context(app):
        transport = httpx.ASGITransport(app=app)
        async with httpx.AsyncClient(transport=transport, base_url="http://runtime") as client:
            created = await send(client, "Where do my applications stand?", token=TOKEN)
            task_id = created["result"]["task"]["id"]
            own = (await call(client, "ListTasks", {}, TOKEN)).json()
            others = (await call(client, "ListTasks", {}, OTHER_TOKEN)).json()
            stolen = (await call(client, "GetTask", {"id": task_id}, OTHER_TOKEN)).json()

    assert [task["id"] for task in own["result"]["tasks"]] == [task_id]
    assert others["result"].get("tasks", []) == []
    assert "error" in stolen


@pytest.mark.asyncio
async def test_api_failures_do_not_leak_internal_urls() -> None:
    def failing_api(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/agent/v1/me":
            return httpx.Response(200, json={"owner": "owner"})
        return httpx.Response(500, text="boom at https://api.careeroc.test/internal")

    app = create_app(
        settings=AgentRuntimeSettings(
            provider="deterministic",
            api_base_url="https://api.careeroc.test",
            public_url="https://agent.careeroc.test",
        ),
        suite=DeterministicAgentSuite(),
        checkpointer=InMemorySaver(),
        a2a_http_client_factory=lambda: httpx.AsyncClient(
            transport=httpx.MockTransport(failing_api)
        ),
    )
    async with app.router.lifespan_context(app):
        transport = httpx.ASGITransport(app=app)
        async with httpx.AsyncClient(transport=transport, base_url="http://runtime") as client:
            result = await send(client, "Where do my applications stand?")

    task = result["result"]["task"]
    assert task["status"]["state"] == "TASK_STATE_FAILED"
    assert "careeroc.test" not in str(result)
