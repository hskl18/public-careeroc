from __future__ import annotations

import json
import operator
import re
from typing import Annotated, Any, TypedDict

from langgraph.graph import END, START, StateGraph
from langgraph.types import RetryPolicy, TimeoutPolicy, interrupt

from careeroc_agent_runtime.agents import AgentSuite
from careeroc_agent_runtime.contracts import (
    ReminderProposal,
    ResumeMailboxReviewRequest,
    ResumeMailboxReviewResponse,
    ReviewDecision,
    SpecialistAnalysis,
    SynthesizedReview,
    TraceFact,
    TraceStep,
)
from careeroc_agent_runtime.telemetry import agent_span


class CareerGraphState(TypedDict, total=False):
    request: dict[str, Any]
    analyses: Annotated[list[dict[str, Any]], operator.add]
    trace: Annotated[list[dict[str, Any]], operator.add]
    review: dict[str, Any]


def _deduplicate(values: list[str], limit: int) -> list[str]:
    result: list[str] = []
    seen: set[str] = set()
    for value in values:
        normalized = " ".join(value.split()).strip()
        key = normalized.casefold()
        if not normalized or key in seen:
            continue
        seen.add(key)
        result.append(normalized)
        if len(result) == limit:
            break
    return result


def _grounded_references(references: list[str], request: ResumeMailboxReviewRequest) -> list[str]:
    # shortcut: a reference counts when every word in it appears somewhere in the snapshot, which
    # catches invented companies and numbers but not a wrong pairing of real ones. Upgrade to
    # structured references (application id plus field) if pairings start to matter.
    snapshot = json.dumps(
        request.model_dump(mode="json", by_alias=True), ensure_ascii=False
    ).casefold()
    return [
        reference
        for reference in references
        if (words := re.findall(r"[^\W_]+", reference.casefold()))
        and all(word in snapshot for word in words)
    ]


def _grounded_reminders(
    proposals: list[ReminderProposal], request: ResumeMailboxReviewRequest
) -> list[ReminderProposal]:
    # A model can name any id; only applications in this snapshot without an open reminder count.
    eligible = {
        application.id for application in request.applications if not application.has_open_reminder
    }
    kept: list[ReminderProposal] = []
    for proposal in proposals:
        if proposal.application_id in eligible:
            eligible.discard(proposal.application_id)
            kept.append(proposal)
    return kept[:3]


