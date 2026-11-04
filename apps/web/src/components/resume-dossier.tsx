"use client";

import { useState, type ReactNode } from "react";
import { LocalTime } from "@/components/local-time";
import type {
  ResumeDocument,
  ResumeEvaluation,
  ResumeModelRun,
  ResumeSectionReview,
  ResumeStructuredData,
  ResumeStructuredDataPatchOperation,
} from "@/lib/resumes";
import { InlineMemoryProposals } from "@/components/inline-memory-proposals";
import { ResumeSuggestionsPanel } from "@/components/resume-suggestions-panel";
import { ResumeImportPanel } from "./resume-import-panel";
import {
  ResumeEducationEditor,
  ResumeExperienceEditor,
  ResumeProjectEditor,
  ResumeSkillsEditor,
} from "./resume-section-editors";

function clampScore(value: number) {
  return Math.min(100, Math.max(0, Math.round(value)));
}

function formatLatency(value: number | null) {
  if (value === null) {
    return "Not recorded";
  }

  if (value >= 1000) {
    return `${(value / 1000).toFixed(value >= 10_000 ? 0 : 1)}s`;
  }

  return `${value}ms`;
}

function formatTokenCount(value: number | null) {
  return value === null ? "Not recorded" : new Intl.NumberFormat("en-US").format(value);
}

function formatEstimatedCost(value: number | null) {
  if (value === null) {
    return "Not recorded";
  }

  if (value === 0) {
    return "$0.00";
  }

  return value < 0.01 ? `$${value.toFixed(4)}` : `$${value.toFixed(2)}`;
}

function scoreTier(score: number) {
  const clamped = clampScore(score);
  if (clamped >= 85) {
    return { label: "strong", color: "var(--green)" };
  }

  if (clamped >= 70) {
    return { label: "solid", color: "var(--yellow)" };
  }

  if (clamped >= 55) {
    return { label: "mixed", color: "var(--blue)" };
  }

  return { label: "weak", color: "var(--red)" };
}

