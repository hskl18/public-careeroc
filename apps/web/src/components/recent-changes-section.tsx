"use client";

import Link from "next/link";
import { useState } from "react";
import type { RecentActivity } from "@/lib/dashboard";
import { formatTimestampInTimeZone } from "@/lib/date-time";

type ActivityGroup = {
  key: string;
  jobApplicationId: string;
  company: string;
  role: string;
  activities: RecentActivity[];
};

type CompanyActivityGroup = {
  key: string;
  company: string;
  roleGroups: ActivityGroup[];
};

type RankedCompanyActivityGroup = CompanyActivityGroup & {
  importanceScore: number;
  latestActivityTime: number;
};

type RecentChangesSectionProps = {
  recentActivity: RecentActivity[];
  coldStart: boolean;
  timeZoneId: string;
  variant?: "preview" | "full";
};

const recentChangeDisplayLimit = 5;
const previewRowLimit = 3;

const activityCopy: Record<string, { label: string; dotClassName: string }> = {
  recruiter_outreach: { label: "Recruiter", dotClassName: "bg-[var(--blue)]" },
  application_received: { label: "Applied", dotClassName: "bg-[var(--green)]" },
  oa_invitation: { label: "OA invite", dotClassName: "bg-[var(--violet)]" },
  interview_invitation: { label: "Interview invite", dotClassName: "bg-[var(--accent)]" },
  interview_confirmation: { label: "Interview set", dotClassName: "bg-[var(--accent)]" },
  rejection: { label: "Rejected", dotClassName: "bg-[var(--red)]" },
  offer: { label: "Offer", dotClassName: "bg-[var(--green)]" },
  reminder_completed: { label: "Task done", dotClassName: "bg-[var(--yellow)]" },
  reminder_dismissed: { label: "Task dismissed", dotClassName: "bg-[var(--yellow)]" },
  reminder_reopened: { label: "Task reopened", dotClassName: "bg-[var(--yellow)]" },
  gmail_reply_detected: { label: "You replied", dotClassName: "bg-[var(--green)]" },
  manual_stage_update: { label: "Status changed", dotClassName: "bg-[var(--blue)]" },
};

function normalizeCategory(category: string) {
  return category
    .replace(/([a-z0-9])([A-Z])/g, "$1_$2")
    .replace(/[\s-]+/g, "_")
    .toLowerCase();
}

function normalizeCompanyIdentity(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .split(" ")
    .filter(
      (token) =>
        token &&
        ![
          "inc",
          "incorporated",
          "llc",
          "ltd",
          "limited",
          "co",
          "company",
          "corp",
          "corporation",
          "plc",
          "gmbh",
          "ag",
          "lp",
          "llp",
        ].includes(token),
    )
    .join("");
}

function normalizeRoleIdentity(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .split(" ")
    .filter(
      (token) =>
        token && !["role", "position", "opening", "opportunity"].includes(token),
    )
    .join("");
}

function isReminderActivity(activity: RecentActivity) {
  return normalizeCategory(activity.category).startsWith("reminder_");
}

function getActivityImportanceScore(activity: RecentActivity) {
  return Math.min(100, Math.max(0, activity.importanceScore));
}

function getActivityCopy(activity: RecentActivity) {
  return (
    activityCopy[normalizeCategory(activity.category)] ?? {
      label: "Update",
      dotClassName: "bg-[var(--blue)]",
    }
  );
}

function buildActivityGroups(recentActivity: RecentActivity[]) {
  const groups = new Map<string, ActivityGroup>();

  for (const activity of recentActivity) {
    const key = `${normalizeCompanyIdentity(activity.company)}:${normalizeRoleIdentity(activity.role)}`;
    const group = groups.get(key);

    if (group) {
      group.activities.push(activity);
      continue;
    }

    groups.set(key, {
      key,
      jobApplicationId: activity.jobApplicationId,
      company: activity.company,
      role: activity.role,
      activities: [activity],
    });
  }

  return Array.from(groups.values()).map((group) => ({
    ...group,
    activities: sortActivitiesByRecency(group.activities),
  }));
}

function buildCompanyActivityGroups(recentActivity: RecentActivity[]) {
  const companyGroups = new Map<string, CompanyActivityGroup>();

  for (const roleGroup of buildActivityGroups(recentActivity)) {
    const key = normalizeCompanyIdentity(roleGroup.company);
    const group = companyGroups.get(key);

    if (group) {
      group.roleGroups.push(roleGroup);
      continue;
    }

    companyGroups.set(key, {
      key,
      company: roleGroup.company,
      roleGroups: [roleGroup],
    });
  }

  return Array.from(companyGroups.values()).map((group) => ({
    ...group,
    roleGroups: group.roleGroups.sort(
      (left, right) =>
        getLatestActivityTime(right.activities) - getLatestActivityTime(left.activities),
    ),
  }));
}

