"use client";

import { useState } from "react";
import { InlineMemoryProposals } from "@/components/inline-memory-proposals";
import { WorkspaceMetricTile } from "@/components/workspace-controls";
import type {
  CreateResumeRequest,
  ResumeDocument,
  ResumeEvaluation,
  ResumeSectionReview,
  ResumeStructuredData,
  ResumeWorkspace,
} from "@/lib/resumes";

const minimumResumeLength = 80;

function formatSectionName(value: string) {
  return value
    .replace(/[_-]+/g, " ")
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="card flex min-h-0 flex-1 flex-col p-0">
      <h2 className="shrink-0 px-4 pt-3 text-base font-semibold text-[var(--text-primary)]">{title}</h2>
      <div className="workspace-scroll-region flex min-h-0 flex-1 flex-col px-4 pb-3 pt-2">{children}</div>
    </section>
  );
}

function ParsedResume({ data }: { data: ResumeStructuredData }) {
  const skills = [...new Set([...data.skills.technical, ...data.skills.languages, ...data.skills.other])];
  const rows = [
    ...data.experience.map((role) => ({
      key: `${role.company}-${role.title}-${role.dateRange}`,
      title: [role.title, role.company].filter(Boolean).join(" at "),
      detail: role.dateRange,
    })),
    ...data.projects.map((project) => ({ key: project.name, title: project.name, detail: "Project" })),
    ...data.education.map((school) => ({
      key: `${school.school}-${school.degree}`,
      title: school.school,
      detail: school.dateRange,
    })),
  ];
  return (
    <Panel title="What careeroc read">
      <ul className="fill-rows flex-1 divide-y divide-[var(--border)]">
        {rows.map((row) => (
          <li key={row.key} className="flex items-center justify-between gap-3 py-1.5">
            <span className="min-w-0 truncate text-sm text-[var(--text-primary)]">{row.title}</span>
            <span className="shrink-0 text-xs text-[var(--text-tertiary)]">{row.detail}</span>
          </li>
        ))}
      </ul>
      {skills.length > 0 ? (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {skills.map((skill) => (
            <span key={skill} className="badge text-[var(--text-secondary)]">
              {skill}
            </span>
          ))}
        </div>
      ) : null}
    </Panel>
  );
}

function ToFix({ evaluation }: { evaluation: ResumeEvaluation }) {
  const items = [...new Set([...evaluation.gaps, ...evaluation.nextActions])];
  return (
    <Panel title="To fix">
      {items.length > 0 ? (
        <ul className="fill-rows flex-1 divide-y divide-[var(--border)]">
          {items.map((item) => (
            <li key={item} className="py-1.5 text-sm text-[var(--text-secondary)]">
              {item}
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-sm text-[var(--text-tertiary)]">Nothing to fix.</p>
      )}
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
  const sections = (evaluation?.sectionReviews ?? []).filter(
    (review: ResumeSectionReview) => review.section.toLowerCase() !== "overall",
  );
  return (
    <>
      <div className="shrink-0">
        <h1 className="truncate text-xl font-semibold text-[var(--text-primary)]">{document.title}</h1>
        {evaluation ? (
          <p className="truncate text-sm text-[var(--text-secondary)]">{evaluation.verdict}</p>
        ) : null}
      </div>

      <div className="grid shrink-0 grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-6">
        <WorkspaceMetricTile
          label="Score"
          value={evaluation?.score ?? "—"}
          tone="var(--accent)"
        />
        {sections.map((review) => (
          <WorkspaceMetricTile
            key={review.section}
            label={formatSectionName(review.section)}
            value={review.score}
          />
        ))}
      </div>

      <div className="grid min-h-0 flex-1 gap-3 xl:grid-cols-2 xl:[grid-template-rows:minmax(0,1fr)]">
        <div className="flex min-h-0 flex-col gap-3">
          {evaluation ? <ToFix evaluation={evaluation} /> : null}
          {(document.memoryProposals?.length ?? 0) > 0 ? (
            <div className="shrink-0">
              <InlineMemoryProposals proposals={document.memoryProposals} proxyPrefix={proxyPrefix} />
            </div>
          ) : null}
          <div className="shrink-0">{children}</div>
        </div>
        {version?.structuredData ? <ParsedResume data={version.structuredData} /> : null}
      </div>
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
      <div className="workspace-shell flex w-full flex-col gap-3 px-3 py-4 animate-in sm:gap-4 sm:px-5 sm:py-6">
        {latest ? (
          <ResumeDetail document={latest} proxyPrefix={proxyPrefix}>
            <details className="card px-4 py-2.5">
              <summary className="text-sm font-medium text-[var(--text-primary)]">New version</summary>
              <div className="mt-3">
                <NewResumeForm proxyPrefix={proxyPrefix} onCreated={setWorkspace} />
              </div>
            </details>
          </ResumeDetail>
        ) : (
          <Panel title="Add your resume">
            <NewResumeForm proxyPrefix={proxyPrefix} onCreated={setWorkspace} />
          </Panel>
        )}
      </div>
    </main>
  );
}