function formatSectionName(value: string) {
  return value
    .replace(/[_-]+/g, " ")
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function getVersionStructuredData(
  version: NonNullable<ResumeDocument["latestVersion"]>,
): ResumeStructuredData {
  const structuredData = version.structuredData;

  return {
    contact: structuredData?.contact ?? {
      name: "",
      email: "",
      phone: "",
      location: "",
      links: [],
    },
    experience: structuredData?.experience ?? [],
    education: structuredData?.education ?? [],
    skills: {
      technical: structuredData?.skills?.technical ?? version.skills ?? [],
      languages: structuredData?.skills?.languages ?? [],
      other: structuredData?.skills?.other ?? [],
    },
    projects:
      structuredData?.projects ??
      (version.projectHighlights ?? []).map((description, index) => ({
        name: `Project ${index + 1}`,
        description,
        tech: [],
        links: [],
        bullets: [],
      })),
  };
}

export function ResumeDossier({
  document,
  documents,
  canPersist,
  showNewResumeForm,
  title,
  sourceText,
  importFile,
  fileInputKey,
  canImport,
  canCreateFromText,
  isImporting,
  pendingOperation,
  importPhase,
  fieldPath,
  correctedValue,
  note,
  isCorrecting,
  notice,
  error,
  onToggleNewResumeForm,
  onFileChange,
  onTitleChange,
  onSourceTextChange,
  onImport,
  onCreateFromText,
  onFieldPathChange,
  onCorrectedValueChange,
  onNoteChange,
  onSubmit,
  onPatch,
  onAnalyze,
  onReextract,
  proxyPrefix,
  applicationId,
}: {
  document: ResumeDocument | null;
  documents: ResumeDocument[];
  canPersist: boolean;
  showNewResumeForm: boolean;
  title: string;
  sourceText: string;
  importFile: File | null;
  fileInputKey: number;
  canImport: boolean;
  canCreateFromText: boolean;
  isImporting: boolean;
  pendingOperation: "file-import" | "text-create" | "reanalyze" | "reextract" | null;
  importPhase: "reading" | "saving" | "extracting" | "analyzing" | null;
  fieldPath: string;
  correctedValue: string;
  note: string;
  isCorrecting: boolean;
  notice: string | null;
  error: string | null;
  onToggleNewResumeForm: () => void;
  onFileChange: (file: File | null) => void;
  onTitleChange: (value: string) => void;
  onSourceTextChange: (value: string) => void;
  onImport: () => Promise<void>;
  onCreateFromText: () => Promise<void>;
  onFieldPathChange: (value: string) => void;
  onCorrectedValueChange: (value: string) => void;
  onNoteChange: (value: string) => void;
  onSubmit: (versionId: string) => void;
  onPatch: (
    versionId: string,
    operations: ResumeStructuredDataPatchOperation[],
    note?: string | null,
  ) => Promise<boolean>;
  onAnalyze: (versionId: string) => Promise<void>;
  onReextract: (documentId: string) => Promise<void>;
  proxyPrefix: string | null;
  applicationId?: string | null;
}) {
  const version = document?.latestVersion ?? null;
  const evaluation: ResumeEvaluation | null = document?.latestEvaluation ?? null;

  if (!document || !version || !evaluation) {
    return (
      <section className="card app-workspace-panel resume-dossier-panel flex min-h-0 flex-1 items-center justify-center p-6">
        <div className="max-w-md text-center">
          <p className="eyebrow">Resume dossier</p>
          <h2 className="mt-2 text-xl font-semibold text-[var(--text-primary)]">
            Evaluation is still being prepared
          </h2>
          <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
            The saved resume exists, but the structured extraction has not
            landed yet.
          </p>
        </div>
      </section>
    );
  }

  const activeVersion = version;
  const structuredData = getVersionStructuredData(activeVersion);
  const patchStructuredData = async (
    operations: ResumeStructuredDataPatchOperation[],
    patchNote?: string | null,
  ) => onPatch(activeVersion.id, operations, patchNote ?? null);

  function renderParsedSections(compact = false) {
    return (
      <>
        <ResumeDossierSection
          number="01"
          eyebrow="Parsed"
          title="Contact"
          compact={compact}
        >
          <ResumeContactCard
            structuredData={structuredData}
            canEdit={canPersist}
            onPatch={patchStructuredData}
          />
        </ResumeDossierSection>

        <ResumeDossierSection
          number="02"
          eyebrow="Parsed"
          title="Summary"
          compact={compact}
        >
          <p className="rounded-md border border-[var(--border)] bg-white/60 px-4 py-3 text-sm leading-7 text-[var(--text-secondary)]">
            {activeVersion.summary}
          </p>
        </ResumeDossierSection>

        <ResumeDossierSection
          number="03"
          eyebrow="Parsed"
          title="Experience"
          compact={compact}
        >
          <ResumeExperienceEditor
            items={structuredData.experience}
            canEdit={canPersist}
            onPatch={patchStructuredData}
          />
        </ResumeDossierSection>

        <ResumeDossierSection
          number="04"
          eyebrow="Parsed"
          title="Education"
          compact={compact}
        >
          <ResumeEducationEditor
            items={structuredData.education}
            canEdit={canPersist}
            onPatch={patchStructuredData}
          />
        </ResumeDossierSection>

        <ResumeDossierSection
          number="05"
          eyebrow="Parsed"
          title="Skills"
          compact={compact}
        >
          <ResumeSkillsEditor
            skills={structuredData.skills}
            canEdit={canPersist}
            onPatch={patchStructuredData}
          />
        </ResumeDossierSection>

        <ResumeDossierSection
          number="06"
          eyebrow="Parsed"
          title="Projects"
          compact={compact}
        >
          <ResumeProjectEditor
            projects={structuredData.projects}
            canEdit={canPersist}
            onPatch={patchStructuredData}
          />
        </ResumeDossierSection>
      </>
    );
  }

  return (
    <section className="card app-workspace-panel resume-dossier-panel flex min-h-0 flex-1 flex-col overflow-hidden p-0">
      <ResumeDossierHeader
        document={document}
        versionId={activeVersion.id}
        evaluation={evaluation}
        showNewResumeForm={showNewResumeForm}
        pendingOperation={pendingOperation}
        onToggleNewResumeForm={onToggleNewResumeForm}
        onAnalyze={() => onAnalyze(activeVersion.id)}
        onReextract={() => onReextract(document.id)}
      />

      <div className="workspace-scroll-region min-h-0 flex-1 px-5 py-4">
        <div className="mx-auto grid max-w-[92rem] gap-4 xl:grid-cols-[11rem_minmax(0,1fr)]">
          <ResumeAgentRail
            evaluation={evaluation}
            version={activeVersion}
            pendingOperation={pendingOperation}
          />

          <div className="grid min-w-0 gap-4">
            {showNewResumeForm ? (
              <ResumeDossierSection number="00" eyebrow="Ingest" title="Submit new resume" accent="var(--accent)">
                <ResumeImportPanel
                  inputId="resume-reupload"
                  fileInputKey={fileInputKey}
                  importFile={importFile}
                  title={title}
                  sourceText={sourceText}
                  canPersist={canPersist}
                  canImport={canImport}
                  canCreateFromText={canCreateFromText}
                  isImporting={isImporting}
                  phase={importPhase}
                  notice={notice}
                  error={error}
                  compact
                  onFileChange={onFileChange}
                  onTitleChange={onTitleChange}
                  onSourceTextChange={onSourceTextChange}
                  onImport={onImport}
                  onCreateFromText={onCreateFromText}
                />
              </ResumeDossierSection>
            ) : null}

            <div className="hidden gap-4 md:grid">
              {renderParsedSections()}
            </div>

            <ResumeDossierSection number="07" eyebrow="Review" title="Recruiter review" accent="var(--violet)">
              <ResumeRecruiterReview evaluation={evaluation} version={activeVersion} />
            </ResumeDossierSection>

            <ResumeSuggestionsPanel
              resumeDocumentId={document.id}
              proxyPrefix={proxyPrefix}
              applicationId={applicationId}
            />

            <InlineMemoryProposals
              proposals={document.memoryProposals}
              proxyPrefix={proxyPrefix}
              title="Resume memory"
              emptyCopy="No resume-scoped memory proposals are waiting here."
            />

            <details className="rounded-md border border-[var(--border)] bg-white/55 md:hidden">
              <summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-3">
                <span className="min-w-0">
                  <span className="eyebrow block">Parsed resume</span>
                  <span className="mt-1 block text-sm font-semibold text-[var(--text-primary)]">
                    Contact, history, skills, and projects
                  </span>
                </span>
                <span className="badge shrink-0 bg-[var(--surface-elevated)] text-[var(--text-secondary)]">
                  6 sections
                </span>
              </summary>
              <div className="grid gap-3 border-t border-[var(--border)] p-3">
                {renderParsedSections(true)}
              </div>
            </details>

            <details className="rounded-md border border-[var(--border)] bg-white/35">
              <summary className="cursor-pointer px-4 py-3 font-mono text-[11px] font-semibold uppercase tracking-[0.16em] text-[var(--text-secondary)]">
                Parser tools
              </summary>
              <div className="grid gap-4 border-t border-[var(--border)] p-4 lg:grid-cols-2">
                <ResumeDossierSection number="08" eyebrow="Correct" title="Teach parser" compact accent="var(--accent)">
                  <ResumeCorrectionPanel
                    document={document}
                    canPersist={canPersist}
                    fieldPath={fieldPath}
                    correctedValue={correctedValue}
                    note={note}
                    isCorrecting={isCorrecting}
                    onFieldPathChange={onFieldPathChange}
                    onCorrectedValueChange={onCorrectedValueChange}
                    onNoteChange={onNoteChange}
                    onSubmit={onSubmit}
                  />
                  {notice ? <p className="mt-3 text-sm font-medium text-[var(--green)]">{notice}</p> : null}
                  {error ? <p className="mt-3 text-sm font-medium text-[var(--red)]">{error}</p> : null}
                </ResumeDossierSection>

                <ResumeDossierSection number="09" eyebrow="History" title="Saved resumes" compact>
                  <ResumeDocumentsPanel documents={documents} />
                </ResumeDossierSection>
              </div>
            </details>
          </div>
        </div>
      </div>
    </section>
  );
}

function ResumeDossierHeader({
  document,
  versionId,
  evaluation,
  showNewResumeForm,
  pendingOperation,
  onToggleNewResumeForm,
  onAnalyze,
  onReextract,
}: {
  document: ResumeDocument;
  versionId: string;
  evaluation: ResumeEvaluation;
  showNewResumeForm: boolean;
  pendingOperation: "file-import" | "text-create" | "reanalyze" | "reextract" | null;
  onToggleNewResumeForm: () => void;
  onAnalyze: () => void;
  onReextract: () => void;
}) {
  const version = document.latestVersion;
  const isBusy = pendingOperation !== null;

  return (
    <div className="shrink-0 border-b border-[var(--border)] px-5 py-3">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0 lg:max-w-[46rem]">
          <div className="flex flex-wrap items-center gap-2">
            <p className="eyebrow">Resume dossier</p>
            <span className="badge bg-[var(--surface-elevated)] text-[var(--text-secondary)]">
              dossier
            </span>
            <span className="badge bg-[var(--surface-elevated)] text-[var(--text-secondary)]">
              {document.sourceKind}
            </span>
          </div>
          <h1 className="mt-2 break-words text-xl font-semibold text-[var(--text-primary)]">
            {document.title}
          </h1>
          <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">
            filed <LocalTime value={document.createdAtUtc} /> · version {versionId.slice(0, 8)}
          </p>
        </div>
        <div className="grid gap-3 lg:w-[42rem] lg:shrink-0">
          <div className="flex flex-wrap items-center justify-start gap-2 lg:justify-end">
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={onAnalyze}
              disabled={isBusy}
            >
              {pendingOperation === "reanalyze" ? "Reviewing..." : "Re-run"}
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={onReextract}
              disabled={isBusy}
            >
              {pendingOperation === "reextract" ? "Extracting..." : "Re-extract"}
            </button>
            <button
              type="button"
              className={`${showNewResumeForm ? "btn btn-secondary" : "btn btn-primary"} btn-sm`}
              onClick={onToggleNewResumeForm}
              disabled={isBusy}
            >
              {showNewResumeForm ? "Close" : "Submit resume"}
            </button>
          </div>
          <div className="grid grid-cols-2 gap-2 text-sm sm:grid-cols-4">
            <Metric label="Overall" value={clampScore(evaluation.score)} />
            <Metric label="Skills" value={version?.skills.length ?? 0} />
            <Metric label="Experience" value={version?.experienceCount ?? 0} />
            <Metric label="Projects" value={version?.projectCount ?? 0} />
          </div>
        </div>
      </div>
    </div>
  );
}

function ResumeAgentRail({
  evaluation,
  version,
  pendingOperation,
}: {
  evaluation: ResumeEvaluation;
  version: NonNullable<ResumeDocument["latestVersion"]>;
  pendingOperation: "file-import" | "text-create" | "reanalyze" | "reextract" | null;
}) {
  const structuredData = getVersionStructuredData(version);
  const activeStep = pendingOperation === "file-import" || pendingOperation === "text-create"
    ? "01"
    : pendingOperation === "reextract"
      ? "02"
      : pendingOperation === "reanalyze"
        ? "04"
        : null;
  const steps = [
    {
      index: "01",
      title: "Ingest",
      body: "resume source saved",
      tone: "var(--accent)",
    },
    {
      index: "02",
      title: "Extract",
      body: `${structuredData.experience.length} roles · ${structuredData.projects.length} projects`,
      tone: "var(--blue)",
    },
    {
      index: "03",
      title: "Correct",
      body: `${version.corrections.length} saved edits`,
      tone: "var(--accent)",
    },
    {
      index: "04",
      title: "Review",
      body: `${clampScore(evaluation.score)}/100 recruiter score`,
      tone: "var(--violet)",
    },
  ];

  return (
    <aside className="hidden xl:block">
      <div className="sticky top-4 grid gap-2">
        <p className="eyebrow px-1">Resume agents</p>
        {steps.map((step) => (
          <div
            key={step.index}
            className={`rounded-md border p-3 transition ${
              activeStep === step.index
                ? "border-[var(--accent)] bg-[var(--accent-soft)]"
                : "border-[var(--border)] bg-white/90"
            }`}
            style={{ boxShadow: `inset 3px 0 0 ${step.tone}` }}
          >
            <p className="font-mono text-[11px] font-semibold text-[var(--text-tertiary)]">
              {step.index}
            </p>
            <p className="mt-1 text-sm font-semibold text-[var(--text-primary)]">
              {step.title}
            </p>
            <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">
              {step.body}
            </p>
          </div>
        ))}
      </div>
    </aside>
  );
}

function ResumeDossierSection({
  number,
  eyebrow,
  title,
  compact = false,
  accent = "var(--accent)",
  children,
}: {
  number: string;
  eyebrow: string;
  title: string;
  compact?: boolean;
  accent?: string;
  children: ReactNode;
}) {
  return (
    <section
      className={`rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.92)] ${
        compact ? "p-3" : "p-4 sm:p-5"
      }`}
      style={{ boxShadow: `inset 3px 0 0 ${accent}` }}
    >
      <div className="mb-4 flex flex-wrap items-baseline gap-x-3 gap-y-1 border-b border-[var(--border)] pb-3">
        <span className="font-mono text-sm font-semibold text-[var(--text-tertiary)]">
          {number}
        </span>
        <p className="eyebrow">{eyebrow}</p>
        <h3 className="text-base font-semibold text-[var(--text-primary)]">
          {title}
        </h3>
      </div>
      {children}
    </section>
  );
}

function ResumeContactCard({
  structuredData,
  canEdit,
  onPatch,
}: {
  structuredData: ResumeStructuredData;
  canEdit: boolean;
  onPatch: (
    operations: ResumeStructuredDataPatchOperation[],
    note?: string | null,
  ) => Promise<boolean>;
}) {
  const contact = structuredData.contact;
  if (!contact) {
    return <p className="text-sm text-[var(--text-secondary)]">No contact section was extracted.</p>;
  }

  const fields = [
    { key: "name", label: "Name", value: contact.name, path: "/contact/name" },
    { key: "email", label: "Email", value: contact.email, path: "/contact/email" },
    { key: "phone", label: "Phone", value: contact.phone, path: "/contact/phone" },
    { key: "location", label: "Location", value: contact.location, path: "/contact/location" },
  ];
  const hasAnyValue = fields.some((field) => field.value);

  return (
    <div className="grid overflow-hidden rounded-md border border-[var(--border)] bg-white/95 md:grid-cols-2">
      {hasAnyValue || canEdit ? (
        fields.map((field) => (
          <ResumeContactFieldRow
            key={field.key}
            label={field.label}
            value={field.value}
            path={field.path}
            canEdit={canEdit}
            onPatch={onPatch}
          />
        ))
      ) : (
        <p className="p-4 text-sm text-[var(--text-secondary)]">No contact fields were extracted.</p>
      )}
      {contact.links.length > 0 ? (
        <div className="px-4 py-3 md:col-span-2">
          <p className="eyebrow">Links</p>
          <div className="mt-2 flex flex-wrap gap-2">
            {contact.links.map((link) => (
              <a
                key={link.url}
                href={link.url}
                target="_blank"
                rel="noreferrer"
                className="badge bg-[var(--surface-elevated)] text-[var(--text-secondary)] hover:underline"
              >
                {link.label}
              </a>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  );
}

function ResumeContactFieldRow({
  label,
  value,
  path,
  canEdit,
  onPatch,
}: {
  label: string;
  value: string;
  path: string;
  canEdit: boolean;
  onPatch: (
    operations: ResumeStructuredDataPatchOperation[],
    note?: string | null,
  ) => Promise<boolean>;
}) {
  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState(value);
  const [isSaving, setIsSaving] = useState(false);

  function startEdit() {
    setDraft(value);
    setIsEditing(true);
  }

  function cancelEdit() {
    setIsEditing(false);
    setDraft(value);
  }

  async function save() {
    const trimmed = draft.trim();
    if (trimmed === value.trim()) {
      setIsEditing(false);
      return;
    }
    setIsSaving(true);
    const operation: ResumeStructuredDataPatchOperation = trimmed.length === 0
      ? { op: "replace", path, value: "" }
      : { op: "replace", path, value: trimmed };
    const ok = await onPatch([operation], `Edited ${label.toLowerCase()}`);
    setIsSaving(false);
    if (ok) {
      setIsEditing(false);
    }
  }

  return (
    <div className="border-b border-[var(--border)] px-4 py-3 md:odd:border-r">
      <div className="flex items-center justify-between gap-2">
        <p className="eyebrow">{label}</p>
        {canEdit && !isEditing ? (
          <button
            type="button"
            onClick={startEdit}
            className="text-xs text-[var(--accent)] hover:underline"
          >
            Edit
          </button>
        ) : null}
      </div>
      {isEditing ? (
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <input
            type="text"
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            disabled={isSaving}
            className="min-w-0 flex-1 rounded-md border border-[var(--border)] bg-white/80 px-2 py-1 text-sm text-[var(--text-primary)]"
            maxLength={300}
            autoFocus
          />
          <button
            type="button"
            className="btn btn-primary btn-sm"
            onClick={() => void save()}
            disabled={isSaving}
          >
            {isSaving ? "Saving..." : "Save"}
          </button>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={cancelEdit}
            disabled={isSaving}
          >
            Cancel
          </button>
        </div>
      ) : (
        <p className="mt-1 break-words text-sm font-medium text-[var(--text-primary)]">
          {value || <span className="text-[var(--text-tertiary)]">Not detected</span>}
        </p>
      )}
    </div>
  );
}

function ResumeRecruiterReview({
  evaluation,
  version,
}: {
  evaluation: ResumeEvaluation;
  version: NonNullable<ResumeDocument["latestVersion"]>;
}) {
  const reviews = evaluation.sectionReviews ?? [];
  const overallReview = reviews.find((review) => review.section.toLowerCase() === "overall") ?? null;
  const sectionReviews = reviews.filter((review) => review.section.toLowerCase() !== "overall");
  const tier = scoreTier(evaluation.score);

  return (
    <div className="grid gap-3">
      <div className="grid gap-3 lg:grid-cols-[11rem_minmax(0,1fr)]">
        <div className="rounded-md border border-[var(--border)] bg-white/90 p-3">
          <div className="flex items-center justify-between gap-2">
            <p className="eyebrow">Overall</p>
            <span
              className="rounded-full px-2 py-1 text-[11px] font-semibold uppercase text-white"
              style={{ backgroundColor: tier.color }}
            >
              {tier.label}
            </span>
          </div>
          <p className="mt-2 text-3xl font-semibold text-[var(--text-primary)]">
            {clampScore(evaluation.score)}
            <span className="text-base text-[var(--text-tertiary)]">/100</span>
          </p>
          <p className="mt-3 text-sm leading-6 text-[var(--text-secondary)]">
            {evaluation.verdict}
          </p>
        </div>
        <div className="grid gap-3 md:grid-cols-3">
          <SignalList title="Strengths" items={evaluation.strengths} fallback="No strengths detected yet." />
          <SignalList title="Gaps" items={evaluation.gaps} fallback="No major gaps detected." />
          <SignalList title="Next actions" items={evaluation.nextActions} fallback="No next action generated." />
        </div>
      </div>
      {overallReview ? <ResumeSectionTriad review={overallReview} /> : null}
      {sectionReviews.length > 0 ? <ResumeSectionReviewGrid reviews={sectionReviews} /> : null}
      <ResumeExtractionContractPanel version={version} />
      <ResumeModelRunPanel modelRun={evaluation.modelRun} />
    </div>
  );
}

function ResumeSectionTriad({ review }: { review: ResumeSectionReview }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.92)] p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <p className="eyebrow">Overall analysis</p>
          <p className="mt-1 text-sm font-semibold text-[var(--text-primary)]">
            Section score {clampScore(review.score)}/100
          </p>
        </div>
        <span className="badge bg-[var(--surface-elevated)] text-[var(--text-secondary)]">
          {scoreTier(review.score).label}
        </span>
      </div>
      <div className="mt-3 grid gap-3 md:grid-cols-3">
        <ReviewSignalBand
          title="Strengths"
          items={review.strengths}
          fallback="No overall strengths returned."
        />
        <ReviewSignalBand
          title="Weaknesses"
          items={review.weaknesses}
          fallback="No overall weaknesses returned."
        />
        <ReviewSignalBand
          title="Suggestions"
          items={review.suggestions}
          fallback="No overall suggestions returned."
        />
      </div>
    </div>
  );
}

function ResumeSectionReviewGrid({ reviews }: { reviews: ResumeSectionReview[] }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.92)] p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="eyebrow">Section analysis</p>
        <span className="badge bg-[var(--surface-elevated)] text-[var(--text-secondary)]">
          {reviews.length} sections
        </span>
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-2">
        {reviews.map((review) => (
          <ResumeSectionReviewCard key={review.section} review={review} />
        ))}
      </div>
    </div>
  );
}

