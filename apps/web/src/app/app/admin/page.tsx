import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { AdminRefreshControl } from "@/components/admin/admin-refresh-control";
import { AdminTableShell } from "@/components/admin/admin-table-shell";
import {
  buildAdminSearchText,
  EmptyTableRow,
  formatUtcTimestamp,
} from "@/components/admin/admin-table-utils";
import { RetryJobButton } from "@/components/admin/retry-job-button";
import {
  getAdminSummaryForUser,
  type AdminFailedEmailJob,
  type AdminModelCallEvent,
  type AdminOperationalCheck,
} from "@/lib/admin-server";
import { getCareerocClientApiBaseUrl } from "@/lib/careeroc-api-server";

export default async function AdminPage() {
  const session = await auth();
  const email = session?.user?.email?.trim();
  if (!email) {
    redirect("/");
  }

  const { summary, status } = await getAdminSummaryForUser({ email, name: session?.user?.name?.trim() || null });
  if (!summary) {
    if (status === 401 || status === 403) {
      redirect("/app");
    }

    return <AdminUnavailable status={status} />;
  }

  return (
    <main className="app-scroll-main">
      <div className="workspace-shell mx-auto flex w-full max-w-[104rem] flex-col px-5 py-6 animate-in">
        <section className="workspace-scroll-region min-h-0 space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <p className="eyebrow">Admin</p>
              <h1 className="mt-1 text-2xl font-semibold">Operations</h1>
            </div>
            <AdminRefreshControl />
          </div>
          <div className="grid gap-3 sm:grid-cols-3">
            <Metric label="Pending jobs" value={summary.queue.pendingAnalysisJobs} />
            <Metric label="Processing jobs" value={summary.queue.processingAnalysisJobs} />
            <Metric label="Failed jobs" value={summary.queue.failedAnalysisJobs} />
          </div>
          <OperationalChecksTable checks={summary.operationalChecks} />
          <FailedJobsTable jobs={summary.recentFailedEmailJobs} proxyPrefix={getCareerocClientApiBaseUrl()} />
          <ModelCallTable events={summary.recentModelCallEvents} />
        </section>
      </div>
    </main>
  );
}

function AdminUnavailable({ status }: { status: number }) {
  return (
    <main className="app-scroll-main">
      <div className="workspace-shell mx-auto w-full max-w-[104rem] px-5 py-6">
        <section className="card app-workspace-panel p-5">
          <p className="eyebrow">Admin</p>
          <h1 className="mt-2 text-xl font-semibold">The operations summary is unavailable</h1>
          <p className="mt-2 text-sm text-[var(--text-secondary)]">
            The API answered with status {status}. Check that it is running and reachable from the web app.
          </p>
        </section>
      </div>
    </main>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div className="card p-4">
      <p className="eyebrow">{label}</p>
      <p className="mt-2 text-3xl font-semibold text-[var(--text-primary)]">{value.toLocaleString()}</p>
    </div>
  );
}

