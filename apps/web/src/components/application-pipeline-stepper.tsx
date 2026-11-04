const PIPELINE_STAGES = [
  "Applied",
  "Recruiter Contact",
  "Oa Pending",
  "Interviewing",
  "Offer",
] as const;

const STAGE_DISPLAY_LABELS: Record<(typeof PIPELINE_STAGES)[number], string> = {
  Applied: "Applied",
  "Recruiter Contact": "Recruiter Contact",
  "Oa Pending": "OA Pending",
  Interviewing: "Interviewing",
  Offer: "Offer",
};

function isTerminalStatus(status: string) {
  return status === "Rejected" || status === "Closed";
}

export function ApplicationPipelineStepper({
  status,
  completedStages,
}: {
  status: string;
  completedStages: string[];
}) {
  const isRejected = status === "Rejected";
  const terminal = isTerminalStatus(status);
  const currentIndex = terminal
    ? -1
    : PIPELINE_STAGES.findIndex((stage) => stage === status);
  const effectiveIndex = currentIndex < 0 ? 0 : currentIndex;
  const historicalStages = completedStages.length > 0 ? completedStages : ["Applied"];
  const completedStageSet = new Set(historicalStages);
  const historicalIndex = Math.max(
    0,
    ...historicalStages
      .map((stage) =>
        PIPELINE_STAGES.findIndex((pipelineStage) => pipelineStage === stage),
      )
      .filter((index) => index >= 0),
  );
  const progressIndex = terminal
    ? historicalIndex
    : Math.max(effectiveIndex, historicalIndex);
  const progressPercent = Math.round(
    (progressIndex / (PIPELINE_STAGES.length - 1)) * 100,
  );
  const latestHistoricalStage = PIPELINE_STAGES[historicalIndex] ?? "Applied";

  return (
    <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.36)] p-3">
      <div className="flex items-center justify-between gap-3">
        <p className="eyebrow">Pipeline</p>
        <span
          className="badge"
          style={{
            backgroundColor: isRejected
              ? "var(--red)"
              : terminal
                ? "var(--surface-overlay)"
                : "var(--accent-soft)",
            color: isRejected
              ? "#ffffff"
              : terminal
                ? "var(--text-tertiary)"
                : "var(--accent)",
            borderColor: isRejected ? "var(--red-strong)" : undefined,
            boxShadow: isRejected ? "0 8px 18px rgba(225, 45, 57, 0.2)" : undefined,
          }}
        >
          {terminal ? status : status || "Applied"}
        </span>
      </div>

      <div className="relative mt-3 px-[14px]">
        <div className="absolute left-[14px] right-[14px] top-[13px] h-[2px] bg-[var(--surface-overlay)]" />
        {progressIndex > 0 ? (
          <div
            className="absolute left-[14px] top-[13px] h-[2px] transition-all duration-700"
            style={{
              width: `calc((100% - 28px) * ${progressPercent / 100})`,
              backgroundColor: "var(--accent)",
            }}
          />
        ) : null}
        <ol className="relative grid grid-cols-5 gap-2">
          {PIPELINE_STAGES.map((stage, index) => {
            const isCurrent = !terminal && index === effectiveIndex;
            const isComplete = !terminal && index < effectiveIndex;
            const wasReached = completedStageSet.has(stage) || index <= historicalIndex;
            const filled = wasReached || isCurrent || isComplete;
            return (
              <li key={stage} className="flex flex-col items-center text-center">
                <span
                  className="relative z-10 flex h-6 w-6 items-center justify-center rounded-full border text-[10px] font-semibold transition-colors"
                  style={{
                    backgroundColor: filled ? "var(--accent)" : "var(--surface)",
                    borderColor: filled ? "var(--accent)" : "var(--border)",
                    color: filled ? "var(--bg)" : "var(--text-tertiary)",
                  }}
                  aria-current={isCurrent ? "step" : undefined}
                >
                  {filled && !isCurrent ? "✓" : index + 1}
                </span>
                <span
                  className="mt-1.5 text-[11px] leading-tight font-medium"
                  style={{
                    color: isCurrent
                      ? "var(--text-primary)"
                      : wasReached || isComplete
                        ? "var(--text-secondary)"
                        : "var(--text-tertiary)",
                  }}
                >
                  {STAGE_DISPLAY_LABELS[stage]}
                </span>
              </li>
            );
          })}
        </ol>
      </div>

      {terminal ? (
        <p
          className="mt-3 text-xs"
          style={{ color: isRejected ? "var(--red)" : "var(--text-tertiary)" }}
        >
          {isRejected
            ? `Rejected after ${STAGE_DISPLAY_LABELS[latestHistoricalStage]}.`
            : `Closed after ${STAGE_DISPLAY_LABELS[latestHistoricalStage]}.`}
        </p>
      ) : null}
    </div>
  );
}
