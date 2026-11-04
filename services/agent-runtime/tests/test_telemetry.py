from __future__ import annotations

import pytest
from langgraph.checkpoint.memory import InMemorySaver
from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import SimpleSpanProcessor
from opentelemetry.sdk.trace.export.in_memory_span_exporter import InMemorySpanExporter

from careeroc_agent_runtime.deterministic import DeterministicAgentSuite
from careeroc_agent_runtime.graph import build_graph
from tests.test_graph import build_request

EXPORTER = InMemorySpanExporter()


@pytest.fixture(scope="module", autouse=True)
def tracer_provider() -> None:
    provider = TracerProvider()
    provider.add_span_processor(SimpleSpanProcessor(EXPORTER))
    trace.set_tracer_provider(provider)


@pytest.mark.asyncio
async def test_each_agent_emits_a_gen_ai_invoke_agent_span() -> None:
    EXPORTER.clear()
    graph = build_graph(DeterministicAgentSuite(), checkpointer=InMemorySaver())
    request = build_request("review:telemetry-001")

    await graph.ainvoke(
        {"request": request.model_dump(mode="json", by_alias=True), "analyses": [], "trace": []},
        config={"configurable": {"thread_id": request.thread_id}},
    )

    spans = {span.name: span for span in EXPORTER.get_finished_spans()}
    assert set(spans) >= {
        "invoke_agent resume_analyst",
        "invoke_agent mailbox_analyst",
        "invoke_agent strategy_analyst",
        "invoke_agent career_supervisor",
    }
    supervisor = spans["invoke_agent career_supervisor"].attributes
    assert supervisor["gen_ai.operation.name"] == "invoke_agent"
    assert supervisor["gen_ai.provider.name"] == "deterministic"
    assert supervisor["gen_ai.request.model"] == "evidence-v1"
    assert supervisor["gen_ai.conversation.id"] == "review:telemetry-001"