function getLatestActivityTime(activities: RecentActivity[]) {
  return Math.max(
    ...activities.map((activity) => new Date(activity.occurredAtUtc).getTime()),
  );
}

function compareActivitiesByRecency(left: RecentActivity, right: RecentActivity) {
  return (
    new Date(right.occurredAtUtc).getTime() -
    new Date(left.occurredAtUtc).getTime()
  );
}

function sortActivitiesByRecency(activities: RecentActivity[]) {
  return [...activities].sort(compareActivitiesByRecency);
}

function getVisibleActivities(activities: RecentActivity[]) {
  const sorted = sortActivitiesByRecency(activities);
  const nonReminderActivities = sorted.filter(
    (activity) => !isReminderActivity(activity),
  );

  if (nonReminderActivities.length === 0) {
    return sorted;
  }

  const latestReminderActivity = sorted.find(isReminderActivity);
  if (
    latestReminderActivity &&
    new Date(latestReminderActivity.occurredAtUtc).getTime() >
      new Date(nonReminderActivities[0].occurredAtUtc).getTime()
  ) {
    return [latestReminderActivity, ...nonReminderActivities];
  }

  return nonReminderActivities;
}

function getCompanyGroupVisibleActivities(group: CompanyActivityGroup) {
  return sortActivitiesByRecency(
    group.roleGroups.flatMap((roleGroup) =>
      getVisibleActivities(roleGroup.activities),
    ),
  );
}

function getCompanyGroupAllActivities(group: CompanyActivityGroup) {
  return sortActivitiesByRecency(
    group.roleGroups.flatMap((roleGroup) => roleGroup.activities),
  );
}

function getCompanyGroupImportanceScore(group: CompanyActivityGroup) {
  const visibleActivities = getCompanyGroupVisibleActivities(group);
  const activities = visibleActivities.length > 0
    ? visibleActivities
    : getCompanyGroupAllActivities(group);
  const categoryScore = Math.max(...activities.map(getActivityImportanceScore), 0);
  const activityDepthScore = Math.min(12, activities.length * 2);
  const roleBreadthScore = Math.min(4, Math.max(0, group.roleGroups.length - 1) * 2);

  return categoryScore + activityDepthScore + roleBreadthScore;
}

function rankCompanyActivityGroups(
  groups: CompanyActivityGroup[],
): RankedCompanyActivityGroup[] {
  return groups
    .map((group) => ({
      ...group,
      importanceScore: getCompanyGroupImportanceScore(group),
      latestActivityTime: getLatestActivityTime(getCompanyGroupAllActivities(group)),
    }))
    .sort((left, right) => {
      if (right.latestActivityTime !== left.latestActivityTime) {
        return right.latestActivityTime - left.latestActivityTime;
      }

      if (right.importanceScore !== left.importanceScore) {
        return right.importanceScore - left.importanceScore;
      }

      return left.company.localeCompare(right.company);
    });
}