function ResumeSectionReviewCard({ review }: { review: ResumeSectionReview }) {
  const accent = getSectionAccent(review.section);
  const tier = scoreTier(review.score);

  return (
    <div className="rounded-md border border-[var(--border)] bg-white/95 p-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-2">
          <span
            className="h-2.5 w-2.5 shrink-0 rounded-full"
            style={{ backgroundColor: accent }}
          />
          <div className="min-w-0">
            <p className="text-sm font-semibold text-[var(--text-primary)]">
              {formatSectionName(review.section)}
            </p>
            <p className="mt-1 text-xs text-[var(--text-tertiary)]">{tier.label}</p>
          </div>
        </div>
        <div className="text-right">
          <p className="text-xl font-semibold text-[var(--text-primary)]">
            {clampScore(review.score)}
            <span className="text-xs text-[var(--text-tertiary)]">/100</span>
          </p>
        </div>
      </div>
      <div className="mt-3 grid gap-2">
        <ReviewSignalBand
          title="Strengths"
          items={review.strengths}
          fallback="No strengths returned for this section."
        />
        <ReviewSignalBand
          title="Weaknesses"
          items={review.weaknesses}
          fallback="No weaknesses returned for this section."
        />
        <ReviewSignalBand
          title="Suggestions"
          items={review.suggestions}
          fallback="No suggestions returned for this section."
        />
      </div>
    </div>
  );
}

