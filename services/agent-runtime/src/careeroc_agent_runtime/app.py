from __future__ import annotations

import asyncio
import hmac
import logging
from collections.abc import AsyncIterator, Callable
from contextlib import asynccontextmanager

import httpx
from a2a.server.request_handlers import DefaultRequestHandler
from a2a.server.routes import (
    add_a2a_routes_to_fastapi,
    create_agent_card_routes,
    create_jsonrpc_routes,
)
from a2a.server.tasks import InMemoryTaskStore
from fastapi import FastAPI, Header, HTTPException, Request, status
from langgraph.checkpoint.memory import InMemorySaver
from langgraph.checkpoint.postgres.aio import AsyncPostgresSaver
from langgraph.types import Command
from psycopg.rows import dict_row
from psycopg_pool import AsyncConnectionPool

from careeroc_agent_runtime.a2a_server import (
    CallerAuthMiddleware,
    CareerocAgentExecutor,
    build_agent_card,
)
from careeroc_agent_runtime.agents import AgentSuite, LangChainAgentSuite
from careeroc_agent_runtime.contracts import (
    ResumeMailboxReviewRequest,
    ResumeMailboxReviewResponse,
    ReviewDecision,
)
from careeroc_agent_runtime.deterministic import DeterministicAgentSuite
from careeroc_agent_runtime.graph import build_graph
from careeroc_agent_runtime.model_factory import build_chat_model
from careeroc_agent_runtime.settings import AgentRuntimeSettings
from careeroc_agent_runtime.telemetry import configure_tracing

logger = logging.getLogger(__name__)


