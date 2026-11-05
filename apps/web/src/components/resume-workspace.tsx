"use client";

import { useState } from "react";
import { InlineMemoryProposals } from "@/components/inline-memory-proposals";
import { LocalTime } from "@/components/local-time";
import type {
  CreateResumeRequest,
  ResumeDocument,
  ResumeEvaluation,
  ResumeSectionReview,
  ResumeStructuredData,
  ResumeWorkspace,
} from "@/lib/resumes";

const minimumResumeLength = 80;

const cellClass =
  "rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-2.5";

function scoreTier(score: number) {
  if (score >= 85) return "Strong";
  if (score >= 70) return "Solid";
  if (score >= 55) return "Mixed";
  return "Weak";
}

function formatSectionName(value: string) {
  return value
    .replace(/[_-]+/g, " ")
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function Panel({
  eyebrow,
  title,
  children,
}: {
  eyebrow: string;
  title: string;
  children: React.ReactNode;
}) {
  return (
    <section className="card app-workspace-panel p-3 sm:p-4">
      <p className="eyebrow">{eyebrow}</p>
      <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">{title}</h2>
      <div className="mt-3">{children}</div>
    </section>
  );
}

function SignalList({ title, items, empty }: { title: string; items: string[]; empty: string }) {
  return (
    <div className={cellClass}>
      <p className="eyebrow">{title}</p>
      <ul className="mt-2 space-y-1.5 text-sm leading-5 text-[var(--text-secondary)]">
        {(items.length > 0 ? items : [empty]).map((item) => (
          <li key={item} className="break-words">
            {item}
          </li>
        ))}
      </ul>
    </div>
  );
}

function BulletList({ items }: { items: string[] }) {
  if (items.length === 0) return null;
  return (
    <ul className="mt-2 list-disc space-y-1 pl-4 text-sm leading-5 text-[var(--text-secondary)]">
      {items.map((item) => (
        <li key={item} className="break-words">
          {item}
        </li>
      ))}
    </ul>
  );
}

function ParsedResume({ summary, data }: { summary: string; data: ResumeStructuredData }) {
  const skills = [...data.skills.technical, ...data.skills.languages, ...data.skills.other];
  return (
    <Panel eyebrow="Parsed" title="What careeroc read">
      <div className="grid gap-2">
        <p className={`text-sm leading-6 text-[var(--text-secondary)] ${cellClass}`}>{summary}</p>
        {data.experience.map((role) => (
          <article key={`${role.company}-${role.title}-${role.dateRange}`} className={cellClass}>
            <h3 className="text-sm font-semibold text-[var(--text-primary)]">
              {[role.title, role.company].filter(Boolean).join(" at ")}
            </h3>
            <p className="text-xs text-[var(--text-tertiary)]">
              {[role.dateRange, role.location].filter(Boolean).join(" · ")}
            </p>
            <BulletList items={role.bullets} />
          </article>
        ))}
        {data.projects.map((project) => (
          <article key={project.name} className={cellClass}>
            <h3 className="text-sm font-semibold text-[var(--text-primary)]">{project.name}</h3>
            <p className="text-sm leading-5 text-[var(--text-secondary)]">{project.description}</p>
            <BulletList items={project.bullets} />
          </article>
        ))}
        {data.education.map((school) => (
          <article key={`${school.school}-${school.degree}`} className={cellClass}>
            <h3 className="text-sm font-semibold text-[var(--text-primary)]">{school.school}</h3>
            <p className="text-xs text-[var(--text-tertiary)]">
              {[school.degree, school.field, school.dateRange].filter(Boolean).join(" · ")}
            </p>
          </article>
        ))}
        {skills.length > 0 ? (
          <div className="flex flex-wrap gap-2">
            {skills.map((skill) => (
              <span key={skill} className="badge text-[var(--text-secondary)]">
                {skill}
              </span>
            ))}
          </div>
        ) : null}
      </div>
    </Panel>
  );
}

function RecruiterReview({ evaluation }: { evaluation: ResumeEvaluation }) {
  const sections = evaluation.sectionReviews.filter(
    (review: ResumeSectionReview) => review.section.toLowerCase() !== "overall",
  );
  return (
    <Panel eyebrow="Review" title={evaluation.verdict}>
      <div className="grid gap-2 md:grid-cols-3">
        <SignalList title="Strengths" items={evaluation.strengths} empty="None detected yet." />
        <SignalList title="Gaps" items={evaluation.gaps} empty="No major gaps detected." />
        <SignalList title="Next actions" items={evaluation.nextActions} empty="Nothing to do yet." />
      </div>
      {sections.length > 0 ? (
        <div className="mt-2 grid gap-2 lg:grid-cols-2">
          {sections.map((review) => (
            <article key={review.section} className={cellClass}>
              <div className="flex items-baseline justify-between gap-3">
                <h3 className="text-sm font-semibold text-[var(--text-primary)]">
                  {formatSectionName(review.section)}
                </h3>
                <p className="text-xs text-[var(--text-tertiary)]">
                  {review.score}/100 · {scoreTier(review.score)}
                </p>
              </div>
              <BulletList items={[...review.weaknesses, ...review.suggestions]} />
            </article>
          ))}
        </div>
      ) : null}
    </Panel>
  );
}

function NewResumeForm({
  proxyPrefix,
  onCreated,
}: {
  proxyPrefix: string | null;
  onCreated: (workspace: ResumeWorkspace) => void;
}) {
  const [title, setTitle] = useState("");
  const [sourceText, setSourceText] = useState("");
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const canSave = Boolean(proxyPrefix) && sourceText.trim().length >= minimumResumeLength;

  async function save() {
    if (!proxyPrefix || !canSave) return;
    setIsSaving(true);
    setError(null);
    try {
      const request: CreateResumeRequest = {
        title: title.trim() || null,
        sourceKind: "text",
        sourceText: sourceText.trim(),
      };
      const response = await fetch(`${proxyPrefix}/api/resumes`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(request),
      });
      if (!response.ok) {
        const payload = (await response.json().catch(() => null)) as { error?: string } | null;
        throw new Error(payload?.error ?? `Request failed with ${response.status}`);
      }
      onCreated((await response.json()) as ResumeWorkspace);
      setTitle("");
      setSourceText("");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The resume could not be saved.");
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <div className="grid gap-2">
      <input
        aria-label="Resume title"
        className="input"
        placeholder="Title (optional)"
        value={title}
        onChange={(event) => setTitle(event.target.value)}
      />
      <textarea
        aria-label="Resume text"
        className="input min-h-48"
        placeholder="Paste the text of your resume"
        value={sourceText}
        onChange={(event) => setSourceText(event.target.value)}
      />
      <div className="flex items-center gap-3">
        <button
          type="button"
          className="btn btn-primary btn-sm"
          disabled={!canSave || isSaving}
          onClick={() => void save()}
        >
          {isSaving ? "Saving..." : "Save resume"}
        </button>
        {error ? <p className="text-sm font-medium text-[var(--red)]">{error}</p> : null}
      </div>
    </div>
  );
}

function ResumeDetail({
  document,
  proxyPrefix,
  children,
}: {
  document: ResumeDocument;
  proxyPrefix: string | null;
  children: React.ReactNode;
}) {
  const version = document.latestVersion;
  const evaluation = document.latestEvaluation;
  return (
    <>
      <section className="card app-workspace-panel p-3 sm:p-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <p className="eyebrow">Latest resume</p>
            <h1 className="mt-1 break-words text-xl font-semibold text-[var(--text-primary)] sm:mt-2 sm:text-2xl">
              {document.title}
            </h1>
            <p className="mt-1 text-xs text-[var(--text-tertiary)]">
              Saved <LocalTime value={document.createdAtUtc} />
            </p>
          </div>
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4 lg:shrink-0">
            {[
              ["Score", evaluation?.score ?? 0],
              ["Skills", version?.skills.length ?? 0],
              ["Roles", version?.experienceCount ?? 0],
              ["Projects", version?.projectCount ?? 0],
            ].map(([label, value]) => (
              <div key={label} className={cellClass}>
                <p className="eyebrow">{label}</p>
                <p className="mt-1 font-semibold text-[var(--text-primary)]">{value}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {evaluation ? <RecruiterReview evaluation={evaluation} /> : null}
      {version?.structuredData ? (
        <ParsedResume summary={version.summary} data={version.structuredData} />
      ) : null}
      <InlineMemoryProposals
        proposals={document.memoryProposals}
        proxyPrefix={proxyPrefix}
        title="Resume memory"
        emptyCopy="No resume-scoped memory proposals are waiting here."
      />
      {children}
    </>
  );
}

export function ResumeWorkspace({
  initialWorkspace,
  proxyPrefix,
}: {
  initialWorkspace: ResumeWorkspace;
  proxyPrefix: string | null;
}) {
  const [workspace, setWorkspace] = useState(initialWorkspace);
  const latest = workspace.documents[0] ?? null;

  return (
    <main className="app-scroll-main">
      <div className="mx-auto flex min-h-full w-full max-w-[104rem] flex-col gap-3 px-3 pb-6 pt-4 animate-in sm:gap-4 sm:px-5 sm:pt-6">
        {latest ? (
          <ResumeDetail document={latest} proxyPrefix={proxyPrefix}>
            <details className="card app-workspace-panel p-3 sm:p-4">
              <summary className="text-sm font-semibold text-[var(--text-primary)]">
                Save a new version
              </summary>
              <div className="mt-3">
                <NewResumeForm proxyPrefix={proxyPrefix} onCreated={setWorkspace} />
              </div>
            </details>
          </ResumeDetail>
        ) : (
          <Panel eyebrow="Resume" title="Add your resume">
            <NewResumeForm proxyPrefix={proxyPrefix} onCreated={setWorkspace} />
          </Panel>
        )}
      </div>
    </main>
  );
}
