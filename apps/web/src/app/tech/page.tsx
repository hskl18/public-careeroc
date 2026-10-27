import type { Metadata } from "next";
import Link from "next/link";
import {
  PublicSiteFooter,
  PublicSiteNav,
  ProductRibbon,
} from "@/components/public-site-chrome";
import {
  PublicSignalIcon,
  type PublicSignalIconName,
} from "@/components/public-signal-icon";
import { SvgAsset } from "@/components/svg-asset";
import { buildPageMetadata, repositoryUrl } from "@/lib/site-metadata";

const pageTitle = "How careeroc is built";
const pageDescription =
  "How careeroc is built: a deterministic email pipeline in .NET, a LangGraph multi-agent review with human approval, MCP and A2A access, OpenTelemetry traces, offline evals, and deployments for Compose, Kubernetes, AWS, and Railway.";

export const metadata: Metadata = buildPageMetadata({
  title: pageTitle,
  description: pageDescription,
});

const agentStages: Array<{
  icon: PublicSignalIconName;
  title: string;
  body: string;
}> = [
  {
    icon: "mail",
    title: "Pipeline in .NET",
    body: "Triage, extraction, matching, and reminders are rules first and covered by 400+ tests. An optional model pass is kept only when it is confident.",
  },
  {
    icon: "bot",
    title: "Specialist agents",
    body: "A LangGraph graph runs resume, mailbox, and strategy specialists in parallel on a bounded snapshot, then a supervisor merges them.",
  },
  {
    icon: "review",
    title: "Human approval",
    body: "When the evidence is thin, the graph pauses on interrupt() with a Postgres checkpoint and resumes with the user's decision.",
  },
  {
    icon: "trace",
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

export default function TechnologyPage() {
  return (
    <main className="public-landing-page flex-1 px-3 py-4 sm:px-5 sm:py-6">
      <div className="mx-auto grid max-w-[104rem] gap-12 pb-12">
        <PublicSiteNav current="technology" />

        <section className="grid gap-8 -mt-6 lg:grid-cols-[minmax(0,0.92fr)_minmax(25rem,0.72fr)] lg:items-start lg:gap-10">
          <div>
            <ProductRibbon label="Open-source architecture" className="mb-5" />
            <p className="eyebrow text-[var(--ink-blue)]">Technical architecture</p>
            <h1 className="mt-4 max-w-3xl text-4xl font-bold leading-[1.08] text-[var(--ink-black)] sm:text-6xl">
              Deterministic pipeline, specialist agents, human approval.
            </h1>
            <p className="mt-5 max-w-2xl text-base font-medium leading-7 text-[var(--ink-black)]/70 sm:text-lg">
              Every write goes through a tested .NET pipeline. A LangGraph graph
              handles the judgment that spans many records, sees only a bounded
              snapshot, and stops for a person when its evidence is thin.
            </p>
            <p className="mt-4 max-w-2xl text-sm font-semibold leading-6 text-[var(--ink-black)]/62">
              Everything here is in the public repository and runs with one
              docker compose command. DESIGN.md explains each decision and the
              alternative it rejected.
            </p>
            <div className="mt-7 flex flex-wrap gap-3">
              <Link href="/#access" className="btn btn-primary">
                Open the demo
              </Link>
              <Link href="#eval" className="btn btn-secondary">
                View evals
              </Link>
              <a href={repositoryUrl} className="btn btn-secondary">
                Source
              </a>
              <a href={`${repositoryUrl}/blob/main/SECURITY.md`} className="btn btn-secondary">
                Security policy
              </a>
            </div>
          </div>

          <section className="public-product-panel rounded-xl border p-5">
            <div className="flex items-center justify-between gap-3 border-b border-[var(--border)] pb-4">
              <div className="flex gap-1.5">
                <span className="h-2.5 w-2.5 rounded-full bg-[var(--green)]" />
                <span className="h-2.5 w-2.5 rounded-full bg-[var(--blue)]" />
                <span className="h-2.5 w-2.5 rounded-full bg-[var(--border-active)]" />
              </div>
              <p className="typewriter-text text-[0.62rem] uppercase tracking-[0.18em] text-[var(--text-tertiary)]">
                Review graph
              </p>
            </div>
            <div className="mt-5 grid gap-3">
              {reviewGraph.map(([label, value]) => (
                <div
                  key={label}
                  className="rounded-lg border border-[var(--border)] bg-[var(--surface-elevated)] px-4 py-3"
                >
                  <p className="text-sm font-bold text-[var(--ink-black)]">{label}</p>
                  <p className="mt-1 text-xs font-medium leading-5 text-[var(--text-secondary)]">
                    {value}
                  </p>
                </div>
              ))}
            </div>
          </section>
        </section>

        <section className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
          {agentStages.map((stage) => (
            <article
              key={stage.title}
              className="rounded-lg border border-[var(--border)] bg-[var(--surface-strong)] p-5"
            >
              <PublicSignalIcon icon={stage.icon} />
              <h2 className="mt-4 text-lg font-bold text-[var(--ink-black)]">
                {stage.title}
              </h2>
              <p className="mt-2 text-sm font-medium leading-6 text-[var(--ink-black)]/66">
                {stage.body}
              </p>
            </article>
          ))}
        </section>

        <section
          id="eval"
          className="rounded-xl border border-[var(--border-active)] bg-[var(--surface-strong)] p-5 sm:p-8"
        >
          <div className="grid gap-6 lg:grid-cols-[minmax(0,0.82fr)_minmax(20rem,0.56fr)] lg:items-start">
            <div>
              <p className="eyebrow text-[var(--ink-blue)]">Offline evals</p>
              <h2 className="mt-3 text-3xl font-bold leading-tight text-[var(--ink-black)] sm:text-4xl">
                The real pipeline, scored on the demo inbox in CI.
              </h2>
              <p className="mt-3 text-sm font-medium leading-6 text-[var(--ink-black)]/68 sm:text-base">
                Every email in the 51-email demo inbox has a hand-written label.
                The first run found three defects: a scam email dropped by
                triage, a coding assessment read as an interview, and an agency
                named as the employer. All are fixed, but the labels were
                written alongside those fixes, so this is a regression guard,
                not a benchmark.
              </p>
              <div className="mt-5 flex flex-wrap gap-2">
                {datasetComponents.map((component) => (
                  <span
                    key={component}
                    className="rounded-full border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-2 text-xs font-bold text-[var(--text-secondary)]"
                  >
                    {component}
                  </span>
                ))}
              </div>
            </div>

            <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-elevated)] p-4">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="typewriter-text text-[0.66rem] uppercase tracking-[0.16em] text-[var(--ink-blue)]">
                    Latest run
                  </p>
                  <strong className="mt-1 block text-3xl text-[var(--ink-black)]">
                    All metrics
                  </strong>
                </div>
                <span className="badge bg-[var(--green-soft)] text-[var(--green)]">
                  100%
                </span>
              </div>
              <div className="mt-5 grid gap-3">
                {evalMetrics.map(([label, result, detail]) => (
                  <div key={label}>
                    <div className="flex items-center justify-between gap-3 text-sm">
                      <span className="font-bold text-[var(--ink-black)]">{label}</span>
                      <span className="font-black text-[var(--ink-blue)]">{result}</span>
                    </div>
                    <div
                      className="mt-2 h-2 overflow-hidden rounded-full bg-[var(--blue-soft)]"
                      aria-hidden="true"
                    >
                      <div className="h-full w-full rounded-full bg-[var(--brand-blue)]" />
                    </div>
                    <p className="mt-1 text-xs font-medium leading-5 text-[var(--text-tertiary)]">
                      {detail}
                    </p>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </section>

        <section className="rounded-xl border border-[var(--border)] bg-[var(--surface-strong)] p-5 sm:p-8">
          <div className="max-w-3xl">
            <p className="eyebrow text-[var(--ink-blue)]">Models</p>
            <h2 className="mt-3 text-3xl font-bold leading-tight text-[var(--ink-black)] sm:text-4xl">
              Models are pluggable, never required.
            </h2>
            <p className="mt-3 text-sm font-medium leading-6 text-[var(--ink-black)]/68 sm:text-base">
              The graph, the gate, and the review contract stay the same
              whichever suite runs behind them, and every model call has a
              deterministic path to fall back on.
            </p>
          </div>
          <div className="mt-7 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            {modelRoutes.map(([task, route, detail]) => (
              <article
                key={task}
                className="rounded-lg border border-[var(--border)] bg-[var(--surface-elevated)] p-4"
              >
                <p className="typewriter-text text-[0.64rem] uppercase tracking-[0.16em] text-[var(--ink-blue)]">
                  {route}
                </p>
                <h3 className="mt-3 text-base font-bold text-[var(--ink-black)]">
                  {task}
                </h3>
                <p className="mt-2 text-sm font-medium leading-6 text-[var(--ink-black)]/64">
                  {detail}
                </p>
              </article>
            ))}
          </div>
        </section>

        <section className="rounded-xl border border-[var(--border)] bg-[var(--surface-strong)] p-5 sm:p-8">
          <div className="flex flex-wrap items-start justify-between gap-5">
            <div className="max-w-2xl">
              <p className="eyebrow text-[var(--ink-blue)]">Seven-stage pipeline</p>
              <h2 className="mt-3 text-3xl font-bold leading-tight text-[var(--ink-black)] sm:text-4xl">
                What each stage does.
              </h2>
              <p className="mt-3 text-sm font-medium leading-6 text-[var(--ink-black)]/68 sm:text-base">
                Narrow stages give every output a source, a confidence level,
                and a review fallback.
              </p>
            </div>
            <SvgAsset
              src="/agents/agent-career-orchestrator.svg"
              label="Career orchestrator"
              className="!h-20 !w-20"
            />
          </div>

          <div className="mt-8 grid gap-3">
            {pipelineStages.map(([step, title, body]) => (
              <div
                key={step}
                className="grid gap-3 rounded-lg border border-[var(--border)] bg-[var(--surface-elevated)] p-4 sm:grid-cols-[4rem_minmax(10rem,0.45fr)_1fr] sm:items-center"
              >
                <span className="typewriter-text text-sm font-bold text-[var(--brand-blue)]">
                  {step}
                </span>
                <h3 className="text-base font-bold text-[var(--ink-black)]">
                  {title}
                </h3>
                <p className="text-sm font-medium leading-6 text-[var(--ink-black)]/64">
                  {body}
                </p>
              </div>
            ))}
          </div>
        </section>

        <section className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.55fr)]">
          <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-strong)] p-5 sm:p-8">
            <p className="eyebrow text-[var(--ink-blue)]">Architecture overview</p>
            <h2 className="mt-3 text-3xl font-bold leading-tight text-[var(--ink-black)] sm:text-4xl">
              Four services, one owner for every write.
            </h2>
            <div className="mt-6 grid gap-3 sm:grid-cols-2">
              {stackRows.map(([label, value]) => (
                <div
                  key={label}
                  className="rounded-lg border border-[var(--border)] bg-[var(--surface-elevated)] p-4"
                >
                  <h3 className="text-base font-bold text-[var(--ink-black)]">
                    {label}
                  </h3>
                  <p className="mt-2 text-sm font-medium leading-6 text-[var(--ink-black)]/66">
                    {value}
                  </p>
                </div>
              ))}
            </div>
          </div>

          <div className="rounded-xl border border-[var(--border-active)] bg-[var(--surface-strong)] p-5 sm:p-6">
            <p className="eyebrow text-[var(--ink-blue)]">Interop and deploy</p>
            <div className="mt-4 grid gap-2">
              {interop.map(
                (item) => (
                  <div
                    key={item}
                    className="rounded-full border border-[var(--border)] bg-[var(--surface-elevated)] px-4 py-3 text-sm font-bold text-[var(--ink-black)]"
                  >
                    {item}
                  </div>
                ),
              )}
            </div>
          </div>
        </section>

        <PublicSiteFooter />
      </div>
    </main>
  );
}
