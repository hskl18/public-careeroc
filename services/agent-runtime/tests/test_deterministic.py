from __future__ import annotations

import pytest
from langgraph.checkpoint.memory import InMemorySaver

from careeroc_agent_runtime.deterministic import DeterministicAgentSuite
from careeroc_agent_runtime.graph import build_graph
from tests.test_graph import build_request


@pytest.mark.asyncio
async def test_deterministic_suite_runs_the_graph_with_grounded_claims() -> None:
    graph = build_graph(DeterministicAgentSuite(), checkpointer=InMemorySaver())
    request = build_request("review:deterministic-001")
    request.applications[0].has_open_reminder = True

    state = await graph.ainvoke(
        {"request": request.model_dump(mode="json", by_alias=True), "analyses": [], "trace": []},
        config={"configurable": {"thread_id": request.thread_id}},
    )
    review = state["review"]

    assert review["modelPath"] == "deterministic/evidence-v1"
    assert [step["stage"] for step in review["trace"]].count("parallel_analysis") == 3
    assert any("1 of 3 applications" in finding for finding in review["findings"])
    assert review["nextActions"]
    assert review["status"] == "completed"


@pytest.mark.asyncio
async def test_deterministic_suite_flags_missing_resume_for_human_review() -> None:
    graph = build_graph(DeterministicAgentSuite(), checkpointer=InMemorySaver())
    request = build_request("review:deterministic-002").model_copy(update={"latest_resume": None})

    state = await graph.ainvoke(
        {"request": request.model_dump(mode="json", by_alias=True), "analyses": [], "trace": []},
        config={"configurable": {"thread_id": request.thread_id}},
    )

    assert state["review"]["requiresHumanReview"] is True


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("approved", "expected_status"), [(True, "completed"), (False, "rejected")]
)
async def test_low_evidence_review_waits_for_a_human_decision(
    approved: bool, expected_status: str
) -> None:
    from langgraph.types import Command

    graph = build_graph(DeterministicAgentSuite(), checkpointer=InMemorySaver())
    request = build_request("review:deterministic-003").model_copy(update={"latest_resume": None})
    config = {"configurable": {"thread_id": request.thread_id}}

    paused = await graph.ainvoke(
        {"request": request.model_dump(mode="json", by_alias=True), "analyses": [], "trace": []},
        config=config,
    )
    assert paused["review"]["status"] == "review_required"
    assert (await graph.aget_state(config)).interrupts

    resumed = await graph.ainvoke(Command(resume={"approved": approved}), config=config)

    assert resumed["review"]["status"] == expected_status
    assert resumed["review"]["trace"][-1]["stage"] == "human_review"
    assert not (await graph.aget_state(config)).interrupts


@pytest.mark.asyncio
async def test_proposed_reminders_pause_the_run_until_a_person_decides() -> None:
    from langgraph.types import Command

    graph = build_graph(DeterministicAgentSuite(), checkpointer=InMemorySaver())
    request = build_request("review:deterministic-004")
    config = {"configurable": {"thread_id": request.thread_id}}

    paused = await graph.ainvoke(
        {"request": request.model_dump(mode="json", by_alias=True), "analyses": [], "trace": []},
        config=config,
    )
    proposals = paused["review"]["proposedReminders"]

    assert paused["review"]["status"] == "review_required"
    assert [proposal["applicationId"] for proposal in proposals] == ["7b0c6c1e-roblox"]
    assert (await graph.aget_state(config)).interrupts[0].value["proposedReminders"] == proposals

    resumed = await graph.ainvoke(Command(resume={"approved": True}), config=config)

    assert resumed["review"]["status"] == "completed"
    assert resumed["review"]["proposedReminders"] == proposals