function OperationalChecksTable({ checks }: { checks: AdminOperationalCheck[] }) {
  return (
    <AdminTableShell
      eyebrow="Configuration"
      title="Configuration checks"
      count={checks.length}
      description="Settings that can break specific API paths even while the health check is green."
      searchPlaceholder="Filter checks"
    >
      <table className="w-full min-w-[960px] border-collapse text-left text-sm">
        <thead className="border-b border-[var(--border)] text-xs text-[var(--text-tertiary)]">
          <tr>
            <th className="py-2 pr-4 font-medium">Status</th>
            <th className="py-2 pr-4 font-medium">Area</th>
            <th className="py-2 pr-4 font-medium">Check</th>
            <th className="py-2 pr-4 font-medium">Current value</th>
            <th className="py-2 pr-4 font-medium">Detail</th>
          </tr>
        </thead>
        <tbody>
          {checks.length === 0 ? (
            <EmptyTableRow colSpan={5} message="No checks returned." />
          ) : (
            checks.map((check) => (
              <tr
                key={`${check.area}-${check.name}`}
                data-admin-row
                data-admin-search={buildAdminSearchText([check.status, check.area, check.name, check.currentValue, check.detail])}
                className="border-b border-[var(--border)]/70 last:border-0"
              >
                <td className="py-3 pr-4">
                  <span
                    className={`inline-block rounded-full border px-2.5 py-1 text-xs font-semibold uppercase tracking-wider ${checkStatusClass(check.status)}`}
                  >
                    {check.status}
                  </span>
                </td>
                <td className="py-3 pr-4">{check.area}</td>
                <td className="py-3 pr-4 font-medium text-[var(--text-primary)]">{check.name}</td>
                <td className="py-3 pr-4">
                  <span className="block max-w-72 truncate text-[var(--text-secondary)]">{check.currentValue ?? "n/a"}</span>
                </td>
                <td className="py-3 pr-4">
                  <span className="block max-w-[32rem] text-[var(--text-secondary)]">{check.detail}</span>
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </AdminTableShell>
  );
}

function FailedJobsTable({ jobs, proxyPrefix }: { jobs: AdminFailedEmailJob[]; proxyPrefix: string }) {
  return (
    <AdminTableShell eyebrow="Queue" title="Failed email jobs" count={jobs.length} searchPlaceholder="Filter jobs">
      <table className="w-full min-w-[960px] border-collapse text-left text-sm">
        <thead className="border-b border-[var(--border)] text-xs text-[var(--text-tertiary)]">
          <tr>
            <th className="py-2 pr-4 font-medium">Updated</th>
            <th className="py-2 pr-4 font-medium">Inbox</th>
            <th className="py-2 pr-4 font-medium">Attempts</th>
            <th className="py-2 pr-4 font-medium">Error</th>
            <th className="py-2 pr-4 font-medium">Action</th>
          </tr>
        </thead>
        <tbody>
          {jobs.length === 0 ? (
            <EmptyTableRow colSpan={5} message="No failed email jobs." />
          ) : (
            jobs.map((job) => (
              <tr
                key={job.jobId}
                data-admin-row
                data-admin-search={buildAdminSearchText([job.updatedAtUtc, job.inboxEmailAddress, job.userEmail, job.jobId, job.lastError])}
                className="border-b border-[var(--border)]/70 last:border-0"
              >
                <td className="py-3 pr-4">{formatUtcTimestamp(job.updatedAtUtc)}</td>
                <td className="py-3 pr-4">
                  <p className="font-medium text-[var(--text-primary)]">{job.inboxEmailAddress}</p>
                  <p className="text-xs text-[var(--text-tertiary)]">{job.userEmail}</p>
                </td>
                <td className="py-3 pr-4">{job.attempts}</td>
                <td className="py-3 pr-4">
                  <span className="block max-w-96 truncate text-[var(--text-secondary)]">{job.lastError ?? "n/a"}</span>
                </td>
                <td className="py-3 pr-4">
                  <RetryJobButton jobId={job.jobId} proxyPrefix={proxyPrefix} />
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </AdminTableShell>
  );
}

function ModelCallTable({ events }: { events: AdminModelCallEvent[] }) {
  return (
    <AdminTableShell eyebrow="Models" title="Recent model calls" count={events.length} searchPlaceholder="Filter model calls">
      <table className="w-full min-w-[960px] border-collapse text-left text-sm">
        <thead className="border-b border-[var(--border)] text-xs text-[var(--text-tertiary)]">
          <tr>
            <th className="py-2 pr-4 font-medium">Time</th>
            <th className="py-2 pr-4 font-medium">Purpose</th>
            <th className="py-2 pr-4 font-medium">Model</th>
            <th className="py-2 pr-4 font-medium">Outcome</th>
            <th className="py-2 pr-4 font-medium">Tokens</th>
            <th className="py-2 pr-4 font-medium">Latency</th>
          </tr>
        </thead>
        <tbody>
          {events.length === 0 ? (
            <EmptyTableRow colSpan={6} message="No recent model calls." />
          ) : (
            events.map((event) => (
              <tr
                key={event.id}
                data-admin-row
                data-admin-search={buildAdminSearchText([event.occurredAtUtc, event.purpose, event.model, event.outcome, event.errorType, event.userEmail])}
                className="border-b border-[var(--border)]/70 last:border-0"
              >
                <td className="py-3 pr-4">{formatUtcTimestamp(event.occurredAtUtc)}</td>
                <td className="py-3 pr-4 font-medium text-[var(--text-primary)]">{event.purpose}</td>
                <td className="py-3 pr-4">
                  <span className="block max-w-48 truncate">{event.model}</span>
                  <span className="block text-xs text-[var(--text-tertiary)]">{event.provider}</span>
                </td>
                <td className="py-3 pr-4">
                  {event.outcome}
                  {event.errorType ? <span className="block text-xs text-[var(--text-tertiary)]">{event.errorType}</span> : null}
                </td>
                <td className="py-3 pr-4">{event.totalTokens?.toLocaleString() ?? "n/a"}</td>
                <td className="py-3 pr-4">{formatLatency(event.latencyMs)}</td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </AdminTableShell>
  );
}

function checkStatusClass(status: string) {
  if (status === "critical") {
    return "border-[var(--red)]/30 bg-[var(--red-soft)] text-[var(--red)]";
  }

  if (status === "warning") {
    return "border-[var(--yellow)]/30 bg-[var(--yellow-soft)] text-[var(--yellow)]";
  }

  return "border-[var(--green)]/30 bg-[var(--green-soft)] text-[var(--green)]";
}

function formatLatency(value: number) {
  return value >= 1000 ? `${(value / 1000).toFixed(value >= 10_000 ? 0 : 1)} s` : `${value.toLocaleString()} ms`;
}
