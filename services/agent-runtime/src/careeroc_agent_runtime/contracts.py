from __future__ import annotations

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field
from pydantic.alias_generators import to_camel


class ContractModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class ApplicationEvidence(ContractModel):
    id: str = Field(min_length=1, max_length=64)
    company: str = Field(min_length=1, max_length=200)
    role: str = Field(min_length=1, max_length=240)
    status: str = Field(min_length=1, max_length=80)
    last_activity_at_utc: datetime
    has_open_reminder: bool = False


class LatestResumeEvidence(ContractModel):
    title: str = Field(min_length=1, max_length=240)
    summary: str = Field(default="", max_length=4000)
    score: int = Field(ge=0, le=100)
    skills: list[str] = Field(default_factory=list, max_length=80)
    project_highlights: list[str] = Field(default_factory=list, max_length=40)


class ReviewEvidence(ContractModel):
    resume_versions: int = Field(ge=0)
    latest_resume_score: int = Field(ge=0, le=100)
    total_applications: int = Field(ge=0)
    interview_or_offer_applications: int = Field(ge=0)
    rejected_applications: int = Field(ge=0)
    resume_version_linked_to_outcome: str | None = None


class ResumeMailboxReviewRequest(ContractModel):
    thread_id: str = Field(pattern=r"^[A-Za-z0-9._:-]{8,160}$")
    requested_at_utc: datetime
    baseline_summary: str = Field(min_length=1, max_length=2000)
    baseline_findings: list[str] = Field(default_factory=list, max_length=12)
    baseline_next_actions: list[str] = Field(default_factory=list, max_length=8)
    evidence: ReviewEvidence
    latest_resume: LatestResumeEvidence | None = None
    applications: list[ApplicationEvidence] = Field(default_factory=list, max_length=30)
    top_target_roles: list[str] = Field(default_factory=list, max_length=8)
    positive_outcomes: list[str] = Field(default_factory=list, max_length=8)
    repeated_rejections: list[str] = Field(default_factory=list, max_length=8)
    latest_action_signals: list[str] = Field(default_factory=list, max_length=8)
    confirmed_memory_facts: list[str] = Field(default_factory=list, max_length=8)
    # False for callers that cannot approve anything (A2A): the graph reports the gate result
    # instead of pausing on a checkpoint nobody will resume.
    human_review_available: bool = True


class TraceFact(ContractModel):
    label: str = Field(min_length=1, max_length=100)
    value: str = Field(min_length=1, max_length=1000)


class TraceStep(ContractModel):
    stage: str = Field(min_length=1, max_length=100)
    agent_name: str = Field(min_length=1, max_length=160)
    summary: str = Field(min_length=1, max_length=2000)
    reason: str | None = Field(default=None, max_length=2000)
    facts: list[TraceFact] = Field(default_factory=list, max_length=12)


class SpecialistAnalysis(ContractModel):
    agent_name: str = Field(min_length=1, max_length=160)
    summary: str = Field(min_length=1, max_length=2000)
    findings: list[str] = Field(default_factory=list, max_length=8)
    recommended_actions: list[str] = Field(default_factory=list, max_length=6)
    evidence_references: list[str] = Field(default_factory=list, max_length=12)
    confidence: float = Field(ge=0, le=1)


class ReminderProposal(ContractModel):
    application_id: str = Field(min_length=1, max_length=64)
    title: str = Field(min_length=1, max_length=160)
    due_in_days: int = Field(ge=1, le=30)


class SynthesizedReview(ContractModel):
    summary: str = Field(min_length=1, max_length=2000)
    findings: list[str] = Field(default_factory=list, max_length=12)
    next_actions: list[str] = Field(default_factory=list, max_length=8)
    evidence_references: list[str] = Field(default_factory=list, max_length=20)
    proposed_reminders: list[ReminderProposal] = Field(default_factory=list, max_length=8)
    confidence: float = Field(ge=0, le=1)


class ReviewDecision(ContractModel):
    approved: bool
    note: str | None = Field(default=None, max_length=500)


class ResumeMailboxReviewResponse(ContractModel):
    thread_id: str
    status: Literal["completed", "review_required", "rejected"]
    runtime_path: str
    model_path: str
    summary: str
    findings: list[str]
    next_actions: list[str]
    requires_human_review: bool
    confidence: float = Field(ge=0, le=1)
    trace: list[TraceStep]
    proposed_reminders: list[ReminderProposal] = Field(default_factory=list)