function ReviewSignalBand({
  title,
  items,
  fallback,
}: {
  title: string;
  items: string[];
  fallback: string;
}) {
  const visibleItems = items.length > 0 ? items : [fallback];

  return (
    <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.94)] px-3 py-2">
      <p className="eyebrow">{title}</p>
      <ul className="mt-2 space-y-1.5 text-sm leading-5 text-[var(--text-secondary)]">
        {visibleItems.map((item) => (
          <li key={item} className="break-words">
            {item}
          </li>
        ))}
      </ul>
    </div>
  );
}

function getSectionAccent(section: string) {
  switch (section.toLowerCase()) {
    case "contact":
      return "var(--blue)";
    case "summary":
      return "var(--accent)";
    case "experience":
      return "var(--green)";
    case "education":
      return "var(--violet)";
    case "skills":
      return "var(--yellow)";
    case "projects":
      return "var(--red)";
    default:
      return "var(--text-tertiary)";
  }
}

function ResumeExtractionContractPanel({
  version,
}: {
  version: NonNullable<ResumeDocument["latestVersion"]>;
}) {
  const metadata = version.extractionMetadata;
  const rows = [
    {
      label: "Method",
      value: metadata.extractionMethod || "Not recorded",
    },
    {
      label: "Prompt",
      value: metadata.promptVersion ?? "Not recorded",
    },
    {
      label: "Extracted",
      value: metadata.extractedAtUtc ? <LocalTime value={metadata.extractedAtUtc} /> : "Not recorded",
    },
    {
      label: "Source",
      value: metadata.sourceFileName ?? metadata.sourceKind,
    },
  ];

  return (
    <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="eyebrow">Extraction contract</p>
        <span className="badge bg-[var(--surface-elevated)] text-[var(--text-secondary)]">
          {metadata.sourceKind}
        </span>
      </div>
      <div className="mt-3 grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
        {rows.map((row) => (
          <div key={row.label} className="rounded-md border border-[var(--border)] bg-white/45 px-3 py-2">
            <p className="eyebrow">{row.label}</p>
            <p className="mt-1 break-words text-sm font-semibold text-[var(--text-primary)]">
              {row.value}
            </p>
          </div>
        ))}
      </div>
    </div>
  );
}

