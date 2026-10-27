from __future__ import annotations

import asyncio
from datetime import UTC, datetime

import pytest
from langgraph.checkpoint.memory import InMemorySaver

from careeroc_agent_runtime.contracts import (
    ApplicationEvidence,
    LatestResumeEvidence,
    ResumeMailboxReviewRequest,
    ReviewEvidence,
    SpecialistAnalysis,
    SynthesizedReview,
)
from careeroc_agent_runtime.graph import build_graph


class FakeAgentSuite:
    model_path = "fake/test-model"

    def __init__(self) -> None:
        self.active = 0
        self.max_active = 0

    async def analyze(
        self,
        specialist: str,
        request: ResumeMailboxReviewRequest,
    ) -> SpecialistAnalysis:
        self.active += 1
        self.max_active = max(self.max_active, self.active)
        await asyncio.sleep(0.02)
        self.active -= 1
        return SpecialistAnalysis(
            agent_name=specialist,
            summary=f"{specialist} completed",
            findings=[f"{specialist} finding"],
            recommended_actions=[f"{specialist} action"],
            evidence_references=[
                request.applications[0].company if request.applications else "baseline"
            ],
            confidence=0.9,
        )

    async def synthesize(
        self,
        request: ResumeMailboxReviewRequest,
        analyses: list[SpecialistAnalysis],
    ) -> SynthesizedReview:
        return SynthesizedReview(
            summary="The resume and mailbox evidence support focused targeting.",
            findings=[analysis.findings[0] for analysis in analyses],
            next_actions=["Create one role-specific resume version."],
            evidence_references=[analysis.evidence_references[0] for analysis in analyses],
            confidence=0.88,
        )


def build_request(thread_id: str = "review:test-run-001") -> ResumeMailboxReviewRequest:
    return ResumeMailboxReviewRequest(
        thread_id=thread_id,
        requested_at_utc=datetime(2026, 7, 13, tzinfo=UTC),
        baseline_summary="Baseline summary",
        baseline_findings=["Baseline finding"],
        baseline_next_actions=["Baseline action"],
        evidence=ReviewEvidence(
            resume_versions=2,
            latest_resume_score=84,
            total_applications=3,
            interview_or_offer_applications=1,
            rejected_applications=1,
            resume_version_linked_to_outcome="54f38f36-09ae-47bf-b37a-c1f988efeb45",
        ),
        latest_resume=LatestResumeEvidence(
            title="Software engineer resume",
            summary="Full-stack engineer",
            score=84,
            skills=["TypeScript", "PostgreSQL"],
            project_highlights=["Careeroc"],
        ),
        applications=[
            ApplicationEvidence(
                id="7b0c6c1e-roblox",
                company="Roblox",
                role="Software Engineer",
                status="interviewing",
                last_activity_at_utc=datetime(2026, 7, 10, tzinfo=UTC),
            )
        ],
        top_target_roles=["Software Engineer (3 applications)"],
        positive_outcomes=["Roblox - Software Engineer (interview)"],
        repeated_rejections=[],
        latest_action_signals=["Roblox - Software Engineer: interview"],
        confirmed_memory_facts=["Prefers full-stack roles"],
    )


@pytest.mark.asyncio
async def test_graph_runs_specialists_in_parallel_and_gates_evidence() -> None:
    suite = FakeAgentSuite()
    graph = build_graph(suite, checkpointer=InMemorySaver())
    request = build_request()

    state = await graph.ainvoke(
        {
            "request": request.model_dump(mode="json", by_alias=True),
            "analyses": [],
            "trace": [],
        },
        config={"configurable": {"thread_id": request.thread_id}},
    )

    review = state["review"]
    assert suite.max_active == 3
    assert review["status"] == "completed"
    assert review["requiresHumanReview"] is False
    assert review["runtimePath"] == "langgraph.resume_mailbox_review.v1"
    assert len(review["trace"]) == 5


@pytest.mark.asyncio
async def test_graph_requires_review_when_resume_or_application_evidence_is_missing() -> None:
    suite = FakeAgentSuite()
    graph = build_graph(suite, checkpointer=InMemorySaver())
    request = build_request("review:test-run-002").model_copy(
        update={
            "latest_resume": None,
            "applications": [],
            "evidence": ReviewEvidence(
                resume_versions=0,
                latest_resume_score=0,
                total_applications=0,
                interview_or_offer_applications=0,
                rejected_applications=0,
                resume_version_linked_to_outcome=None,
            ),
        }
    )

    state = await graph.ainvoke(
        {
            "request": request.model_dump(mode="json", by_alias=True),
            "analyses": [],
            "trace": [],
        },
        config={"configurable": {"thread_id": request.thread_id}},
    )

    assert state["review"]["status"] == "review_required"
    assert state["review"]["requiresHumanReview"] is True


@pytest.mark.asyncio
async def test_a_hung_specialist_times_out_instead_of_blocking_the_run() -> None:
    class HangingSuite(FakeAgentSuite):
        async def analyze(self, specialist, request):  # type: ignore[override]
            if specialist == "resume_analyst":
                await asyncio.sleep(5)
            return await super().analyze(specialist, request)

    graph = build_graph(HangingSuite(), checkpointer=InMemorySaver(), agent_timeout_seconds=0.05)
    request = build_request("review:timeout-001")

    with pytest.raises(Exception) as raised:
        await asyncio.wait_for(
            graph.ainvoke(
                {
                    "request": request.model_dump(mode="json", by_alias=True),
                    "analyses": [],
                    "trace": [],
                },
                config={"configurable": {"thread_id": request.thread_id}},
            ),
            timeout=2,
        )

    assert not isinstance(raised.value, asyncio.TimeoutError)
