"""A2A 1.0 surface for the careeroc career agent.

A caller authenticates with the user's careeroc personal access token. The runtime
forwards that token to the careeroc API to read a bounded snapshot, so it never holds
database credentials or sees another user's data, then answers from the snapshot or
runs the multi-agent review graph on it. Both skills are read-only.
"""

from __future__ import annotations

import asyncio
import logging
import uuid
from collections.abc import Callable
from typing import Any

import httpx
from a2a.helpers import new_data_part, new_task_from_user_message, new_text_message, new_text_part
from a2a.server.agent_execution import AgentExecutor, RequestContext
from a2a.server.events import EventQueue
from a2a.server.tasks import TaskUpdater
from a2a.types import (
    AgentCapabilities,
    AgentCard,
    AgentInterface,
    AgentSkill,
    HTTPAuthSecurityScheme,
    SecurityRequirement,
    SecurityScheme,
    StringList,
)
from starlette.authentication import SimpleUser
from starlette.responses import JSONResponse
from starlette.types import ASGIApp, Receive, Scope, Send

from careeroc_agent_runtime.contracts import ResumeMailboxReviewRequest

logger = logging.getLogger(__name__)

REVIEW_KEYWORDS = ("review", "strategy", "advice", "advise", "next step", "should i", "improve")
SECURITY_SCHEME = "careerocToken"


def build_agent_card(public_url: str) -> AgentCard:
    base = public_url.rstrip("/")
    return AgentCard(
        name="careeroc",
        description=(
            "Read-only career agent for one person's job search: pipeline status from "
            "their recruiting inbox, and a multi-agent review of resume and outcomes."
        ),
        version="1.0.0",
        supported_interfaces=[
            AgentInterface(url=f"{base}/a2a", protocol_binding="JSONRPC", protocol_version="1.0")
        ],
        capabilities=AgentCapabilities(streaming=True, push_notifications=False),
        default_input_modes=["text/plain"],
        default_output_modes=["text/plain", "application/json"],
        security_schemes={
            SECURITY_SCHEME: SecurityScheme(
                http_auth_security_scheme=HTTPAuthSecurityScheme(
                    scheme="bearer",
                    bearer_format="careeroc personal access token (coc_...)",
                    description="Create one in careeroc under Settings, Assistant access.",
                )
            )
        },
        security_requirements=[SecurityRequirement(schemes={SECURITY_SCHEME: StringList(list=[])})],
        skills=[
            AgentSkill(
                id="pipeline_status",
                name="Pipeline status",
                description="Applications by status, open deadlines, and recent activity.",
                tags=["job search", "status", "deadlines"],
                examples=["Where do my applications stand?", "What deadlines are coming up?"],
            ),
            AgentSkill(
                id="career_review",
                name="Career review",
                description=(
                    "Resume, mailbox, and strategy specialists review the evidence in "
                    "parallel; a supervisor and an evidence gate decide what to recommend."
                ),
                tags=["job search", "multi-agent", "review"],
                examples=["Review my search and suggest next steps."],
            ),
        ],
    )


def select_skill(text: str) -> str:
    lowered = text.casefold()
    return (
        "career_review"
        if any(keyword in lowered for keyword in REVIEW_KEYWORDS)
        else "pipeline_status"
    )


def describe_pipeline(summary: dict[str, Any]) -> str:
    by_status = ", ".join(
        f"{count} {status}" for status, count in summary["applicationsByStatus"].items()
    )
    lines = [f"{summary['totalApplications']} applications: {by_status or 'none yet'}."]
    for reminder in summary["openReminders"][:5]:
        due = reminder["dueAtUtc"][:10]
        prefix = "Overdue" if reminder["isOverdue"] else "Due"
        lines.append(f"{prefix} {due}: {reminder['title']}")
    return "\n".join(lines)


class CallerAuthMiddleware:
    """Authenticates /a2a with the caller's careeroc token and makes the user the task owner.

    a2a-sdk scopes every stored task to request.user; without an authenticated user all callers
    share one empty owner and can list and read each other's tasks.
    """

    def __init__(
        self,
        app: ASGIApp,
        api_base_url: str,
        http_client_factory: Callable[[], httpx.AsyncClient],
    ) -> None:
        self._app = app
        self._api_base_url = api_base_url.rstrip("/")
        self._http_client_factory = http_client_factory

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http" or scope["path"] != "/a2a":
            await self._app(scope, receive, send)
            return

        headers = {
            key.decode("latin-1").lower(): value.decode("latin-1")
            for key, value in scope["headers"]
        }
        token = _bearer_token(headers)
        try:
            owner = await self._resolve_owner(token) if token else None
        except httpx.HTTPError:
            logger.warning("Could not reach the careeroc API to authenticate an A2A caller.")
            await JSONResponse({"error": "careeroc is unavailable."}, status_code=503)(
                scope, receive, send
            )
            return

        if owner is None:
            response = JSONResponse(
                {"error": "Send a valid careeroc personal access token as a bearer token."},
                status_code=401,
                headers={"WWW-Authenticate": 'Bearer realm="careeroc"'},
            )
            await response(scope, receive, send)
            return

        scope["user"] = SimpleUser(owner)
        await self._app(scope, receive, send)

    async def _resolve_owner(self, token: str) -> str | None:
        async with self._http_client_factory() as client:
            response = await client.get(
                f"{self._api_base_url}/agent/v1/me", headers={"Authorization": f"Bearer {token}"}
            )
        if response.status_code == httpx.codes.UNAUTHORIZED:
            return None
        response.raise_for_status()
        return response.json()["owner"]


