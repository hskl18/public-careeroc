import type { Metadata } from "next";
import Link from "next/link";
import { PublicSiteShell } from "@/components/public-site-chrome";
import { SvgAsset, mascotAssets } from "@/components/svg-asset";
import { buildPageMetadata, repositoryUrl } from "@/lib/site-metadata";

const pageTitle = "How careeroc is built";
const pageDescription =
  "How careeroc is built: a deterministic email pipeline in .NET, a LangGraph multi-agent review with human approval, MCP and A2A access, OpenTelemetry traces, offline evals, and deployments for Compose and Kubernetes.";

export const metadata: Metadata = buildPageMetadata({
  title: pageTitle,
  description: pageDescription,
});

const agentStages = [
  { title: "Pipeline in .NET", body: "Rules first, tested." },
  { title: "Specialist agents", body: "Three in parallel, one supervisor." },
  { title: "Human approval", body: "The graph pauses until you decide." },
  { title: "Traced end to end", body: "OpenTelemetry on every step." },
];

const pipelineStages = [
  ["1. Inbox sync", "Gmail or demo inbox"],
  ["2. Triage", "Recruiting or noise"],
  ["3. Extraction", "Company, role, deadline"],
  ["4. Matching", "Thread, then company and role"],
  ["5. Review gate", "Unsure mail waits for you"],
  ["6. Write", "Event, reminders, audit row"],
  ["7. Agent review", "Specialists, gate, approval"],
];

const reviewGraph = [
  ["resume, mailbox, strategy", "Run in parallel"],
  ["career_supervisor", "Merges findings"],
  ["evidence_gate", "Checks citations"],
  ["human_review", "Waits for you"],
];

const evalMetrics = [
  ["Triage", "46/46"],
  ["Category", "42/42"],
  ["Company", "42/42"],
  ["Application status", "20/20"],
  ["Review routing", "2/2"],
];

const modelRoutes = [
  ["Deterministic suite", "The default. No model needed."],
  ["Model-backed agents", "Ollama or OpenAI, same gate."],
  ["Email model pass", "Optional. Rules stand if it fails."],
  ["Graph failure", "The API falls back to its own review."],
];

const stackRows = [
  ["Next.js 16", "Web, auth, signed proxy"],
  [".NET 10 API", "Pipeline, every write, MCP"],
  ["LangGraph", "Review graph, A2A"],
  ["PostgreSQL 17", "Data, queue, checkpoints"],
];

const interop = [
  ["MCP", "3 read-only tools"],
  ["A2A 1.0", "2 skills"],
  ["Access", "Revocable tokens"],
  ["Deploy", "Compose, Kubernetes"],
];

type PanelProps = {
  title: string;
  id?: string;
  rows: string[][];
  note?: string;
};

function Panel({ title, id, rows, note }: PanelProps) {
  return (
    <section id={id} className="card flex scroll-mt-16 flex-col p-4">
      <h2 className="text-base font-semibold text-[var(--text-primary)]">{title}</h2>
      <div className="mt-1 flex flex-1 flex-col divide-y divide-[var(--border)]">
        {rows.map(([label, value]) => (
          <div key={label} className="flex flex-1 items-center justify-between gap-3 py-1">
            <h3 className="min-w-0 text-sm font-medium text-[var(--text-primary)]">{label}</h3>
            <p className="text-right text-xs text-[var(--text-tertiary)]">{value}</p>
          </div>
        ))}
      </div>
      {note ? <p className="mt-1 text-xs text-[var(--text-tertiary)]">{note}</p> : null}
    </section>
  );
}

export default function TechnologyPage() {
  return (
    <PublicSiteShell>
      <section className="card flex flex-col p-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="flex min-w-0 items-center gap-3 sm:gap-4">
            <SvgAsset
              src={mascotAssets.loadingResumeScan}
              decorative
              className="h-12 w-12"
            />
            <h1 className="min-w-0 text-xl font-semibold text-[var(--text-primary)]">
              Pipeline, agents, human approval
            </h1>
          </div>
          <div className="flex flex-wrap gap-2 lg:shrink-0 lg:justify-end">
            <Link href="/#access" className="btn btn-primary btn-sm">
              Open the demo
            </Link>
            <a href={repositoryUrl} className="btn btn-secondary btn-sm">
              Source
            </a>
          </div>
        </div>

        <div className="mt-2 grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
          {agentStages.map((stage) => (
            <article
              key={stage.title}
              className="rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-1.5"
            >
              <h2 className="text-sm font-semibold text-[var(--text-primary)]">{stage.title}</h2>
              <p className="text-xs text-[var(--text-tertiary)]">{stage.body}</p>
            </article>
          ))}
        </div>
      </section>

      <div className="grid grow gap-3 md:grid-cols-2 xl:grid-cols-3">
        <Panel title="Pipeline" rows={pipelineStages} />
        <Panel title="Review graph" rows={reviewGraph} />
        <Panel id="eval" title="Evals in CI" rows={evalMetrics} note="Regression guard, not a benchmark." />
        <Panel title="Models" rows={modelRoutes} />
        <Panel title="Services" rows={stackRows} />
        <Panel title="Connect and deploy" rows={interop} />
      </div>
    </PublicSiteShell>
  );
}
