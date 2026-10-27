from __future__ import annotations

import asyncio
import os

import pytest
from httpx import ASGITransport, AsyncClient
from langgraph.checkpoint.memory import InMemorySaver

from careeroc_agent_runtime.app import create_app
from careeroc_agent_runtime.settings import AgentRuntimeSettings
from tests.test_graph import FakeAgentSuite, build_request

SECRET = {"X-Careeroc-Agent-Secret": "test-secret"}


async def test_review_endpoint_requires_configured_shared_secret() -> None:
    app = create_app(
        settings=AgentRuntimeSettings(shared_secret="test-secret"),
        suite=FakeAgentSuite(),
        checkpointer=InMemorySaver(),
    )
    async with app.router.lifespan_context(app):
        async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
            unauthorized = await client.post(
                "/v1/resume-mailbox-review",
                json=build_request().model_dump(mode="json", by_alias=True),
            )
            response = await client.post(
                "/v1/resume-mailbox-review",
                headers={"X-Careeroc-Agent-Secret": "test-secret"},
                json=build_request("review:test-run-003").model_dump(mode="json", by_alias=True),
            )

    assert unauthorized.status_code == 401
    assert response.status_code == 200
    assert response.json()["runtimePath"] == "langgraph.resume_mailbox_review.v1"


async def test_a_review_that_outlasts_its_budget_fails_before_the_api_gives_up() -> None:
    class HangingSuite(FakeAgentSuite):
        async def analyze(self, specialist, request):  # type: ignore[override]
            await asyncio.sleep(5)
            return await super().analyze(specialist, request)

    app = create_app(
        settings=AgentRuntimeSettings(shared_secret="test-secret", run_timeout_seconds=1),
        suite=HangingSuite(),
        checkpointer=InMemorySaver(),
    )
    async with app.router.lifespan_context(app):
        async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
            started = asyncio.get_running_loop().time()
            response = await client.post(
                "/v1/resume-mailbox-review",
                headers=SECRET,
                json=build_request("review:deadline-001").model_dump(mode="json", by_alias=True),
            )

    assert response.status_code == 503
    assert asyncio.get_running_loop().time() - started < 3


async def test_review_endpoint_refuses_everyone_without_a_configured_secret() -> None:
    app = create_app(
        settings=AgentRuntimeSettings(), suite=FakeAgentSuite(), checkpointer=InMemorySaver()
    )
    async with app.router.lifespan_context(app):
        async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
            response = await client.post(
                "/v1/resume-mailbox-review",
                headers={"X-Careeroc-Agent-Secret": ""},
                json=build_request().model_dump(mode="json", by_alias=True),
            )

    assert response.status_code == 401


@pytest.mark.asyncio
async def test_review_endpoint_uses_postgres_checkpoints_when_configured() -> None:
    database_url = os.getenv("CAREEROC_AGENT_TEST_DATABASE_URL")
    if not database_url:
        pytest.skip("CAREEROC_AGENT_TEST_DATABASE_URL is not configured")

    app = create_app(
        settings=AgentRuntimeSettings(
            checkpoint_database_url=database_url, shared_secret="test-secret"
        ),
        suite=FakeAgentSuite(),
    )
    async with app.router.lifespan_context(app):
        async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
            health = await client.get("/health")
            ready = await client.get("/health/ready")
            response = await client.post(
                "/v1/resume-mailbox-review",
                headers=SECRET,
                json=build_request("review:postgres-run-001").model_dump(
                    mode="json", by_alias=True
                ),
            )

    assert health.json()["checkpointMode"] == "postgres"
    assert ready.status_code == 200
    assert response.status_code == 200


async def test_decision_endpoint_resumes_only_a_paused_review() -> None:
    from careeroc_agent_runtime.deterministic import DeterministicAgentSuite

    app = create_app(
        settings=AgentRuntimeSettings(provider="deterministic", shared_secret="test-secret"),
        suite=DeterministicAgentSuite(),
        checkpointer=InMemorySaver(),
    )
    paused_request = build_request("review:decision-001").model_copy(update={"latest_resume": None})
    async with app.router.lifespan_context(app):
        async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
            nothing_waiting = await client.post(
                "/v1/resume-mailbox-review/review:never-started/decision",
                headers=SECRET,
                json={"approved": True},
            )
            paused = await client.post(
                "/v1/resume-mailbox-review",
                headers=SECRET,
                json=paused_request.model_dump(mode="json", by_alias=True),
            )
            decided = await client.post(
                "/v1/resume-mailbox-review/review:decision-001/decision",
                headers=SECRET,
                json={"approved": True, "note": "Looks right."},
            )
            again = await client.post(
                "/v1/resume-mailbox-review/review:decision-001/decision",
                headers=SECRET,
                json={"approved": False},
            )

    assert nothing_waiting.status_code == 409
    assert paused.json()["status"] == "review_required"
    assert decided.json()["status"] == "completed"
    assert decided.json()["trace"][-1]["reason"] == "Looks right."
    assert again.status_code == 409
