"""Evidence-only specialists for demos and CI.

They run the same graph, checkpoints, and evidence gate as the model-backed suite,
but every claim is computed from the request, so a run is free, repeatable, and
never invents facts. The model path says "deterministic" so traces stay honest.
"""

from __future__ import annotations

from collections import Counter
from datetime import timedelta

from careeroc_agent_runtime.contracts import (
    ReminderProposal,
    ResumeMailboxReviewRequest,
    SpecialistAnalysis,
    SynthesizedReview,
)

POSITIVE_STATUSES = {"interviewing", "offer"}
STALE_AFTER = timedelta(days=14)


def _dedupe(values: list[str], limit: int) -> list[str]:
    result: list[str] = []
    for value in values:
        if value and value not in result:
            result.append(value)
        if len(result) == limit:
            break
    return result


def _count(value: int, noun: str) -> str:
    return f"{value} {noun}" if value == 1 else f"{value} {noun}s"


class DeterministicAgentSuite:
    model_path = "deterministic/evidence-v1"

    async def analyze(
        self,
        specialist: str,
        request: ResumeMailboxReviewRequest,
    ) -> SpecialistAnalysis:
        analyses = {
            "resume_analyst": self._resume,
            "mailbox_analyst": self._mailbox,
            "strategy_analyst": self._strategy,
        }
        return analyses[specialist](request).model_copy(update={"agent_name": specialist})

    async def synthesize(
        self,
        request: ResumeMailboxReviewRequest,
        analyses: list[SpecialistAnalysis],
    ) -> SynthesizedReview:
        findings = _dedupe([finding for analysis in analyses for finding in analysis.findings], 8)
        actions = _dedupe(
            [action for analysis in analyses for action in analysis.recommended_actions]
            + request.baseline_next_actions,
            4,
        )
        references = _dedupe(
            [reference for analysis in analyses for reference in analysis.evidence_references], 20
        )
        confidence = round(min(analysis.confidence for analysis in analyses), 2) if analyses else 0
        evidence = request.evidence
        return SynthesizedReview(
            summary=(
                f"{_count(len(analyses), 'specialist')} reviewed "
                f"{_count(evidence.total_applications, 'application')} and "
                f"{_count(evidence.resume_versions, 'resume version')}; "
                f"{_count(len(actions), 'next action')} grounded in that evidence."
            ),
            findings=findings or request.baseline_findings,
            next_actions=actions,
            evidence_references=references,
            proposed_reminders=self._reminders(request),
            confidence=confidence,
        )

    @staticmethod
    def _reminders(request: ResumeMailboxReviewRequest) -> list[ReminderProposal]:
        proposals: list[ReminderProposal] = []
        for application in request.applications:
            if application.has_open_reminder:
                continue
            status = application.status.casefold()
            if status in POSITIVE_STATUSES:
                title, days = f"Prepare for the next {application.company} step", 2
            elif (
                status == "applied"
                and request.requested_at_utc - application.last_activity_at_utc >= STALE_AFTER
            ):
                title, days = f"Follow up with {application.company} on {application.role}", 1
            else:
                continue
            proposals.append(
                ReminderProposal(application_id=application.id, title=title[:160], due_in_days=days)
            )
        return proposals[:3]

    @staticmethod
    def _resume(request: ResumeMailboxReviewRequest) -> SpecialistAnalysis:
        resume = request.latest_resume
        if resume is None:
            return SpecialistAnalysis(
                agent_name="resume_analyst",
                summary="No resume is available, so resume evidence cannot support a review.",
                recommended_actions=["Upload a resume so outcomes can be compared with it."],
                confidence=0.2,
            )

        findings = [
            f"The latest resume scores {resume.score}/100 and lists {len(resume.skills)} skills.",
        ]
        if resume.project_highlights:
            # Highlights keep the resume's own list markers; the finding is already a list item.
            highlight = resume.project_highlights[0].lstrip("•*-– ").strip()
            findings.append(f"Strongest project evidence: {highlight}")
        actions = (
            ["Add a measurable result to each project bullet before the next applications."]
            if resume.score < 85
            else ["Keep this resume version as the baseline and tailor only the summary."]
        )
        return SpecialistAnalysis(
            agent_name="resume_analyst",
            summary=f"Reviewed resume '{resume.title}' against {len(resume.skills)} listed skills.",
            findings=findings,
            recommended_actions=actions,
            evidence_references=[f"resume:{resume.title}", f"resume_score:{resume.score}"],
            confidence=0.8,
        )

    @staticmethod
    def _mailbox(request: ResumeMailboxReviewRequest) -> SpecialistAnalysis:
        evidence = request.evidence
        statuses = Counter(application.status.casefold() for application in request.applications)
        findings = [
            f"{evidence.interview_or_offer_applications} of {evidence.total_applications} "
            "applications reached an interview or offer.",
        ]
        if evidence.rejected_applications:
            findings.append(f"{evidence.rejected_applications} applications ended in rejection.")
        findings.extend(
            f"Repeated rejection pattern: {item}" for item in request.repeated_rejections[:2]
        )
        references = [
            f"{application.company} · {application.status}"
            for application in request.applications
            if application.status.casefold() in POSITIVE_STATUSES
        ]
        return SpecialistAnalysis(
            agent_name="mailbox_analyst",
            summary=(
                f"Read {len(request.applications)} applications across {len(statuses)} statuses."
            ),
            findings=findings,
            recommended_actions=[
                f"Follow up on: {signal}" for signal in request.latest_action_signals[:2]
            ],
            evidence_references=references[:12],
            confidence=0.85 if request.applications else 0.3,
        )

    @staticmethod
    def _strategy(request: ResumeMailboxReviewRequest) -> SpecialistAnalysis:
        roles = request.top_target_roles[:3]
        findings = [f"Most applications target {', '.join(roles)}."] if roles else []
        findings.extend(
            f"Confirmed in memory: {fact}" for fact in request.confirmed_memory_facts[:2]
        )
        actions = [
            f"Prepare for the next step with {outcome}."
            for outcome in request.positive_outcomes[:2]
        ]
        return SpecialistAnalysis(
            agent_name="strategy_analyst",
            summary="Prioritized reversible actions from positive outcomes and pending signals.",
            findings=findings,
            recommended_actions=actions,
            evidence_references=[f"role:{role}" for role in roles],
            confidence=0.75 if roles else 0.4,
        )