function ResumeModelRunPanel({ modelRun }: { modelRun: ResumeModelRun | null }) {
  if (!modelRun) {
    return (
      <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] p-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="eyebrow">Model run</p>
          <span className="badge bg-[var(--surface-elevated)] text-[var(--text-secondary)]">
            deterministic
          </span>
        </div>
        <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
          This review used deterministic scoring or was created before model call
          telemetry was available.
        </p>
      </div>
    );
  }

  const runRows = [
    {
      label: "Provider",
      value: modelRun.provider ?? "Not recorded",
    },
    {
      label: "Model",
      value: modelRun.model ?? "Not recorded",
    },
    {
      label: "Latency",
      value: formatLatency(modelRun.latencyMs),
    },
    {
      label: "Tokens",
      value: formatTokenCount(modelRun.totalTokens),
      detail:
        modelRun.promptTokens !== null || modelRun.completionTokens !== null
          ? `${formatTokenCount(modelRun.promptTokens)} prompt · ${formatTokenCount(modelRun.completionTokens)} completion`
          : null,
    },
    {
      label: "Cost",
      value: formatEstimatedCost(modelRun.estimatedCostUsd),
    },
    {
      label: "Recorded",
      value: modelRun.occurredAtUtc ? <LocalTime value={modelRun.occurredAtUtc} /> : "Not recorded",
    },
  ];

  return (
    <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <p className="eyebrow">Model run</p>
          <p className="mt-1 break-all font-mono text-[11px] text-[var(--text-tertiary)]">
            {modelRun.runId}
          </p>
        </div>
        <span className="badge bg-[var(--surface-elevated)] text-[var(--text-secondary)]">
          {modelRun.outcome}
        </span>
      </div>
      <div className="mt-3 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {runRows.map((row) => (
          <div key={row.label} className="rounded-md border border-[var(--border)] bg-white/45 px-3 py-2">
            <p className="eyebrow">{row.label}</p>
            <p className="mt-1 break-words text-sm font-semibold text-[var(--text-primary)]">
              {row.value}
            </p>
            {"detail" in row && row.detail ? (
              <p className="mt-1 text-xs text-[var(--text-tertiary)]">{row.detail}</p>
            ) : null}
          </div>
        ))}
      </div>
      {modelRun.errorType || modelRun.statusCode ? (
        <p className="mt-3 rounded-md border border-[var(--border)] bg-white/45 px-3 py-2 text-xs text-[var(--text-secondary)]">
          {modelRun.errorType ? `Error: ${modelRun.errorType}` : "No error type recorded"}
          {modelRun.statusCode ? ` · status ${modelRun.statusCode}` : ""}
        </p>
      ) : null}
    </div>
  );
}

