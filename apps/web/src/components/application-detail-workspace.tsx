"use client";

import Link from "next/link";
import { useState } from "react";
import { ApplicationPipelineStepper } from "@/components/application-pipeline-stepper";
import { ApplicationDetailSidebar } from "@/components/application-detail-sidebar";
import {
  AgentTraceStepsPanel,
  ApplicationThreadEvidenceSection,
} from "@/components/application-thread-evidence-section";
import { InlineMemoryProposals } from "@/components/inline-memory-proposals";
import { ResumeFitPanel } from "@/components/resume-fit-panel";
import { fetchWithInFlightDedup, readErrorMessage } from "@/lib/client-fetch";
import { toUtcIsoStringFromDateTimeLocal } from "@/lib/date-time";
import type { ApplicationDetail, ApplicationThreadMessage } from "@/lib/application-detail";
import {
  applyNotOaCorrection,
  isAssessmentReminder,
  isOaMessage,
} from "@/lib/application-detail-view";

type ApplicationDetailWorkspaceProps = {
  detail: ApplicationDetail | null;
  source: "api" | "fallback";
  proxyPrefix: string | null;
  timeZoneId: string;
};

export function ApplicationDetailWorkspace({
  detail: initialDetail,
  source,
  proxyPrefix,
  timeZoneId,
}: ApplicationDetailWorkspaceProps) {
  const [detail, setDetail] = useState(initialDetail);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pendingCorrectionId, setPendingCorrectionId] = useState<string | null>(null);

  async function refreshDetailFromApi() {
    if (!proxyPrefix || !detail) {
      return false;
    }

    const response = await fetchWithInFlightDedup(
      `${proxyPrefix}/api/applications/${detail.id}`,
      {
        cache: "no-store",
      },
    );

    if (!response.ok) {
      return false;
    }

    setDetail((await response.json()) as ApplicationDetail);
    return true;
  }

  async function handleMarkNotOa(messageId?: string) {
    if (!proxyPrefix || !detail) {
      setError("API proxy is not configured.");
      return;
    }

    const targetMessageId =
      messageId ??
      detail.threads
        .flatMap((thread) => thread.messages)
        .find((message) => isOaMessage(message))?.id;

    if (!targetMessageId) {
      setError("No OA evidence message is available to correct.");
      return;
    }

    setPendingCorrectionId(targetMessageId);
    setError(null);
    setNotice(null);

    const requestHeaders = {
      "Content-Type": "application/json",
    };

    try {
      const correctionResponse = await fetch(
        `${proxyPrefix}/api/emails/${targetMessageId}/correction`,
        {
          method: "PATCH",
          headers: requestHeaders,
          body: JSON.stringify({
            category: "generic_update",
            actionRequired: false,
            clearDueDate: true,
            notes: "User correction: this email is not an OA assignment.",
          }),
        },
      );

      if (!correctionResponse.ok) {
        throw new Error(await readErrorMessage(correctionResponse));
      }

      const assessmentActions = detail.pendingActions.filter(isAssessmentReminder);
      await Promise.all(
        assessmentActions.map(async (action) => {
          const reminderResponse = await fetch(
            `${proxyPrefix}/api/reminders/${action.id}/status`,
            {
              method: "PATCH",
              headers: requestHeaders,
              body: JSON.stringify({ status: "Dismissed" }),
            },
          );

          if (!reminderResponse.ok) {
            throw new Error(await readErrorMessage(reminderResponse));
          }
        }),
      );

      if (detail.status === "Oa Pending") {
        const statusResponse = await fetch(
          `${proxyPrefix}/api/applications/${detail.id}/status`,
          {
            method: "PATCH",
            headers: requestHeaders,
            body: JSON.stringify({
              status: "Applied",
              priority: detail.priority,
            }),
          },
        );

        if (!statusResponse.ok) {
          throw new Error(await readErrorMessage(statusResponse));
        }
      }

      const refreshed = await refreshDetailFromApi();
      if (!refreshed) {
        setDetail((current) => (current ? applyNotOaCorrection(current, targetMessageId) : current));
      }

      setNotice("Marked this evidence as not an OA and cleared the assessment action.");
    } catch (requestError) {
      // Earlier steps may have saved; show what the API has now.
      void refreshDetailFromApi().catch(() => {});
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to save correction.",
      );
    } finally {
      setPendingCorrectionId(null);
    }
  }

  async function handleApplicationStatusSubmit(
    event: React.FormEvent<HTMLFormElement>,
  ) {
    event.preventDefault();

    if (!proxyPrefix || !detail) {
      setError("API proxy is not configured.");
      return;
    }

    const formData = new FormData(event.currentTarget);
    const status = String(formData.get("status") ?? detail.status);
    const priority = Number(formData.get("priority") ?? detail.priority);

    setPendingCorrectionId("application-status");
    setError(null);
    setNotice(null);

    try {
      const response = await fetch(
        `${proxyPrefix}/api/applications/${detail.id}/status`,
        {
          method: "PATCH",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({ status, priority }),
        },
      );

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      const refreshed = await refreshDetailFromApi();
      if (!refreshed) {
        setDetail((current) =>
          current
            ? {
                ...current,
                status,
                priority,
                lastActivityAtUtc: new Date().toISOString(),
              }
            : current,
        );
      }

      setNotice("Saved application status.");
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to update application.",
      );
    } finally {
      setPendingCorrectionId(null);
    }
  }

  async function handleCorrectEmail(
    event: React.FormEvent<HTMLFormElement>,
    message: ApplicationThreadMessage,
  ) {
    event.preventDefault();

    if (!proxyPrefix || !detail) {
      setError("API proxy is not configured.");
      return;
    }

    const formData = new FormData(event.currentTarget);
    const dueDateValue = String(formData.get("dueDateUtc") ?? "");
    const dueDateUtc = dueDateValue
      ? toUtcIsoStringFromDateTimeLocal(dueDateValue, timeZoneId)
      : undefined;

    if (dueDateValue && !dueDateUtc) {
      setError("Invalid due date.");
      return;
    }

    setPendingCorrectionId(message.id);
    setError(null);
    setNotice(null);

    try {
      const response = await fetch(
        `${proxyPrefix}/api/emails/${message.id}/correction`,
        {
          method: "PATCH",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            category: String(formData.get("category") ?? message.category ?? ""),
            jobApplicationId: String(formData.get("jobApplicationId") ?? detail.id),
            company: String(formData.get("company") ?? detail.company).trim(),
            role: String(formData.get("role") ?? detail.role).trim(),
            actionRequired: formData.get("actionRequired") === "on",
            dueDateUtc,
            clearDueDate: !dueDateValue,
            notes: String(formData.get("notes") ?? "").trim(),
          }),
        },
      );

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      const refreshed = await refreshDetailFromApi();
      if (!refreshed) {
        setDetail((current) => {
          if (!current) return current;

          return {
            ...current,
            threads: current.threads.map((thread) => ({
              ...thread,
              messages: thread.messages.map((threadMessage) =>
                threadMessage.id === message.id
                  ? {
                      ...threadMessage,
                      category: String(
                        formData.get("category") ??
                          threadMessage.category ??
                          "generic_update",
                      ),
                      actionRequired: formData.get("actionRequired") === "on",
                      dueDateUtc: dueDateUtc ?? null,
                      requiresManualReview: false,
                    }
                  : threadMessage,
              ),
            })),
          };
        });
      }

      setNotice(`Saved correction for "${message.subject}".`);
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to save evidence correction.",
      );
    } finally {
      setPendingCorrectionId(null);
    }
  }

  if (!detail) {
    return (
      <main className="app-scroll-main">
        <div className="flex w-full flex-col gap-4 px-3 py-4 sm:px-5 sm:py-6">
          <section className="card shrink-0 p-6 sm:p-8">
            <h1 className="text-xl font-semibold text-[var(--text-primary)]">
              Application not found
            </h1>
            <div className="mt-5 flex flex-wrap gap-2">
              <Link href="/app/applications" className="btn btn-primary">
                Back to applications
              </Link>
            </div>
          </section>
        </div>
      </main>
    );
  }

  const actionsDisabled = source !== "api" || pendingCorrectionId !== null;

  return (
    <main className="app-scroll-main">
      <div className="flex min-h-full w-full flex-col gap-4 px-4 py-5 animate-in sm:px-5 sm:py-6 md:h-full md:overflow-hidden">
        <section className="card shrink-0 p-4">
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <h1 className="min-w-0 text-xl font-semibold text-[var(--text-primary)]">
              {detail.company} / {detail.role}
            </h1>
            <div className="flex flex-wrap items-center gap-2">
              {source === "fallback" ? (
                <span className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]">Offline</span>
              ) : null}
              <Link href="/app/applications" className="btn btn-secondary btn-sm">
                Applications
              </Link>
            </div>
          </div>

          <div className="mt-3">
            <ApplicationPipelineStepper
              status={detail.status}
              completedStages={detail.completedStages ?? []}
            />
          </div>

          {notice ? <div className="alert alert-success mt-4">{notice}</div> : null}
          <p role="status" className="sr-only">{notice ?? ""}</p>
          {error ? (
            <div role="alert" className="alert alert-error mt-4">
              {error}
            </div>
          ) : null}

        </section>

        <section className="grid gap-4 md:min-h-0 md:flex-1 md:items-stretch md:overflow-hidden xl:grid-cols-[22rem_minmax(0,1fr)] 2xl:grid-cols-[24rem_minmax(0,1fr)]">
          <div className="workspace-scroll-region min-h-0 space-y-4">
            <ApplicationDetailSidebar
              detail={detail}
              timeZoneId={timeZoneId}
              disabled={actionsDisabled}
              pendingCorrectionId={pendingCorrectionId}
              onStatusSubmit={handleApplicationStatusSubmit}
              onMarkNotOa={() => void handleMarkNotOa()}
            />
          </div>

          <div className="workspace-scroll-region min-h-0 space-y-4">
            <section className="card p-4">
              <h2 className="text-base font-semibold text-[var(--text-primary)]">
                {detail.agentBrief.nextAction}
              </h2>
              <div className="mt-3">
                <AgentTraceStepsPanel
                  steps={detail.agentTrace?.steps ?? []}
                  title="Evidence summary"
                />
              </div>
            </section>

            <ResumeFitPanel applicationId={detail.id} proxyPrefix={proxyPrefix} />

            <InlineMemoryProposals
              proposals={detail.memoryProposals}
              proxyPrefix={proxyPrefix}
            />

            <ApplicationThreadEvidenceSection
              threads={detail.threads}
              source={source}
              pendingCorrectionId={pendingCorrectionId}
              timeZoneId={timeZoneId}
              applicationId={detail.id}
              applicationCompany={detail.company}
              applicationRole={detail.role}
              onCorrectEmail={handleCorrectEmail}
              onMarkNotOa={(messageId) => void handleMarkNotOa(messageId)}
            />
          </div>
        </section>

      </div>
    </main>
  );
}