function ExampleChangesPreview() {
  const examples = [
    {
      label: "Interview invite",
      title: "Interview invitation received",
      dot: "bg-[var(--accent)]",
      badge: "bg-[var(--accent-soft)] text-[var(--accent-ink)]",
      company: "Example Co.",
      role: "Example role",
    },
    {
      label: "Receipt",
      title: "Application receipt received",
      dot: "bg-[var(--green)]",
      badge: "bg-[var(--green-soft)] text-[var(--green)]",
      company: "Example Co.",
      role: "Example role",
    },
  ];

  return (
    <div
      className="mt-4 overflow-hidden rounded-md border border-dashed border-[var(--border-hover)]"
      aria-hidden="true"
    >
      <div className="border-b border-dashed border-[var(--border)] bg-[var(--surface-strong)] px-4 py-2">
        <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
          Example
        </span>
        <span className="ml-2 text-xs text-[var(--text-tertiary)]">
          Your real updates will replace this preview
        </span>
      </div>
      <div className="divide-y divide-[var(--border)] opacity-60">
        {examples.map((example) => (
          <div
            key={example.label}
            className="grid gap-3 px-4 py-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-start"
          >
            <div className="flex min-w-0 gap-3">
              <span
                className={`mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full ring-2 ${example.dot}`}
              />
              <div className="min-w-0">
                <p className="text-sm font-medium text-[var(--text-primary)]">
                  {example.title}
                </p>
                <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">
                  {example.company} &middot; {example.role}
                </p>
              </div>
            </div>
            <div className="sm:text-right">
              <span className={`badge ${example.badge}`}>{example.label}</span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

export function RecentChangesSection({
  recentActivity,
  coldStart,
  timeZoneId,
  variant = "preview",
}: RecentChangesSectionProps) {
  const companyGroups = rankCompanyActivityGroups(
    buildCompanyActivityGroups(recentActivity),
  );
  const canOpenFullTimeline =
    variant === "preview" && companyGroups.length > recentChangeDisplayLimit;
  // Touch screens cannot hover, so a tap shows a tag's time until the next tap.
  const [shownTimeId, setShownTimeId] = useState<string | null>(null);

  return (
    <section className="card flex h-full min-h-0 flex-col p-0">
      <div className={`shrink-0 items-center justify-between gap-3 px-4 pt-3 ${variant === "full" ? "hidden" : "flex"}`}>
        <h2 className="text-base font-semibold text-[var(--text-primary)]">Recent activity</h2>
        {canOpenFullTimeline ? (
          <Link
            href="/app/updates"
            className="max-sm:inline-flex max-sm:min-h-6 max-sm:items-center text-xs font-medium text-[var(--text-tertiary)] hover:text-[var(--text-primary)] hover:underline"
          >
            All activity
          </Link>
        ) : null}
      </div>

      {recentActivity.length === 0 ? (
        <div className="px-5 pt-4 pb-5">
          <div className="rounded-md border border-dashed border-[var(--border-hover)] bg-[var(--surface-strong)] p-6">
            <p className="text-sm font-medium text-[var(--text-primary)]">
              {coldStart ? "Nothing synced yet" : "No changes recorded yet"}
            </p>
          </div>
          {coldStart ? <ExampleChangesPreview /> : null}
        </div>
      ) : (
        <div className="workspace-scroll-region min-h-0 flex-1 divide-y divide-[var(--border)] overflow-x-hidden px-4 pt-1 pb-2">
          {companyGroups
            .flatMap((companyGroup, groupIndex) =>
              companyGroup.roleGroups.map((roleGroup) => ({ roleGroup, groupIndex })),
            )
            .map(({ roleGroup, groupIndex }) => {
              const activities = getVisibleActivities(roleGroup.activities);
              // A taller screen shows more of the list; phones scroll the page, so they keep a short preview.
              const phoneHidden = variant === "preview" && groupIndex >= recentChangeDisplayLimit;

              return (
                <article
                  key={roleGroup.key}
                  className={`flex-wrap items-center justify-between gap-x-4 gap-y-1.5 py-2.5 ${phoneHidden ? "hidden md:flex" : "flex"}`}
                >
                  <Link
                    href={`/app/applications/${roleGroup.jobApplicationId}`}
                    className="flex min-w-0 items-baseline gap-2 max-sm:min-h-6 hover:underline"
                  >
                    <span className="shrink-0 text-sm font-medium text-[var(--text-primary)]">
                      {roleGroup.company}
                    </span>
                    <span className="min-w-0 truncate text-xs text-[var(--text-tertiary)]">
                      {roleGroup.role}
                    </span>
                  </Link>
                  {/* Newest first, beside the company when the row has room and below it when not.
                      Each tag shows its time on hover or tap, and screen readers hear it. */}
                  <ul className="flex flex-wrap gap-1.5">
                    {(variant === "full"
                      ? activities
                      : activities.slice(0, previewRowLimit)
                    ).map((activity) => {
                      const copy = getActivityCopy(activity);
                      const time = formatTimestampInTimeZone(activity.occurredAtUtc, timeZoneId, "Waiting for first sync");

                      return (
                        <li key={activity.id} className="relative">
                          <button
                            type="button"
                            className="badge group gap-1.5 text-[var(--text-secondary)] max-sm:min-h-6"
                            onClick={() =>
                              setShownTimeId((current) => (current === activity.id ? null : activity.id))
                            }
                          >
                            <span
                              className={`h-1.5 w-1.5 shrink-0 rounded-full ${copy.dotClassName}`}
                              aria-hidden="true"
                            />
                            {copy.label}
                            <span className="sr-only">, {time}</span>
                            <span
                              aria-hidden="true"
                              className={`pointer-events-none absolute top-full left-1/2 z-10 mt-1 -translate-x-1/2 whitespace-nowrap rounded-md bg-[var(--text-primary)] px-2 py-0.5 text-xs text-[var(--surface)] transition-opacity group-hover:opacity-100 ${
                                shownTimeId === activity.id ? "opacity-100" : "opacity-0"
                              }`}
                            >
                              {time}
                            </span>
                          </button>
                        </li>
                      );
                    })}
                  </ul>
                </article>
              );
            })}
        </div>
      )}
    </section>
  );
}
