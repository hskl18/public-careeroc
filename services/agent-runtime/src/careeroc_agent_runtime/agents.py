from __future__ import annotations

import json
from collections.abc import Mapping
from typing import Protocol

from langchain.agents import create_agent
from langchain.agents.structured_output import ToolStrategy
from langchain_core.language_models.chat_models import BaseChatModel

from careeroc_agent_runtime.contracts import (
    ResumeMailboxReviewRequest,
    SpecialistAnalysis,
    SynthesizedReview,
)

SPECIALIST_PROMPTS: Mapping[str, str] = {
    "resume_analyst": (
        "You are the Resume Evidence Agent in a career multi-agent system. "
        "Assess only resume quality, evidence density, skills, projects, and the "
        "relationship between resume versions and observed outcomes. Never invent "
        "experience or skills. Cite compact labels "
        "from the provided evidence. Return concrete findings and safe next actions."
    ),
    "mailbox_analyst": (
        "You are the Mailbox Outcome Agent in a career multi-agent system. "
        "Analyze application statuses, role patterns, positive outcomes, repeated "
        "rejections, and recent action signals. Treat correlation as correlation, not "
        "causation. Cite compact labels from the "
        "provided evidence and do not infer facts that are absent."
    ),
    "strategy_analyst": (
        "You are the Career Strategy Agent in a career multi-agent system. "
        "Combine confirmed user memory with role targeting and outcome evidence to propose "
        "a small number of reversible, prioritized actions. Never recommend automated "
        "outreach or application mutation. "
        "Call out weak evidence explicitly."
    ),
}


class AgentSuite(Protocol):
    model_path: str

    async def analyze(
        self,
        specialist: str,
        request: ResumeMailboxReviewRequest,
    ) -> SpecialistAnalysis: ...

    async def synthesize(
        self,
        request: ResumeMailboxReviewRequest,
        analyses: list[SpecialistAnalysis],
    ) -> SynthesizedReview: ...


class LangChainAgentSuite:
    def __init__(self, model: BaseChatModel, model_path: str) -> None:
        self.model_path = model_path
        self._specialists = {
            name: create_agent(
                model=model,
                tools=[],
                system_prompt=prompt,
                response_format=ToolStrategy(SpecialistAnalysis),
                name=name,
            )
            for name, prompt in SPECIALIST_PROMPTS.items()
        }
        self._synthesizer = create_agent(
            model=model,
            tools=[],
            system_prompt=(
                "You are the Supervisor Agent for a bounded career decision workflow. "
                "Reconcile specialist results with the deterministic baseline. Keep only "
                "claims grounded in supplied evidence, surface disagreement, and return no "
                "more than eight findings and four prioritized actions. You may propose up "
                "to three follow-up reminders, each for an application listed in the input, "
                "by its exact id, and only where hasOpenReminder is false. Nothing is written "
                "until the user approves; never claim that a reminder already exists."
            ),
            response_format=ToolStrategy(SynthesizedReview),
            name="career_supervisor",
        )

    async def analyze(
        self,
        specialist: str,
        request: ResumeMailboxReviewRequest,
    ) -> SpecialistAnalysis:
        agent = self._specialists[specialist]
        prompt = (
            "Analyze this bounded Careeroc snapshot. Return evidence references using exact "
            "short labels "
            "from the input when possible.\n\n"
            + json.dumps(request.model_dump(mode="json", by_alias=True), separators=(",", ":"))
        )
        result = await agent.ainvoke({"messages": [{"role": "user", "content": prompt}]})
        structured = result.get("structured_response")
        if not isinstance(structured, SpecialistAnalysis):
            raise TypeError(f"{specialist} did not return SpecialistAnalysis")
        return structured.model_copy(update={"agent_name": specialist})

    async def synthesize(
        self,
        request: ResumeMailboxReviewRequest,
        analyses: list[SpecialistAnalysis],
    ) -> SynthesizedReview:
        payload = {
            "deterministicBaseline": {
                "summary": request.baseline_summary,
                "findings": request.baseline_findings,
                "nextActions": request.baseline_next_actions,
            },
            "specialistAnalyses": [
                analysis.model_dump(mode="json", by_alias=True) for analysis in analyses
            ],
            "applications": [
                application.model_dump(mode="json", by_alias=True)
                for application in request.applications
            ],
        }
        result = await self._synthesizer.ainvoke(
            {
                "messages": [
                    {
                        "role": "user",
                        "content": json.dumps(payload, separators=(",", ":")),
                    }
                ]
            }
        )
        structured = result.get("structured_response")
        if not isinstance(structured, SynthesizedReview):
            raise TypeError("career_supervisor did not return SynthesizedReview")
        return structured