def create_app(
    settings: AgentRuntimeSettings | None = None,
    suite: AgentSuite | None = None,
    checkpointer: object | None = None,
    a2a_http_client_factory: Callable[[], httpx.AsyncClient] | None = None,
) -> FastAPI:
    resolved_settings = settings or AgentRuntimeSettings()

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncIterator[None]:
        resolved_suite = suite or (
            DeterministicAgentSuite()
            if resolved_settings.provider.strip().lower() == "deterministic"
            else LangChainAgentSuite(
                build_chat_model(resolved_settings), resolved_settings.model_path
            )
        )

        app.state.model_path = resolved_suite.model_path
        if checkpointer is not None:
            app.state.checkpoint_mode = "injected"
            app.state.graph = build_graph(
                resolved_suite,
                checkpointer=checkpointer,
                minimum_confidence=resolved_settings.minimum_confidence,
                agent_timeout_seconds=resolved_settings.request_timeout_seconds,
            )
            yield
            return

        if resolved_settings.checkpoint_database_url:
            # A pool rather than one connection: concurrent reviews would otherwise share, and
            # queue on, a single connection.
            async with AsyncConnectionPool(
                resolved_settings.checkpoint_database_url,
                max_size=10,
                kwargs={"autocommit": True, "prepare_threshold": 0, "row_factory": dict_row},
                open=False,
            ) as pool:
                postgres_checkpointer = AsyncPostgresSaver(pool)
                await postgres_checkpointer.setup()
                app.state.checkpoint_mode = "postgres"
                app.state.checkpoint_pool = pool
                app.state.graph = build_graph(
                    resolved_suite,
                    checkpointer=postgres_checkpointer,
                    minimum_confidence=resolved_settings.minimum_confidence,
                    agent_timeout_seconds=resolved_settings.request_timeout_seconds,
                )
                yield
            return

        app.state.checkpoint_mode = "memory"
        app.state.graph = build_graph(
            resolved_suite,
            checkpointer=InMemorySaver(),
            minimum_confidence=resolved_settings.minimum_confidence,
            agent_timeout_seconds=resolved_settings.request_timeout_seconds,
        )
        yield

    runtime = FastAPI(
        title="Careeroc Agent Runtime",
        version="0.1.0",
        lifespan=lifespan,
    )

    configure_tracing(runtime)

    if resolved_settings.api_base_url:
        agent_card = build_agent_card(resolved_settings.public_url)
        executor = CareerocAgentExecutor(
            resolved_settings.api_base_url,
            lambda: runtime.state.graph,
            a2a_http_client_factory,
            resolved_settings.run_timeout_seconds,
        )
        # shortcut: tasks live in memory and vanish on restart; both skills are short and read-only.
        # Use a2a's DatabaseTaskStore when skills become long-running.
        handler = DefaultRequestHandler(
            agent_executor=executor,
            task_store=InMemoryTaskStore(),
            agent_card=agent_card,
        )
        add_a2a_routes_to_fastapi(
            runtime,
            agent_card_routes=create_agent_card_routes(agent_card),
            jsonrpc_routes=create_jsonrpc_routes(handler, rpc_url="/a2a"),
        )
        runtime.add_middleware(
            CallerAuthMiddleware,
            api_base_url=resolved_settings.api_base_url,
            http_client_factory=a2a_http_client_factory or (lambda: httpx.AsyncClient(timeout=30)),
        )

    def require_shared_secret(candidate: str | None) -> None:
        expected = resolved_settings.shared_secret
        # Fails closed: with no secret configured, the internal endpoints answer nobody.
        if not expected or candidate is None or not hmac.compare_digest(candidate, expected):
            raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Unauthorized")

    # Liveness: the process answers. Readiness, below, also needs the checkpoint database, so a
    # database outage takes the runtime out of rotation without restarting it.
    @runtime.get("/health")
    async def health(request: Request) -> dict[str, str]:
        return {
            "status": "ok",
            "runtimePath": "langgraph.resume_mailbox_review.v1",
            "modelPath": request.app.state.model_path,
            "checkpointMode": request.app.state.checkpoint_mode,
        }

    @runtime.get("/health/ready")
    async def ready(request: Request) -> dict[str, str]:
        pool = getattr(request.app.state, "checkpoint_pool", None)
        if pool is not None:
            try:
                async with pool.connection(timeout=3) as connection:
                    await connection.execute("SELECT 1")
            except Exception as exc:
                raise HTTPException(
                    status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                    detail="Checkpoint database is unreachable",
                ) from exc
        return {"status": "ready"}

    @runtime.post("/v1/resume-mailbox-review", response_model=ResumeMailboxReviewResponse)
    async def review_resume_mailbox(
        payload: ResumeMailboxReviewRequest,
        request: Request,
        x_careeroc_agent_secret: str | None = Header(default=None),
    ) -> ResumeMailboxReviewResponse:
        require_shared_secret(x_careeroc_agent_secret)
        try:
            async with asyncio.timeout(resolved_settings.run_timeout_seconds):
                state = await request.app.state.graph.ainvoke(
                    {
                        "request": payload.model_dump(mode="json", by_alias=True),
                        "analyses": [],
                        "trace": [],
                    },
                    config={
                        "configurable": {"thread_id": payload.thread_id},
                        "recursion_limit": 20,
                    },
                )
            return ResumeMailboxReviewResponse.model_validate(state["review"])
        except Exception as exc:
            logger.exception("LangGraph resume-mailbox review failed", exc_info=exc)
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="Agent graph execution failed",
            ) from exc

    @runtime.post(
        "/v1/resume-mailbox-review/{thread_id}/decision",
        response_model=ResumeMailboxReviewResponse,
    )
    async def decide_resume_mailbox_review(
        thread_id: str,
        decision: ReviewDecision,
        request: Request,
        x_careeroc_agent_secret: str | None = Header(default=None),
    ) -> ResumeMailboxReviewResponse:
        require_shared_secret(x_careeroc_agent_secret)
        graph = request.app.state.graph
        config = {"configurable": {"thread_id": thread_id}, "recursion_limit": 20}
        snapshot = await graph.aget_state(config)
        if not snapshot.interrupts:
            raise HTTPException(
                status_code=status.HTTP_409_CONFLICT,
                detail="This review is not waiting for a decision.",
            )

        state = await graph.ainvoke(
            Command(resume=decision.model_dump(mode="json", by_alias=True)), config=config
        )
        return ResumeMailboxReviewResponse.model_validate(state["review"])

    return runtime


app = create_app()
