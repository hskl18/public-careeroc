"use client";

import Link from "next/link";
import type { RecentActivity } from "@/lib/dashboard";

type ActivityCopy = {
  label: string;
  title: string;
  summarySingular: string;
  summaryPlural: string;
  badgeClassName: string;
  dotClassName: string;
  timestampLabel: string;
  showLoggedTimestamp: boolean;
};

type ActivityKindSummary = {
  text: string;
  badgeClassName: string;
  dotClassName: string;
};

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

const summaryFormatter = new Intl.ListFormat("en-US", {
  style: "long",
  type: "conjunction",
});

function formatTimestamp(value: string | null, timeZoneId: string) {
  if (!value) return "Waiting for first sync";

  return new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZone: timeZoneId,
  }).format(new Date(value));
}

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

function getActivityCopy(activity: RecentActivity): ActivityCopy {
  switch (normalizeCategory(activity.category)) {
    case "recruiter_outreach":
      return {
        label: "Outreach",
        title: "Recruiter reached out",
        summarySingular: "recruiter message",
        summaryPlural: "recruiter messages",
        badgeClassName: "bg-[var(--blue-soft)] text-[var(--blue)]",
        dotClassName: "bg-[var(--blue)]",
        timestampLabel: "Received",
        showLoggedTimestamp: false,
      };
    case "application_received":
      return {
        label: "Receipt",
        title: "Application receipt received",
        summarySingular: "receipt",
        summaryPlural: "receipts",
        badgeClassName: "bg-[var(--green-soft)] text-[var(--green)]",
        dotClassName: "bg-[var(--green)]",
        timestampLabel: "Received",
        showLoggedTimestamp: false,
      };
    case "oa_invitation":
      return {
        label: "OA",
        title: "OA invitation received",
        summarySingular: "oa",
        summaryPlural: "oa",
        badgeClassName: "bg-[var(--violet-soft)] text-[var(--violet)]",
        dotClassName: "bg-[var(--violet)]",
        timestampLabel: "Received",
        showLoggedTimestamp: false,
      };
    case "interview_invitation":
      return {
        label: "Interview",
        title: "Interview invitation received",
        summarySingular: "interview invitation",
        summaryPlural: "interview invitations",
        badgeClassName: "bg-[var(--accent-soft)] text-[var(--accent-ink)]",
        dotClassName: "bg-[var(--accent)]",
        timestampLabel: "Received",
        showLoggedTimestamp: false,
      };
    case "interview_confirmation":
      return {
        label: "Interview",
        title: "Interview confirmed",
        summarySingular: "interview confirmation",
        summaryPlural: "interview confirmations",
        badgeClassName: "bg-[var(--accent-soft)] text-[var(--accent-ink)]",
        dotClassName: "bg-[var(--accent)]",
        timestampLabel: "Received",
        showLoggedTimestamp: false,
      };
    case "rejection":
      return {
        label: "Closed",
        title: "Rejection email received",
        summarySingular: "rejection",
        summaryPlural: "rejections",
        badgeClassName: "bg-[var(--red-soft)] text-[var(--red)]",
        dotClassName: "bg-[var(--red)]",
        timestampLabel: "Received",
        showLoggedTimestamp: false,
      };
    case "offer":
      return {
        label: "Offer",
        title: "Offer email received",
        summarySingular: "offer",
        summaryPlural: "offers",
        badgeClassName: "bg-[var(--green-soft)] text-[var(--green)]",
        dotClassName: "bg-[var(--green)]",
        timestampLabel: "Received",
        showLoggedTimestamp: false,
      };
    case "reminder_completed":
      return {
        label: "Task done",
        title: "Task marked done",
        summarySingular: "completed task",
        summaryPlural: "completed tasks",
        badgeClassName: "bg-[var(--yellow-soft)] text-[var(--yellow)]",
        dotClassName: "bg-[var(--yellow)]",
        timestampLabel: "Marked done",
        showLoggedTimestamp: false,
      };
    case "reminder_dismissed":
      return {
        label: "Task dismissed",
        title: "Task dismissed",
        summarySingular: "dismissed task",
        summaryPlural: "dismissed tasks",
        badgeClassName: "bg-[var(--yellow-soft)] text-[var(--yellow)]",
        dotClassName: "bg-[var(--yellow)]",
        timestampLabel: "Dismissed",
        showLoggedTimestamp: false,
      };
    case "reminder_reopened":
      return {
        label: "Task reopened",
        title: "Task reopened",
        summarySingular: "reopened task",
        summaryPlural: "reopened tasks",
        badgeClassName: "bg-[var(--yellow-soft)] text-[var(--yellow)]",
        dotClassName: "bg-[var(--yellow)]",
        timestampLabel: "Reopened",
        showLoggedTimestamp: false,
      };
    case "gmail_reply_detected":
      return {
        label: "Replied",
        title: "Your Gmail reply was sent",
        summarySingular: "sent reply",
        summaryPlural: "sent replies",
        badgeClassName: "bg-[var(--green-soft)] text-[var(--green)]",
        dotClassName: "bg-[var(--green)]",
        timestampLabel: "Sent",
        showLoggedTimestamp: false,
      };
    case "manual_stage_update":
      return {
        label: "Status",
        title: "You updated the application status",
        summarySingular: "status update",
        summaryPlural: "status updates",
        badgeClassName: "bg-[var(--blue-soft)] text-[var(--blue)]",
        dotClassName: "bg-[var(--blue)]",
        timestampLabel: "Updated",
        showLoggedTimestamp: false,
      };
    case "generic_update":
    default:
      return {
        label: "Update",
        title: "Update received",
        summarySingular: "update",
        summaryPlural: "updates",
        badgeClassName: "bg-[var(--blue-soft)] text-[var(--blue)]",
        dotClassName: "bg-[var(--blue)]",
        timestampLabel: "Received",
        showLoggedTimestamp: false,
      };
  }
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

function summarizeActivityKinds(activities: RecentActivity[]): ActivityKindSummary[] {
  const counts = new Map<
    string,
    {
      count: number;
      singular: string;
      plural: string;
      badgeClassName: string;
      dotClassName: string;
    }
  >();

  for (const activity of activities) {
    const copy = getActivityCopy(activity);
    const key = copy.summarySingular;
    const current = counts.get(key);

    counts.set(key, {
      count: (current?.count ?? 0) + 1,
      singular: copy.summarySingular,
      plural: copy.summaryPlural,
      badgeClassName: copy.badgeClassName,
      dotClassName: copy.dotClassName,
    });
  }

  return Array.from(counts.values()).map(
    ({ count, singular, plural, badgeClassName, dotClassName }) => ({
      text: `${count} ${count === 1 ? singular : plural}`,
      badgeClassName,
      dotClassName,
    }),
  );
}

function summarizeActivityKindLabels(activities: RecentActivity[]) {
  return summarizeActivityKinds(activities).map((kind) => kind.text);
}

function buildRecentSummary(recentActivity: RecentActivity[], groupCount: number) {
  const changeCount = recentActivity.length;
  const changeLabel = changeCount === 1 ? "change" : "changes";
  const companyLabel = groupCount === 1 ? "company" : "companies";
  const kinds = summarizeActivityKindLabels(recentActivity).slice(0, 3);
  const kindSummary =
    kinds.length > 0
      ? `Includes ${summaryFormatter.format(kinds)}.`
      : "New recruiting events will be summarized here.";

  return {
    headline: `${changeCount} ${changeLabel} · ${groupCount} ${companyLabel}`,
    detail: kindSummary,
  };
}

function buildGroupSummary(activities: RecentActivity[]) {
  if (activities.length === 1) {
    return getActivityCopy(activities[0]).title;
  }

  return `${activities.length} updates: ${summaryFormatter.format(
    summarizeActivityKindLabels(activities),
  )}`;
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
      <div className="border-b border-dashed border-[var(--border)] bg-[rgba(255,255,255,0.36)] px-4 py-2">
        <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
          Example
        </span>
        <span className="ml-2 text-[11px] text-[var(--text-tertiary)]">
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
                className={`mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full ring-2 ring-[rgba(255,255,255,0.82)] ${example.dot}`}
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
  const displayedCompanyGroups =
    variant === "full"
      ? companyGroups
      : companyGroups.slice(0, recentChangeDisplayLimit);
  const canOpenFullTimeline =
    variant === "preview" && companyGroups.length > recentChangeDisplayLimit;
  const visibleActivities = companyGroups
    .flatMap((companyGroup) => getCompanyGroupVisibleActivities(companyGroup))
    .sort(compareActivitiesByRecency);
  const latestUpdateAtUtc = visibleActivities[0]?.occurredAtUtc ?? null;
  const recentSummary = buildRecentSummary(visibleActivities, companyGroups.length);
  const summaryPills = summarizeActivityKinds(visibleActivities).slice(0, 4);
  const summaryInline = summarizeActivityKindLabels(visibleActivities)
    .slice(0, 3)
    .join(" · ");

  return (
    <section className="card app-workspace-panel flex h-full min-h-0 flex-col p-0">
      <div className="shrink-0 px-4 pt-4 sm:px-5 sm:pt-5">
        <div className="flex items-center justify-between gap-3">
          <div>
            <p className="eyebrow">Activity log</p>
            <div className="mt-1.5 flex flex-wrap items-baseline gap-x-3 gap-y-1">
              <h2 className="text-base font-semibold text-[var(--text-primary)] sm:text-lg">
                What happened recently
              </h2>
              <p className="text-[11px] text-[var(--text-tertiary)]">
                Last update {formatTimestamp(latestUpdateAtUtc, timeZoneId)}
              </p>
            </div>
          </div>
        </div>

        {visibleActivities.length > 0 ? (
          <div className="mt-3 border-t border-[var(--border)] pt-3 sm:mt-4">
            <div className="grid gap-2 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-start">
              <div className="min-w-0">
                <p className="text-sm font-semibold text-[var(--text-primary)]">
                  {recentSummary.headline}
                </p>
                {summaryInline ? (
                  <p className="mt-1 truncate text-[11px] text-[var(--text-tertiary)] lg:hidden">
                    {summaryInline}
                  </p>
                ) : null}
              </div>
              <div className="hidden flex-nowrap gap-2 overflow-x-auto pb-1 lg:flex lg:justify-end">
                {summaryPills.map((pill) => (
                  <span
                    key={pill.text}
                    className={`badge shrink-0 gap-2 whitespace-nowrap ${pill.badgeClassName}`}
                  >
                    <span
                      className={`h-1.5 w-1.5 rounded-full ${pill.dotClassName}`}
                      aria-hidden="true"
                    />
                    {pill.text}
                  </span>
                ))}
              </div>
            </div>
          </div>
        ) : null}
      </div>

      {visibleActivities.length === 0 ? (
        <div className="px-5 pt-4 pb-5">
          <div className="rounded-md border border-dashed border-[var(--border-hover)] bg-[rgba(255,255,255,0.36)] p-6">
            <p className="text-sm font-medium text-[var(--text-primary)]">
              {coldStart ? "Nothing synced yet" : "No changes recorded yet"}
            </p>
            <p className="mt-1 text-xs leading-6 text-[var(--text-tertiary)]">
              Stage changes, recruiter replies, interviews, assessments, offers,
              and rejections will appear here after sync.
            </p>
          </div>
          {coldStart ? <ExampleChangesPreview /> : null}
        </div>
      ) : (
        <>
          <div className="workspace-scroll-region min-h-0 flex-1 px-4 pt-3 pb-4 sm:px-5 sm:pt-4 sm:pb-5">
            <div className="space-y-2.5 sm:space-y-3">
            {displayedCompanyGroups.map((companyGroup) => {
              const visibleCompanyActivities = getCompanyGroupVisibleActivities(companyGroup);
              const latestCompanyActivity =
                visibleCompanyActivities[0] ??
                companyGroup.roleGroups[0]?.activities[0];

              if (!latestCompanyActivity) {
                return null;
              }

              return (
                <article
                  key={companyGroup.key}
                  className="overflow-hidden rounded-md border border-[var(--border)] bg-white/55"
                >
                  <div className="p-3 sm:p-4">
                    <div className="min-w-0">
                      <p className="break-words text-sm font-semibold text-[var(--text-primary)]">
                        {companyGroup.company}
                      </p>
                      <p className="mt-1 text-[11px] leading-5 text-[var(--text-tertiary)]">
                        {companyGroup.roleGroups.length} role
                        {companyGroup.roleGroups.length === 1 ? "" : "s"} ·{" "}
                        {visibleCompanyActivities.length} update
                        {visibleCompanyActivities.length === 1 ? "" : "s"} · Latest{" "}
                        {formatTimestamp(latestCompanyActivity.occurredAtUtc, timeZoneId)}
                      </p>
                    </div>
                  </div>

                  <div className="border-t border-[var(--border)]">
                    {companyGroup.roleGroups.map((roleGroup) => {
                      const visibleRoleActivities = getVisibleActivities(roleGroup.activities);

                      return (
                        <div
                          key={roleGroup.key}
                          className="border-b border-[var(--border)] last:border-b-0"
                        >
                          <Link
                            href={`/app/applications/${roleGroup.jobApplicationId}`}
                            className="grid gap-2 px-3 py-2.5 transition-colors hover:bg-white/55 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-start lg:px-4 lg:py-3"
                          >
                            <div className="min-w-0">
                              <p className="text-xs font-semibold leading-5 text-[var(--text-primary)]">
                                {roleGroup.role}
                              </p>
                              <p className="text-[11px] leading-5 text-[var(--text-secondary)]">
                                {buildGroupSummary(visibleRoleActivities)}
                              </p>
                            </div>
                            <span className="hidden w-fit rounded-full border border-[var(--border)] bg-white/70 px-2 py-0.5 font-mono text-[0.6rem] font-semibold uppercase tracking-[0.08em] text-[var(--text-secondary)] lg:inline-flex lg:justify-self-end">
                              {visibleRoleActivities.length} update
                              {visibleRoleActivities.length === 1 ? "" : "s"}
                            </span>
                          </Link>

                          <div className="divide-y divide-[var(--border)] border-t border-[var(--border)]">
                            {visibleRoleActivities.map((activity) => {
                              const copy = getActivityCopy(activity);

                              return (
                                <Link
                                  key={activity.id}
                                  href={`/app/applications/${activity.jobApplicationId}`}
                                  className="grid gap-2 px-3 py-2.5 transition-colors hover:bg-white/50 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-start lg:px-4 lg:py-3"
                                >
                                  <div className="flex min-w-0 gap-2.5">
                                    <span
                                      className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ring-2 ring-white/80 ${copy.dotClassName}`}
                                      aria-hidden="true"
                                    />
                                    <div className="min-w-0">
                                      <p className="text-sm font-medium leading-5 text-[var(--text-primary)]">
                                        {copy.title}
                                      </p>
                                      <p className="text-[11px] leading-5 text-[var(--text-tertiary)]">
                                        {copy.timestampLabel}{" "}
                                        {formatTimestamp(
                                          copy.showLoggedTimestamp
                                            ? activity.receivedAtUtc
                                            : activity.occurredAtUtc,
                                          timeZoneId,
                                        )}
                                      </p>
                                    </div>
                                  </div>
                                  <div className="lg:text-right">
                                    <span className={`badge max-lg:hidden ${copy.badgeClassName}`}>
                                      {copy.label}
                                    </span>
                                    {copy.showLoggedTimestamp ? (
                                      <p className="mt-2 text-[11px] text-[var(--text-tertiary)]">
                                        Logged {formatTimestamp(activity.occurredAtUtc, timeZoneId)}
                                      </p>
                                    ) : null}
                                  </div>
                                </Link>
                              );
                            })}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </article>
              );
            })}
            </div>
          </div>

          {canOpenFullTimeline ? (
            <div className="shrink-0 border-t border-[var(--border)] bg-[rgba(255,255,255,0.32)] px-5 py-3 flex justify-end">
              <Link href="/app/updates" className="btn btn-secondary btn-sm w-full sm:w-auto">
                View full timeline
              </Link>
            </div>
          ) : null}
        </>
      )}
    </section>
  );
}
