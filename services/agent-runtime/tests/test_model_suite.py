"""Runs the model-backed suite through the real graph with a scripted chat model.

CI has no model provider, so this is what proves LangChainAgentSuite's wiring: the agents ask for
structured output, the graph runs them, and the gate still judges what they return.
"""

from __future__ import annotations

from typing import Any

import pytest
from langchain_core.language_models.chat_models import BaseChatModel
from langchain_core.messages import AIMessage
from langchain_core.outputs import ChatGeneration, ChatResult
from langgraph.checkpoint.memory import InMemorySaver
from langgraph.errors import GraphRecursionError
from pydantic import Field

from careeroc_agent_runtime.agents import LangChainAgentSuite
from careeroc_agent_runtime.contracts import SpecialistAnalysis
from careeroc_agent_runtime.graph import build_graph
from tests.test_graph import build_request

KNOWN_APPLICATION = "7b0c6c1e-roblox"


class ScriptedChatModel(BaseChatModel):
    """Answers with a structured-output tool call for whichever schema the agent bound."""

    tool_name: str = ""
    # Real models do this after a failed repair: an empty reply with no tool call, forever.
    silent_turns: int = 0
    turns: list[int] = Field(default_factory=list)

    @property
    def _llm_type(self) -> str:
        return "scripted"

    def bind_tools(self, tools: Any, **kwargs: Any) -> ScriptedChatModel:
        name = getattr(tools[0], "name", None) or tools[0]["function"]["name"]
        return self.model_copy(update={"tool_name": name})

    def _generate(self, messages: Any, stop: Any = None, run_manager: Any = None, **kwargs: Any):
        self.turns.append(1)
        if len(self.turns) <= self.silent_turns:
            return ChatResult(generations=[ChatGeneration(message=AIMessage(content=""))])
        if self.tool_name == "SpecialistAnalysis":
            args = {
                "agent_name": "model",
                "summary": "The model read the snapshot.",
                "findings": ["One application is interviewing."],
                "recommended_actions": ["Prepare for the interview."],
                "evidence_references": ["Roblox · interviewing"],
                "confidence": 0.9,
            }
        else:
            args = {
                "summary": "The model merged the specialists.",
                "findings": ["One application is interviewing."],
                "next_actions": ["Prepare for the interview."],
                "evidence_references": ["Roblox · interviewing", "Stripe · offer"],
                "proposed_reminders": [
                    {
                        "application_id": KNOWN_APPLICATION,
                        "title": "Prepare for Roblox",
                        "due_in_days": 2,
                    },
                    {"application_id": "made-up", "title": "Not real", "due_in_days": 1},
                ],
                "confidence": 0.9,
            }
        message = AIMessage(
            content="",
            tool_calls=[
                {"name": self.tool_name, "args": args, "id": "call-1", "type": "tool_call"}
            ],
        )
        return ChatResult(generations=[ChatGeneration(message=message)])


@pytest.mark.asyncio
async def test_model_backed_suite_runs_through_the_graph_and_the_gate_judges_its_output() -> None:
    suite = LangChainAgentSuite(ScriptedChatModel(), "scripted/test-model")
    request = build_request("review:model-suite-001")
    graph = build_graph(suite, InMemorySaver())

    state = await graph.ainvoke(
        {"request": request.model_dump(mode="json", by_alias=True), "analyses": [], "trace": []},
        config={"configurable": {"thread_id": request.thread_id}},
    )

    review = state["review"]
    assert review["modelPath"] == "scripted/test-model"
    assert review["summary"] == "The model merged the specialists."
    assert {step["agentName"] for step in review["trace"][:3]} == {
        "mailbox_analyst",
        "resume_analyst",
        "strategy_analyst",
    }
    # The invented company is dropped and halves the model's confidence, and the invented
    # application loses its reminder, whatever the model claimed.
    assert review["confidence"] == 0.45
    assert [proposal["applicationId"] for proposal in review["proposedReminders"]] == [
        KNOWN_APPLICATION
    ]
    assert review["trace"][-1]["facts"] == [
        {"label": "Reminder", "value": "Prepare for Roblox"},
        {"label": "Dropped citation", "value": "Stripe · offer"},
    ]
    assert review["status"] == "review_required"


@pytest.mark.asyncio
async def test_a_model_that_stops_answering_is_cut_off_instead_of_looping() -> None:
    model = ScriptedChatModel(silent_turns=1000)
    suite = LangChainAgentSuite(model, "scripted/silent")

    with pytest.raises(GraphRecursionError):
        await suite.analyze("resume_analyst", build_request("review:model-suite-002"))

    assert len(model.turns) <= 4


@pytest.mark.asyncio
async def test_a_specialist_cut_off_once_gets_a_fresh_attempt() -> None:
    # Three specialists run at once, so this is every turn of each one's first attempt.
    model = ScriptedChatModel(silent_turns=12)
    request = build_request("review:model-suite-003")
    graph = build_graph(LangChainAgentSuite(model, "scripted/flaky"), InMemorySaver())

    state = await graph.ainvoke(
        {"request": request.model_dump(mode="json", by_alias=True), "analyses": [], "trace": []},
        config={"configurable": {"thread_id": request.thread_id}},
    )

    assert state["review"]["summary"] == "The model merged the specialists."


def test_lists_over_the_limit_are_cut_instead_of_rejected() -> None:
    analysis = SpecialistAnalysis(
        agent_name="model", summary="s", confidence=1, evidence_references=["x"] * 30
    )

    assert len(analysis.evidence_references) == 12
