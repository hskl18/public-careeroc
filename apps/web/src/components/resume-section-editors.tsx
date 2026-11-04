"use client";

import type {
  ResumeEducation,
  ResumeExperience,
  ResumeProject,
  ResumeStructuredData,
} from "@/lib/resumes";
import {
  AddCardButton,
  ArrayCardControls,
  EditableStringList,
  EditableText,
  type ResumePatchFn,
} from "./resume-editing-primitives";

export function ResumeSkillsEditor({
  skills,
  canEdit,
  onPatch,
}: {
  skills: ResumeStructuredData["skills"];
  canEdit: boolean;
  onPatch: ResumePatchFn;
}) {
  return (
    <div className="grid gap-4">
      <div>
        <p className="eyebrow">Technical</p>
        <div className="mt-2">
          <EditableStringList
            items={skills.technical}
            pathPrefix="/skills/technical"
            canEdit={canEdit}
            onPatch={onPatch}
            noteLabel="technical skill"
            emptyLabel="No technical skills were detected."
            addPlaceholder="Add a technical skill"
            maxLength={120}
          />
        </div>
      </div>
      <div>
        <p className="eyebrow">Languages</p>
        <div className="mt-2">
          <EditableStringList
            items={skills.languages}
            pathPrefix="/skills/languages"
            canEdit={canEdit}
            onPatch={onPatch}
            noteLabel="language"
            emptyLabel="No languages recorded."
            addPlaceholder="Add a language"
            maxLength={120}
          />
        </div>
      </div>
      <div>
        <p className="eyebrow">Other</p>
        <div className="mt-2">
          <EditableStringList
            items={skills.other}
            pathPrefix="/skills/other"
            canEdit={canEdit}
            onPatch={onPatch}
            noteLabel="other skill"
            emptyLabel="No other skills recorded."
            addPlaceholder="Add a skill"
            maxLength={120}
          />
        </div>
      </div>
    </div>
  );
}

