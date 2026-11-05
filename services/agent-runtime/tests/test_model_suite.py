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

from careeroc_agent_runtime.agents import LangChainAgentSuite
from careeroc_agent_runtime.graph import build_graph
from tests.test_graph import build_request

KNOWN_APPLICATION = "7b0c6c1e-roblox"


class ScriptedChatModel(BaseChatModel):
    """Answers with a structured-output tool call for whichever schema the agent bound."""

    tool_name: str = ""

    @property
    def _llm_type(self) -> str:
        return "scripted"

    def bind_tools(self, tools: Any, **kwargs: Any) -> ScriptedChatModel:
        name = getattr(tools[0], "name", None) or tools[0]["function"]["name"]
        return self.model_copy(update={"tool_name": name})

    def _generate(self, messages: Any, stop: Any = None, run_manager: Any = None, **kwargs: Any):
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