function ResumeCorrectionPanel({
  document,
  canPersist,
  fieldPath,
  correctedValue,
  note,
  isCorrecting,
  onFieldPathChange,
  onCorrectedValueChange,
  onNoteChange,
  onSubmit,
}: {
  document: ResumeDocument | null;
  canPersist: boolean;
  fieldPath: string;
  correctedValue: string;
  note: string;
  isCorrecting: boolean;
  onFieldPathChange: (value: string) => void;
  onCorrectedValueChange: (value: string) => void;
  onNoteChange: (value: string) => void;
  onSubmit: (versionId: string) => void;
}) {
  const version = document?.latestVersion ?? null;
  const canSubmit = Boolean(version) && canPersist && correctedValue.trim().length > 0 && !isCorrecting;
  const recentCorrections = version?.corrections.slice(0, 3) ?? [];

  if (!version) {
    return (
      <div className="rounded-md border border-[var(--border)] bg-white/45 p-3">
        <p className="mt-3 text-sm leading-6 text-[var(--text-secondary)]">
          Add a resume first. Corrections attach to the saved resume version and
          then recompute the structured extraction and evaluation.
        </p>
      </div>
    );
  }

  return (
    <div className="grid gap-3">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-[var(--text-secondary)]">
          Write a correction that should persist into the next extraction.
        </p>
        <span className="badge">{version.corrections.length}</span>
      </div>
      <div className="grid gap-3">
        <label className="grid gap-2">
          <span className="eyebrow">Field</span>
          <select
            className="input"
            value={fieldPath}
            onChange={(event) => onFieldPathChange(event.target.value)}
            disabled={!version || isCorrecting}
          >
            <option value="summary">Summary</option>
            <option value="skills">Skills</option>
            <option value="projectHighlights">Project highlights</option>
            <option value="experienceCount">Experience count</option>
            <option value="projectCount">Project count</option>
            <option value="education">Education signal</option>
            <option value="contact">Contact signal</option>
            <option value="links">Links</option>
          </select>
        </label>
        <label className="grid gap-2">
          <span className="eyebrow">Correction</span>
          <textarea
            className="min-h-28 rounded-md border border-[var(--border-strong)] bg-white px-3 py-3 text-sm leading-6 text-[var(--text-primary)] outline-none transition focus:border-[var(--accent)]"
            value={correctedValue}
            onChange={(event) => onCorrectedValueChange(event.target.value)}
            placeholder="Write the corrected value the agent should remember."
            maxLength={2000}
            disabled={!version || isCorrecting}
          />
        </label>
        <label className="grid gap-2">
          <span className="eyebrow">Note</span>
          <input
            className="input"
            value={note}
            onChange={(event) => onNoteChange(event.target.value)}
            placeholder="Optional reason or context"
            maxLength={1000}
            disabled={!version || isCorrecting}
          />
        </label>
        <button
          type="button"
          className="btn btn-secondary"
          disabled={!canSubmit}
          onClick={() => version && onSubmit(version.id)}
        >
          {isCorrecting ? <><span className="spinner" /> Saving...</> : "Save Correction"}
        </button>
      </div>

      {recentCorrections.length > 0 ? (
        <div className="grid gap-2">
          {recentCorrections.map((correction) => (
            <p
              key={correction.id}
              className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-2 text-sm text-[var(--text-secondary)]"
            >
              <span className="font-semibold text-[var(--text-primary)]">
                {correction.fieldPath}:
              </span>{" "}
              {correction.correctedValue}
            </p>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function ResumeDocumentsPanel({ documents }: { documents: ResumeDocument[] }) {
  return (
    <div className="grid gap-3">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-[var(--text-secondary)]">
          Resume versions imported into this workspace.
        </p>
        <span className="badge">{documents.length}</span>
      </div>
      <div className="grid gap-3">
        {documents.length === 0 ? (
          <p className="text-sm text-[var(--text-secondary)]">No saved resumes yet.</p>
        ) : (
          documents.map((document) => (
            <article
              key={document.id}
              className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] p-3"
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="truncate text-sm font-semibold text-[var(--text-primary)]">
                    {document.title}
                  </p>
                  <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                    {document.sourceKind} · {document.latestEvaluation?.score ?? 0}/100 ·{" "}
                    {document.latestVersion?.corrections.length ?? 0} corrections
                  </p>
                </div>
                <span className="badge bg-[var(--surface-overlay)] text-[var(--text-secondary)]">
                  {document.latestVersion?.skills.length ?? 0} skills
                </span>
              </div>
            </article>
          ))
        )}
      </div>
    </div>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.94)] px-3 py-2">
      <p className="eyebrow">{label}</p>
      <p className="mt-1 font-semibold text-[var(--text-primary)]">{value}</p>
    </div>
  );
}

function SignalList({
  title,
  items,
  fallback,
}: {
  title: string;
  items: string[];
  fallback: string;
}) {
  const visibleItems = items.length > 0 ? items : [fallback];

  return (
    <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.94)] p-3">
      <p className="eyebrow">{title}</p>
      <ul className="mt-2 space-y-2 text-sm leading-5 text-[var(--text-secondary)]">
        {visibleItems.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  );
}