export function ResumeExperienceEditor({
  items,
  canEdit,
  onPatch,
}: {
  items: ResumeExperience[];
  canEdit: boolean;
  onPatch: ResumePatchFn;
}) {
  async function addRole() {
    const empty: ResumeExperience = {
      company: "",
      title: "",
      location: "",
      dateRange: "",
      bullets: [],
    };
    await onPatch(
      [{ op: "add", path: "/experience/-", value: empty }],
      "Added role",
    );
  }

  return (
    <div className="grid gap-3">
      {items.length === 0 && !canEdit ? (
        <p className="text-sm text-[var(--text-secondary)]">
          No experience roles were extracted.
        </p>
      ) : null}
      {items.map((item, index) => (
        <article
          key={index}
          className="rounded-md border border-[var(--border)] bg-white/55 p-4"
        >
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <p className="eyebrow">Role {index + 1}</p>
              <h4 className="mt-1 text-base font-semibold text-[var(--text-primary)]">
                {item.company || "Untitled role"}
              </h4>
            </div>
            <ArrayCardControls
              index={index}
              items={items}
              pathPrefix="/experience"
              canEdit={canEdit}
              onPatch={onPatch}
              itemLabel="role"
            />
          </div>
          <div className="mt-3 grid gap-2 md:grid-cols-2">
            <EditableText
              label="Company"
              value={item.company}
              path={`/experience/${index}/company`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="company"
              maxLength={140}
            />
            <EditableText
              label="Title"
              value={item.title}
              path={`/experience/${index}/title`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="title"
              maxLength={140}
            />
            <EditableText
              label="Location"
              value={item.location}
              path={`/experience/${index}/location`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="location"
              maxLength={100}
            />
            <EditableText
              label="Dates"
              value={item.dateRange}
              path={`/experience/${index}/dateRange`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="date range"
              maxLength={100}
            />
          </div>
          <div className="mt-3">
            <p className="eyebrow mb-1.5">Bullets</p>
            <EditableStringList
              items={item.bullets}
              pathPrefix={`/experience/${index}/bullets`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="bullet"
              variant="row"
              multiline
              addPlaceholder="Add an achievement bullet"
              emptyLabel="No bullets recorded."
              maxLength={500}
            />
          </div>
        </article>
      ))}
      <AddCardButton canEdit={canEdit} onAdd={addRole} label="Add role" />
    </div>
  );
}

export function ResumeEducationEditor({
  items,
  canEdit,
  onPatch,
}: {
  items: ResumeEducation[];
  canEdit: boolean;
  onPatch: ResumePatchFn;
}) {
  async function addEducation() {
    const empty: ResumeEducation = {
      school: "",
      degree: "",
      field: "",
      dateRange: "",
      gpa: "",
      notes: [],
    };
    await onPatch(
      [{ op: "add", path: "/education/-", value: empty }],
      "Added education entry",
    );
  }

  return (
    <div className="grid gap-3">
      {items.length === 0 && !canEdit ? (
        <p className="text-sm text-[var(--text-secondary)]">
          No education section was extracted.
        </p>
      ) : null}
      {items.map((item, index) => (
        <article
          key={index}
          className="rounded-md border border-[var(--border)] bg-white/55 p-4"
        >
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <p className="eyebrow">Education {index + 1}</p>
              <h4 className="mt-1 font-semibold text-[var(--text-primary)]">
                {item.school || "Untitled education"}
              </h4>
            </div>
            <ArrayCardControls
              index={index}
              items={items}
              pathPrefix="/education"
              canEdit={canEdit}
              onPatch={onPatch}
              itemLabel="education entry"
            />
          </div>
          <div className="mt-3 grid gap-2 md:grid-cols-2">
            <EditableText
              label="School"
              value={item.school}
              path={`/education/${index}/school`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="school"
              maxLength={180}
            />
            <EditableText
              label="Degree"
              value={item.degree}
              path={`/education/${index}/degree`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="degree"
              maxLength={100}
            />
            <EditableText
              label="Field"
              value={item.field}
              path={`/education/${index}/field`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="field"
              maxLength={160}
            />
            <EditableText
              label="Dates"
              value={item.dateRange}
              path={`/education/${index}/dateRange`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="date range"
              maxLength={100}
            />
            <EditableText
              label="GPA"
              value={item.gpa}
              path={`/education/${index}/gpa`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="GPA"
              maxLength={60}
            />
          </div>
          <div className="mt-3">
            <p className="eyebrow mb-1.5">Notes</p>
            <EditableStringList
              items={item.notes}
              pathPrefix={`/education/${index}/notes`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="education note"
              variant="row"
              multiline
              addPlaceholder="Add a note (coursework, awards, GPA detail)"
              emptyLabel="No notes recorded."
              maxLength={300}
            />
          </div>
        </article>
      ))}
      <AddCardButton
        canEdit={canEdit}
        onAdd={addEducation}
        label="Add education entry"
      />
    </div>
  );
}

export function ResumeProjectEditor({
  projects,
  canEdit,
  onPatch,
}: {
  projects: ResumeProject[];
  canEdit: boolean;
  onPatch: ResumePatchFn;
}) {
  async function addProject() {
    const empty: ResumeProject = {
      name: "",
      description: "",
      tech: [],
      links: [],
      bullets: [],
    };
    await onPatch(
      [{ op: "add", path: "/projects/-", value: empty }],
      "Added project",
    );
  }

  return (
    <div className="grid gap-3">
      {projects.length === 0 && !canEdit ? (
        <p className="text-sm text-[var(--text-secondary)]">
          No project highlights were extracted.
        </p>
      ) : null}
      {projects.map((project, index) => (
        <article
          key={index}
          className="rounded-xl border border-[var(--border)] bg-white/58 p-3 shadow-[0_12px_30px_rgba(15,23,42,0.04)] sm:p-4"
        >
          <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-start">
            <div className="min-w-0">
              <p className="eyebrow">Project {index + 1}</p>
              <h4 className="mt-1 break-words text-lg font-semibold leading-snug text-[var(--text-primary)]">
                {project.name || "Untitled project"}
              </h4>
              <div className="mt-2 flex flex-wrap gap-1.5 text-[11px] font-semibold text-[var(--text-tertiary)]">
                <span className="rounded-full border border-[var(--border)] bg-white/70 px-2.5 py-1">
                  {project.tech.length} tech
                </span>
                <span className="rounded-full border border-[var(--border)] bg-white/70 px-2.5 py-1">
                  {project.bullets.length} bullets
                </span>
                {project.links.length > 0 ? (
                  <span className="rounded-full border border-[var(--border)] bg-white/70 px-2.5 py-1">
                    {project.links.length} links
                  </span>
                ) : null}
              </div>
            </div>
            <ArrayCardControls
              index={index}
              items={projects}
              pathPrefix="/projects"
              canEdit={canEdit}
              onPatch={onPatch}
              itemLabel="project"
            />
          </div>
          <div className="mt-3 grid gap-2">
            <EditableText
              label="Name"
              value={project.name}
              path={`/projects/${index}/name`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="project name"
              maxLength={180}
            />
            <EditableText
              label="Description"
              value={project.description}
              path={`/projects/${index}/description`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="project description"
              multiline
              maxLength={800}
            />
          </div>
          <div className="mt-4 border-t border-[var(--border)] pt-3">
            <p className="eyebrow mb-2">Tech</p>
            <EditableStringList
              items={project.tech}
              pathPrefix={`/projects/${index}/tech`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="tech"
              addPlaceholder="Add a tech tag"
              emptyLabel="No tech tagged yet."
              maxLength={80}
            />
          </div>
          <div className="mt-4 border-t border-[var(--border)] pt-3">
            <p className="eyebrow mb-2">Links</p>
            <EditableStringList
              items={project.links}
              pathPrefix={`/projects/${index}/links`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="link"
              addPlaceholder="https://…"
              emptyLabel="No links recorded."
              maxLength={300}
            />
          </div>
          <div className="mt-4 border-t border-[var(--border)] pt-3">
            <p className="eyebrow mb-2">Bullets</p>
            <EditableStringList
              items={project.bullets}
              pathPrefix={`/projects/${index}/bullets`}
              canEdit={canEdit}
              onPatch={onPatch}
              noteLabel="project bullet"
              variant="row"
              multiline
              addPlaceholder="Add an achievement bullet"
              emptyLabel="No bullets recorded."
              maxLength={500}
            />
          </div>
        </article>
      ))}
      <AddCardButton canEdit={canEdit} onAdd={addProject} label="Add project" />
    </div>
  );
}
