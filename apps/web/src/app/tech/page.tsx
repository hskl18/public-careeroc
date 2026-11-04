import type { Metadata } from "next";
import Link from "next/link";
import { PublicSiteShell } from "@/components/public-site-chrome";
import { buildPageMetadata, repositoryUrl } from "@/lib/site-metadata";

const pageTitle = "How careeroc is built";
const pageDescription =
  "How careeroc is built: a deterministic email pipeline in .NET, a LangGraph multi-agent review with human approval, MCP and A2A access, OpenTelemetry traces, offline evals, and deployments for Compose, Kubernetes, AWS, and Railway.";

export const metadata: Metadata = buildPageMetadata({
  title: pageTitle,
  description: pageDescription,
});

const agentStages = [
  {
    title: "Pipeline in .NET",
    body: "Triage, extraction, matching, and reminders are rules first and covered by 400+ tests. An optional model pass is kept only when it is confident.",
  },
  {
    title: "Specialist agents",
    body: "A LangGraph graph runs resume, mailbox, and strategy specialists in parallel on a bounded snapshot, then a supervisor merges them.",
  },
  {
    title: "Human approval",
    body: "When the evidence is thin, the graph pauses on interrupt() with a Postgres checkpoint and resumes with the user's decision.",
  },
  {
    title: "Traced end to end",
    body: "OpenTelemetry follows one click from the web app through the API into each agent node, with GenAI span attributes.",
  },
];

const pipelineStages = [
  ["01", "Inbox sync", "Gmail history API or the demo inbox; one processing job per message"],
  ["02", "Triage", "Recruiting mail versus inbox noise"],
  ["03", "Extraction", "Company, role, category, deadline, contact"],
  ["04", "Matching", "Thread first, then company and role; the status transition"],
  ["05", "Review gate", "Agencies, likely scams, and weak matches wait for a person"],
  ["06", "Write", "Application event, reminders, and an audit row"],
  ["07", "Agent review", "Specialists, supervisor, evidence gate, and human approval"],
];

const reviewGraph = [
  ["resume, mailbox, strategy analysts", "Run in parallel; each returns findings, actions, evidence references, and confidence"],
  ["career_supervisor", "Merges the specialists with the deterministic baseline"],
  ["evidence_gate", "Checks confidence, evidence, applications, and the resume"],
  ["human_review", "interrupt() until the user approves or rejects in the app"],
];

const stackRows = [
  ["Next.js 16 web", "Public site, workspace, Auth.js, and a same-origin proxy that signs every API call"],
  [".NET 10 API", "Email pipeline, every write, personal access tokens, the MCP server, and demo workspaces"],
  ["LangGraph runtime", "Python review graph, Postgres checkpoints, and the A2A 1.0 endpoint"],
  ["PostgreSQL 17", "Product data, the processing queue, and graph checkpoints"],
];

const modelRoutes = [
  [
    "Deterministic suite",
    "Default",
    "Computes every finding from the snapshot. Free and repeatable, so the demo and CI need no model; traces say deterministic/evidence-v1.",
  ],
  [
    "Model-backed specialists",
    "Ollama or OpenAI",
    "Same graph and gate with a model behind each specialist; every node has a timeout and one retry.",
  ],
  [
    "Email pipeline model pass",
    "Optional",
    "Triage and extraction can ask a fast and a structured model; the rule-based result stands when the model is off, slow, or unsure.",
  ],
  [
    "Graph failure",
    "Fallback",
    "If the runtime fails, the API answers with its own deterministic review and marks it completed_with_fallback.",
  ],
];

const evalMetrics = [
  ["Triage", "46/46", "Recruiting mail kept and inbox noise dropped."],
  ["Category", "42/42", "Receipt, assessment, interview, rejection, offer, and outreach."],
  ["Company", "42/42", "The employer, not the agency or the scheduling tool."],
  ["Application status", "20/20", "Each application ends in the expected stage."],
  ["Review routing", "2/2", "The agency and the scam email go to review, and nothing else does."],
];

const datasetComponents = [
  "20 companies",
  "Interviews and an offer",
  "Rejections",
  "Coding assessments",
  "A recruiting agency",
  "A scam email",
  "Inbox noise",
];

const interop = [
  "MCP server · 3 read-only tools",
  "A2A 1.0 agent · 2 skills",
  "Read-only, revocable tokens",
  "Compose · Kubernetes · AWS · Railway",
];

const cellClass =
  "rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-2.5";

type PanelProps = {
  eyebrow: string;
  title: string;
  id?: string;
  children: React.ReactNode;
};

function Panel({ eyebrow, title, id, children }: PanelProps) {
  return (
    <section id={id} className="card app-workspace-panel scroll-mt-16 p-3 sm:p-4">
      <p className="eyebrow">{eyebrow}</p>
      <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">{title}</h2>
      {children}
    </section>
  );
}