class CareerocAgentExecutor(AgentExecutor):
    def __init__(
        self,
        api_base_url: str,
        graph_provider: Callable[[], Any],
        http_client_factory: Callable[[], httpx.AsyncClient] | None = None,
        run_timeout_seconds: float = 100,
    ) -> None:
        self._api_base_url = api_base_url.rstrip("/")
        self._graph_provider = graph_provider
        self._run_timeout_seconds = run_timeout_seconds
        self._http_client_factory = http_client_factory or (lambda: httpx.AsyncClient(timeout=30))

    async def execute(self, context: RequestContext, event_queue: EventQueue) -> None:
        task = context.current_task
        if task is None:
            task = new_task_from_user_message(context.message)
            await event_queue.enqueue_event(task)
        updater = TaskUpdater(event_queue, task.id, task.context_id)

        token = _bearer_token(context.call_context.state.get("headers", {}))
        if token is None:
            await updater.reject(
                new_text_message(
                    "Send a careeroc personal access token as a bearer token.",
                    context_id=task.context_id,
                )
            )
            return

        skill = select_skill(context.get_user_input())
        await updater.start_work(new_text_message(f"Running {skill}.", context_id=task.context_id))
        path = (
            "/agent/v1/pipeline-summary"
            if skill == "pipeline_status"
            else "/agent/v1/review-request"
        )
        try:
            async with self._http_client_factory() as client:
                response = await client.get(
                    f"{self._api_base_url}{path}", headers={"Authorization": f"Bearer {token}"}
                )
        except httpx.HTTPError:
            logger.warning("A2A %s could not reach the careeroc API.", skill)
            response = None
        if response is not None and response.status_code == httpx.codes.UNAUTHORIZED:
            await updater.reject(
                new_text_message("careeroc rejected the access token.", context_id=task.context_id)
            )
            return
        if response is None or not response.is_success:
            # Keep internal URLs and upstream errors out of the caller-visible message.
            await updater.failed(
                new_text_message(
                    "careeroc could not load your data right now.", context_id=task.context_id
                )
            )
            return

        if skill == "pipeline_status":
            summary = response.json()
            await updater.add_artifact(
                [
                    new_text_part(describe_pipeline(summary)),
                    new_data_part(summary, "application/json"),
                ],
                name="pipeline_status",
            )
        else:
            try:
                review = await self._run_review(response.json())
            except Exception:
                logger.exception("A2A career review failed.")
                await updater.failed(
                    new_text_message(
                        "The career review could not finish right now.", context_id=task.context_id
                    )
                )
                return
            await updater.add_artifact(
                [new_text_part(describe_review(review)), new_data_part(review, "application/json")],
                name="career_review",
            )
        await updater.complete()

    async def cancel(self, context: RequestContext, event_queue: EventQueue) -> None:
        task = context.current_task
        if task is not None:
            await TaskUpdater(event_queue, task.id, task.context_id).cancel()

    async def _run_review(self, payload: dict[str, Any]) -> dict[str, Any]:
        # A fresh checkpoint thread per run: callers choose context IDs, and a shared thread would
        # accumulate another caller's specialist outputs through the graph's list reducers.
        payload["threadId"] = f"a2a:{uuid.uuid4()}"
        # Tokens are read-only, so an A2A caller cannot approve anything; see describe_review.
        payload["humanReviewAvailable"] = False
        request = ResumeMailboxReviewRequest.model_validate(payload)
        async with asyncio.timeout(self._run_timeout_seconds):
            state = await self._graph_provider().ainvoke(
                {
                    "request": request.model_dump(mode="json", by_alias=True),
                    "analyses": [],
                    "trace": [],
                },
                config={"configurable": {"thread_id": request.thread_id}, "recursion_limit": 20},
            )
        return state["review"]


def describe_review(review: dict[str, Any]) -> str:
    if not review["requiresHumanReview"]:
        return review["summary"]
    proposals = len(review.get("proposedReminders", []))
    pending = (
        f"It proposes {proposals} reminders, which only you can approve in careeroc."
        if proposals
        else "The evidence gate wants a person to confirm it in careeroc before it is kept."
    )
    return f"{review['summary']} {pending} Nothing was saved."


def _bearer_token(headers: dict[str, str]) -> str | None:
    value = headers.get("authorization", "")
    if not value.lower().startswith("bearer "):
        return None
    token = value[7:].strip()
    return token or None