def build_graph(
    suite: AgentSuite,
    checkpointer: Any = None,
    minimum_confidence: float = 0.65,
    agent_timeout_seconds: float = 90,
):
    async def run_specialist(specialist: str, state: CareerGraphState) -> CareerGraphState:
        request = ResumeMailboxReviewRequest.model_validate(state["request"])
        with agent_span(specialist, suite.model_path, request.thread_id) as span:
            analysis = await suite.analyze(specialist, request)
            span.set_attribute("careeroc.agent.confidence", analysis.confidence)
        trace = TraceStep(
            stage="parallel_analysis",
            agent_name=analysis.agent_name,
            summary=analysis.summary,
            reason=f"Specialist confidence {analysis.confidence:.2f}",
            facts=[
                TraceFact(label="Evidence", value=value)
                for value in analysis.evidence_references[:6]
            ],
        )
        return {
            "analyses": [analysis.model_dump(mode="json", by_alias=True)],
            "trace": [trace.model_dump(mode="json", by_alias=True)],
        }

    async def resume_analyst(state: CareerGraphState) -> CareerGraphState:
        return await run_specialist("resume_analyst", state)

    async def mailbox_analyst(state: CareerGraphState) -> CareerGraphState:
        return await run_specialist("mailbox_analyst", state)

    async def strategy_analyst(state: CareerGraphState) -> CareerGraphState:
        return await run_specialist("strategy_analyst", state)

    async def synthesize(state: CareerGraphState) -> CareerGraphState:
        request = ResumeMailboxReviewRequest.model_validate(state["request"])
        analyses = [SpecialistAnalysis.model_validate(value) for value in state["analyses"]]
        with agent_span("career_supervisor", suite.model_path, request.thread_id) as span:
            review = await suite.synthesize(request, analyses)
            span.set_attribute("careeroc.agent.confidence", review.confidence)
        trace = TraceStep(
            stage="synthesis",
            agent_name="career_supervisor",
            summary=review.summary,
            reason=f"Synthesized {len(analyses)} independent specialist outputs.",
            facts=[
                TraceFact(label="Evidence", value=value) for value in review.evidence_references[:8]
            ],
        )
        return {
            "review": review.model_dump(mode="json", by_alias=True),
            "trace": [trace.model_dump(mode="json", by_alias=True)],
        }

    async def evidence_gate(state: CareerGraphState) -> CareerGraphState:
        request = ResumeMailboxReviewRequest.model_validate(state["request"])
        review = SynthesizedReview.model_validate(state["review"])
        findings = _deduplicate(review.findings or request.baseline_findings, 8)
        next_actions = _deduplicate(review.next_actions or request.baseline_next_actions, 4)
        claimed_references = _deduplicate(review.evidence_references, 20)
        evidence_references = _grounded_references(claimed_references, request)
        grounded_ratio = (
            len(evidence_references) / len(claimed_references) if claimed_references else 0
        )
        # A model's own confidence cannot outrun the share of its citations found in the snapshot.
        confidence = round(review.confidence * grounded_ratio, 2)
        reminders = _grounded_reminders(review.proposed_reminders, request)
        requires_human_review = (
            confidence < minimum_confidence
            or not evidence_references
            or request.evidence.total_applications == 0
            or request.latest_resume is None
            # Agents only propose writes; a person approves each batch before careeroc applies it.
            or bool(reminders)
        )
        status = "review_required" if requires_human_review else "completed"
        gate_trace = TraceStep(
            stage="evidence_gate",
            agent_name="deterministic_evidence_gate",
            summary=(
                f"{len(reminders)} proposed reminders wait for the user's approval."
                if reminders
                else "Review requires human confirmation before it becomes durable strategy memory."
                if requires_human_review
                else "Review passed the evidence and confidence gate."
            ),
            reason=(
                f"confidence={confidence:.2f}; "
                f"evidenceReferences={len(evidence_references)}/{len(claimed_references)}; "
                f"applications={request.evidence.total_applications}; "
                f"resumePresent={request.latest_resume is not None}; "
                f"remindersProposed={len(review.proposed_reminders)}; "
                f"remindersKept={len(reminders)}"
            ),
            facts=[TraceFact(label="Reminder", value=reminder.title) for reminder in reminders],
        )
        response = ResumeMailboxReviewResponse(
            thread_id=request.thread_id,
            status=status,
            runtime_path="langgraph.resume_mailbox_review.v1",
            model_path=suite.model_path,
            summary=review.summary,
            findings=findings,
            next_actions=next_actions,
            requires_human_review=requires_human_review,
            confidence=confidence,
            trace=[TraceStep.model_validate(value) for value in state["trace"]] + [gate_trace],
            proposed_reminders=reminders,
        )
        return {"review": response.model_dump(mode="json", by_alias=True)}

    async def human_review(state: CareerGraphState) -> CareerGraphState:
        # Pauses on a durable checkpoint until the user approves or rejects the review in careeroc.
        review = ResumeMailboxReviewResponse.model_validate(state["review"])
        decision = ReviewDecision.model_validate(
            interrupt(
                {
                    "threadId": review.thread_id,
                    "summary": review.summary,
                    "confidence": review.confidence,
                    "findings": review.findings,
                    "proposedReminders": [
                        reminder.model_dump(mode="json", by_alias=True)
                        for reminder in review.proposed_reminders
                    ],
                }
            )
        )
        step = TraceStep(
            stage="human_review",
            agent_name="human_reviewer",
            summary=(
                f"The user approved this review and {len(review.proposed_reminders)} reminders."
                if decision.approved
                else "The user rejected this review; nothing from it is saved."
            ),
            reason=decision.note,
            facts=[],
        )
        resolved = review.model_copy(
            update={
                "status": "completed" if decision.approved else "rejected",
                "requires_human_review": False,
                "trace": [*review.trace, step],
            }
        )
        return {"review": resolved.model_dump(mode="json", by_alias=True)}

    def after_gate(state: CareerGraphState) -> str:
        pause = state["review"]["requiresHumanReview"] and state["request"]["humanReviewAvailable"]
        return "human_review" if pause else END

    builder = StateGraph(CareerGraphState)
    # Model-backed nodes get a hard per-attempt deadline and one retry for transient failures;
    # if they still fail, the API falls back to its deterministic orchestrator.
    agent_policies = {
        "timeout": TimeoutPolicy(run_timeout=agent_timeout_seconds),
        "retry_policy": RetryPolicy(max_attempts=2),
    }
    builder.add_node("resume_analyst", resume_analyst, **agent_policies)
    builder.add_node("mailbox_analyst", mailbox_analyst, **agent_policies)
    builder.add_node("strategy_analyst", strategy_analyst, **agent_policies)
    builder.add_node("career_supervisor", synthesize, **agent_policies)
    builder.add_node("evidence_gate", evidence_gate)
    builder.add_edge(START, "resume_analyst")
    builder.add_edge(START, "mailbox_analyst")
    builder.add_edge(START, "strategy_analyst")
    builder.add_edge("resume_analyst", "career_supervisor")
    builder.add_edge("mailbox_analyst", "career_supervisor")
    builder.add_edge("strategy_analyst", "career_supervisor")
    builder.add_edge("career_supervisor", "evidence_gate")
    builder.add_node("human_review", human_review)
    builder.add_conditional_edges("evidence_gate", after_gate, ["human_review", END])
    builder.add_edge("human_review", END)
    return builder.compile(checkpointer=checkpointer)