export default function TechnologyPage() {
  return (
    <PublicSiteShell>
      <section className="card app-workspace-panel p-3 sm:p-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <p className="eyebrow">Technical architecture</p>
            <h1 className="mt-1 text-xl font-semibold text-[var(--text-primary)] sm:mt-2 sm:text-2xl">
              Deterministic pipeline, specialist agents, human approval
            </h1>
            <p className="mt-2 max-w-3xl text-base leading-6 text-[var(--text-secondary)]">
              Every write goes through a tested .NET pipeline. A LangGraph graph
              handles the judgment that spans many records, sees only a bounded
              snapshot, and stops for a person when its evidence is thin.
            </p>
            <p className="mt-1 max-w-3xl text-xs leading-5 text-[var(--text-tertiary)]">
              Everything here is in the public repository and runs with one
              docker compose command. DESIGN.md explains each decision and the
              alternative it rejected.
            </p>
          </div>
          <div className="flex flex-wrap gap-2 lg:shrink-0 lg:justify-end">
            <Link href="/#access" className="btn btn-primary btn-sm">
              Open the demo
            </Link>
            <Link href="#eval" className="btn btn-secondary btn-sm">
              View evals
            </Link>
            <a href={repositoryUrl} className="btn btn-secondary btn-sm">
              Source
            </a>
            <a href={`${repositoryUrl}/blob/main/SECURITY.md`} className="btn btn-secondary btn-sm">
              Security policy
            </a>
          </div>
        </div>

        <div className="mt-3 grid gap-2 sm:mt-4 sm:grid-cols-2 xl:grid-cols-4">
          {agentStages.map((stage) => (
            <article key={stage.title} className={cellClass}>
              <h2 className="text-sm font-semibold text-[var(--text-primary)]">
                {stage.title}
              </h2>
              <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">
                {stage.body}
              </p>
            </article>
          ))}
        </div>
      </section>

      <div className="grid gap-3 sm:gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.7fr)]">
        <Panel eyebrow="Seven-stage pipeline" title="What each stage does">
          <p className="mt-1 text-sm text-[var(--text-secondary)]">
            Narrow stages give every output a source, a confidence level, and a
            review fallback.
          </p>
          <div className="mt-3 grid gap-2">
            {pipelineStages.map(([step, title, body]) => (
              <div
                key={step}
                className={`grid gap-1 sm:grid-cols-[1.5rem_7rem_minmax(0,1fr)] sm:items-baseline sm:gap-3 ${cellClass}`}
              >
                <span className="eyebrow">{step}</span>
                <h3 className="text-sm font-semibold text-[var(--text-primary)]">{title}</h3>
                <p className="text-xs text-[var(--text-tertiary)]">{body}</p>
              </div>
            ))}
          </div>
        </Panel>

        <Panel eyebrow="Review graph" title="The agent review, node by node">
          <div className="mt-3 grid gap-2">
            {reviewGraph.map(([label, value]) => (
              <div key={label} className={cellClass}>
                <p className="text-sm font-medium text-[var(--text-primary)]">{label}</p>
                <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">{value}</p>
              </div>
            ))}
          </div>
        </Panel>
      </div>

      <Panel
        id="eval"
        eyebrow="Offline evals"
        title="The real pipeline, scored on the demo inbox in CI"
      >
        <p className="mt-1 max-w-4xl text-sm leading-6 text-[var(--text-secondary)]">
          Every email in the 51-email demo inbox has a hand-written label.
          The first run found three defects: a scam email dropped by triage, a
          coding assessment read as an interview, and an agency named as the
          employer. All are fixed, but the labels were written alongside those
          fixes, so this is a regression guard, not a benchmark.
        </p>
        <div className="mt-3 flex flex-wrap gap-2">
          <span className="badge bg-[var(--green-soft)] text-[var(--green)]">
            Latest run 100%
          </span>
          {datasetComponents.map((component) => (
            <span key={component} className="badge text-[var(--text-secondary)]">
              {component}
            </span>
          ))}
        </div>
        <div className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-5">
          {evalMetrics.map(([label, result, detail]) => (
            <div key={label} className={cellClass}>
              <p className="eyebrow">{label}</p>
              <p className="mt-1 text-lg font-semibold text-[var(--text-primary)]">{result}</p>
              <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">{detail}</p>
            </div>
          ))}
        </div>
      </Panel>

      <Panel eyebrow="Models" title="Models are pluggable, never required">
        <p className="mt-1 max-w-4xl text-sm leading-6 text-[var(--text-secondary)]">
          The graph, the gate, and the review contract stay the same whichever
          suite runs behind them, and every model call has a deterministic path
          to fall back on.
        </p>
        <div className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
          {modelRoutes.map(([task, route, detail]) => (
            <article key={task} className={cellClass}>
              <p className="eyebrow">{route}</p>
              <h3 className="mt-1 text-sm font-semibold text-[var(--text-primary)]">{task}</h3>
              <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">{detail}</p>
            </article>
          ))}
        </div>
      </Panel>

      <div className="grid gap-3 sm:gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.7fr)]">
        <Panel eyebrow="Architecture overview" title="Four services, one owner for every write">
          <div className="mt-3 grid gap-2 sm:grid-cols-2">
            {stackRows.map(([label, value]) => (
              <div key={label} className={cellClass}>
                <h3 className="text-sm font-semibold text-[var(--text-primary)]">{label}</h3>
                <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">{value}</p>
              </div>
            ))}
          </div>
        </Panel>

        <Panel eyebrow="Interop and deploy" title="How agents connect and where it runs">
          <div className="mt-3 grid gap-2">
            {interop.map((item) => (
              <div
                key={item}
                className={`text-sm font-medium text-[var(--text-primary)] ${cellClass}`}
              >
                {item}
              </div>
            ))}
          </div>
        </Panel>
      </div>
    </PublicSiteShell>
  );
}
