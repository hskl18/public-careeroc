import type { Metadata } from "next";
import type { Route } from "next";
import Link from "next/link";
import {
  getOptionalSession,
  isAuthEnabled,
  isDemoMode,
  isGoogleAuthConfigured,
  signInLabel,
  startSignIn,
} from "@/auth";
import { GoogleSignInSubmit } from "@/components/google-sign-in-submit";
import {
  PublicSiteFooter,
  PublicSiteNav,
  ProductRibbon,
} from "@/components/public-site-chrome";
import { SvgAsset } from "@/components/svg-asset";
import { buildPageMetadata, repositoryUrl } from "@/lib/site-metadata";

export const metadata: Metadata = buildPageMetadata({
  title: "Multi-agent job search for recruiting email",
});

type HomePageProps = {
  searchParams?: Promise<Record<string, string | string[] | undefined>>;
};

function readSearchParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function normalizeNextPath(value: string | undefined) {
  if (!value || !value.startsWith("/") || value.startsWith("//")) {
    return "/app";
  }

  return value;
}

function formatAuthError(value: string | undefined) {
  switch (value) {
    case "CredentialsSignin":
      return "The demo could not open a workspace right now. Try again in a minute.";
    case "AccessDenied":
      return "This Google account is not on this deployment's access allowlist.";
    case "OAuthAccountNotLinked":
      return "This email is already linked to a different sign-in method.";
    case "OAuthCallback":
    case "OAuthSignin":
      return "Google sign-in could not complete. Check the OAuth callback URL and try again.";
    case "Configuration":
      return "Google sign-in is not configured correctly for this deployment.";
    default:
      return value
        ? `Google sign-in failed with ${value}. Try again or check the OAuth configuration.`
        : null;
  }
}

function formatSessionRecovery(value: string | undefined) {
  if (value !== "recovered") {
    return null;
  }

  return "Your session expired. Sign in again to return to your workspace.";
}

const productPillars = [
  "Recruiting threads",
  "Application records",
  "Review gates",
  "Model trace",
];

const architectureBadges = [
  "LangGraph specialist agents",
  "Human-in-the-loop review",
  "MCP and A2A access",
  "OpenTelemetry traces",
];

const dashboardPreviewMetrics = [
  ["Needs action", "3", "text-[var(--ink-blue)]"],
  ["Needs review", "2", "text-[var(--home-live-text)]"],
  ["Interviews", "4", "text-[var(--ink-blue)]"],
  ["Offers", "1", "text-[var(--green)]"],
];

const dashboardPreviewItems = [
  {
    badge: "Interview",
    badgeClass: "bg-[var(--accent-soft)] text-[var(--accent-ink)]",
    title: "Confirm Northstar Labs interview",
    company: "Northstar Labs",
    role: "Software Engineer Intern",
    detail: "Due Tuesday May 12 · 10:30 AM PT",
    action: "Open detail",
    time: "2m",
  },
  {
    badge: "OA",
    badgeClass: "bg-[var(--violet-soft)] text-[var(--violet)]",
    title: "Atlas Robotics assessment deadline",
    company: "Atlas Robotics",
    role: "Backend Engineer",
    detail: "Due Friday · reminder created",
    action: "Open OA email",
    time: "1h",
  },
  {
    badge: "Review",
    badgeClass: "bg-[var(--red-soft)] text-[var(--red)]",
    title: "Weak company match blocked",
    company: "Unknown Company",
    role: "Product Engineering",
    detail: "Evidence paused for manual review",
    action: "Review",
    time: "3h",
  },
];

const pipelinePreview = [
  ["N", "Northstar Labs", "Software Engineer Intern", "Interview", "green"],
  ["A", "Atlas Robotics", "Backend Engineer", "OA due", "amber"],
  ["L", "Linear Systems", "Product Infrastructure", "Applied", "blue"],
  ["?", "Unknown Company", "Product Engineering", "Review", "red"],
];

const howSteps = [
  [
    "1",
    "Connect Gmail",
    "Google sign-in and Gmail sync are separate, so the workspace can be created before mail access is granted.",
  ],
  [
    "2",
    "Extract evidence",
    "Recruiting threads become companies, roles, deadlines, contacts, and source-backed activity.",
  ],
  [
    "3",
    "Review risky changes",
    "Low confidence updates stop in a review queue with model path, confidence, evidence, and fallback.",
  ],
  [
    "4",
    "Work the pipeline",
    "Applications, reminders, resume feedback, and recent changes stay in one operating surface.",
  ],
];

const featureCards = [
  [
    "Application pipeline",
    "Every company, role, stage, next action, and source thread in one place.",
    "blue",
    "/features/feature-applications.svg",
  ],
  [
    "Inbox triage",
    "Agents separate recruiting mail from noise before any workflow extraction runs.",
    "navy",
    "/agents/agent-inbox-triage.svg",
  ],
  [
    "Review gates",
    "Uncertain extraction never silently mutates high-stakes career state.",
    "amber",
    "/agents/agent-review-evidence.svg",
  ],
  [
    "Resume intelligence",
    "Resume feedback can use real mailbox and application outcomes instead of generic advice.",
    "green",
    "/features/feature-resume.svg",
  ],
  [
    "Artifact evidence",
    "Offer PDFs and interview screenshots become reviewable evidence cards without storing raw bytes.",
    "blue",
    "/features/animated/feature-review-scan.svg",
  ],
  [
    "Pipeline analytics",
    "See reply rates, active stages, stalled applications, and where the search is moving.",
    "navy",
    "/features/feature-analytics.svg",
  ],
];

const gmailScopeRows = [
  ["Google sign-in", "No Gmail permission", "green"],
  ["Gmail sync", "User-initiated readonly access", "green"],
  ["Recruiting/job-search processing", "Targeted queries and filters", "green"],
  ["Personal/financial mail", "Not intentionally processed", "muted"],
  ["Send, delete, or label email", "Not permitted", "muted"],
];

function LiveExtractionPanel() {
  return (
    <section className="home-live-panel flex min-h-0 flex-col rounded-xl border p-0 shadow-[0_18px_45px_rgba(15,23,42,0.12)]">
      <div className="shrink-0 px-5 pt-5">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="typewriter-text text-[0.66rem] uppercase tracking-[0.18em] text-[var(--home-live-faint)]">
              Live extractions · last hour
            </p>
            <h2 className="mt-2 text-lg font-semibold text-[var(--home-live-text)]">
              Recruiting command center
            </h2>
            <p className="mt-1 text-xs leading-5 text-[var(--home-live-muted)]">
              Evidence-backed application updates from the connected inbox.
            </p>
          </div>
          <span className="rounded-full bg-[rgba(94,224,160,0.16)] px-2.5 py-1 text-xs font-bold text-[#5ee0a0]">
            Live
          </span>
        </div>

        <div className="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-4">
          {dashboardPreviewMetrics.map(([label, value, colorClass]) => (
            <div
              key={label}
              className="home-live-card rounded-md border px-3 py-2.5"
            >
              <p className="font-mono text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-[var(--home-live-faint)]">
                {label}
              </p>
              <p className={`mt-1 text-lg font-semibold tabular-nums ${colorClass}`}>
                {value}
              </p>
            </div>
          ))}
        </div>
      </div>

      <div className="min-h-0 flex-1 px-5 pt-4 pb-5">
        <div className="mb-3 flex items-center justify-between gap-3">
          <p className="typewriter-text text-[0.66rem] uppercase tracking-[0.18em] text-[var(--home-live-faint)]">
            Needs attention
          </p>
          <span className="rounded-full bg-[rgba(143,180,255,0.12)] px-2.5 py-1 text-xs font-bold text-[var(--home-live-muted)]">
            {dashboardPreviewItems.length}
          </span>
        </div>

        <div className="space-y-3">
          {dashboardPreviewItems.map((item) => (
            <article
              key={item.title}
              className="home-live-card rounded-lg border px-3 py-2.5"
            >
              <div className="flex flex-wrap items-center gap-1.5">
                <span className={`badge ${item.badgeClass}`}>
                  {item.badge}
                </span>
                <span className="ml-auto text-[11px] font-medium text-[var(--home-live-faint)]">
                  {item.time}
                </span>
              </div>

              <p className="mt-2 text-sm font-semibold leading-snug text-[var(--home-live-text)]">
                {item.title}
              </p>
              <p className="mt-0.5 text-xs text-[var(--home-live-muted)]">
                <span className="font-medium text-[var(--home-live-text)]">
                  {item.company}
                </span>
                {" · "}
                {item.role}
              </p>
              <div className="mt-2.5 flex flex-wrap items-center gap-2">
                <span className="text-[11px] font-medium text-[var(--home-live-faint)]">
                  {item.detail}
                </span>
                <span className="home-live-action ml-auto rounded-md border px-2 py-1 text-[11px] font-medium">
                  {item.action}
                </span>
              </div>
            </article>
          ))}
        </div>

        <div className="home-live-sync mt-3 rounded-md border border-dashed p-3">
          <div className="flex items-center gap-3">
            <SvgAsset
              src="/mascots/loading/loading-dots-mini.svg"
              label="Inbox sync activity"
              className="!h-6 !w-14 opacity-75"
            />
            <p className="text-xs font-medium leading-5 text-[var(--home-live-muted)]">
              Watching 34 recruiting threads · 0 sync errors
            </p>
          </div>
        </div>
      </div>
    </section>
  );
}

function InboxToPipelineLoop() {
  return (
    <div className="home-signal-card mt-6 overflow-hidden rounded-xl border border-[var(--border)] bg-[var(--surface-strong)] p-3">
      <div className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)_auto_minmax(0,1fr)] sm:items-center">
        <MiniWorkflowCard
          eyebrow="Email lands"
          title="Recruiter: interview invite"
          detail="Tuesday · 10:30 AM"
          asset="/agents/agent-inbox-triage.svg"
        />
        <WorkflowArrow />
        <MiniWorkflowCard
          eyebrow="5 seconds later"
          title="Northstar Labs"
          detail="Interview · evidence attached"
          asset="/features/feature-applications.svg"
        />
        <WorkflowArrow />
        <MiniWorkflowCard
          eyebrow="Next action"
          title="Confirm availability"
          detail="Review gate passed"
          asset="/agents/agent-review-evidence.svg"
        />
      </div>
    </div>
  );
}

function MiniWorkflowCard({
  eyebrow,
  title,
  detail,
  asset,
}: {
  eyebrow: string;
  title: string;
  detail: string;
  asset: string;
}) {
  return (
    <div className="min-w-0 rounded-lg border border-[var(--border)] bg-[var(--surface-elevated)] p-3">
      <div className="flex items-center gap-2">
        <SvgAsset
          src={asset}
          label=""
          className="!h-8 !w-8 rounded-md border border-[var(--brand-blue)]/14 bg-[var(--blue-soft)] p-1"
        />
        <div className="min-w-0">
          <p className="typewriter-text truncate text-[0.58rem] uppercase tracking-[0.14em] text-[var(--ink-blue)]">
            {eyebrow}
          </p>
          <p className="truncate text-sm font-bold text-[var(--ink-black)]">
            {title}
          </p>
        </div>
      </div>
      <p className="mt-2 truncate text-xs font-medium text-[var(--ink-black)]/62">
        {detail}
      </p>
    </div>
  );
}

function WorkflowArrow() {
  return (
    <div className="hidden h-px w-8 bg-[var(--border-active)] sm:block" aria-hidden="true" />
  );
}

function PipelineProofPanel() {
  return (
    <section className="public-product-panel rounded-xl border p-0">
      <div className="flex flex-col gap-3 border-b border-[var(--border)] px-5 pt-4 pb-4 md:flex-row md:items-end md:justify-between sm:px-6 sm:pt-5">
        <div className="flex min-w-0 gap-4">
          <SvgAsset
            src="/features/animated/feature-inbox-sync.svg"
            label="Inbox sync proof"
            className="hidden !h-14 !w-14 rounded-lg border border-[var(--border)] bg-[var(--surface-elevated)] p-2 sm:inline-block"
          />
          <div className="min-w-0">
            <p className="typewriter-text text-[0.68rem] uppercase tracking-[0.18em] text-[var(--ink-blue)]">
              Application pipeline
            </p>
            <h2 className="mt-2 max-w-2xl text-2xl font-bold leading-tight text-[var(--ink-black)] sm:text-3xl">
              One workspace. Every application, evidence card, and next action.
            </h2>
          </div>
        </div>
        <span className="badge w-fit bg-[var(--green-soft)] text-[var(--green)]">
          Live · 2m
        </span>
      </div>

      <div className="grid gap-4 px-5 py-4 lg:grid-cols-[10.5rem_minmax(0,1fr)] sm:px-6 sm:py-5">
        <div className="grid content-start gap-2 border-[var(--border)] lg:border-r lg:pr-4">
          {["Dashboard", "Applications", "Review", "Resume"].map((item, index) => (
            <div
              key={item}
              className={`flex min-h-10 items-center rounded-md px-3 py-2 text-sm font-semibold ${
                index === 0
                  ? "bg-[var(--brand-blue)] text-white"
                  : "bg-[var(--surface-elevated)] text-[var(--text-secondary)]"
              }`}
            >
              {item}
            </div>
          ))}
        </div>

        <div className="grid gap-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="typewriter-text text-[0.68rem] uppercase tracking-[0.18em] text-[var(--text-tertiary)]">
              Active pipeline · 12 applications
            </p>
            <div className="flex gap-2">
              <span className="rounded-md bg-[var(--surface-elevated)] px-2 py-1 text-[0.66rem] font-bold text-[var(--text-tertiary)]">
                Filter
              </span>
              <span className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]">
                3 need review
              </span>
            </div>
          </div>
          {pipelinePreview.map(([initial, company, role, status, tone]) => (
            <div
              key={company}
              className="grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-2.5"
            >
              <span className="grid h-8 w-8 place-items-center rounded bg-[var(--brand-blue)] text-xs font-black text-white">
                {initial}
              </span>
              <div className="min-w-0">
                <p className="truncate text-sm font-bold text-[var(--ink-black)]">{company}</p>
                <p className="truncate text-xs font-medium text-[var(--text-tertiary)]">{role}</p>
              </div>
              <span
                className={`rounded-full px-2 py-1 text-[0.66rem] font-bold ${
                  tone === "green"
                    ? "bg-[var(--green)]/16 text-[var(--green)]"
                    : tone === "amber"
                      ? "bg-[var(--yellow-soft)] text-[var(--yellow)]"
                      : tone === "red"
                        ? "bg-[var(--red-soft)] text-[var(--red)]"
                        : "bg-[var(--blue-soft)] text-[var(--blue)]"
                }`}
              >
                {status}
              </span>
            </div>
          ))}
          <p className="border-t border-[var(--border)] pt-3 text-xs font-medium text-[var(--text-tertiary)]">
            Evidence extracted from recruiting email, review decisions, and uploaded artifacts.
          </p>
        </div>
      </div>
    </section>
  );
}

export default async function HomePage({ searchParams }: HomePageProps) {
  const session = await getOptionalSession();
  const isSignedIn = Boolean(session?.user);
  const resolvedSearchParams = (await searchParams) ?? {};
  const nextPath = normalizeNextPath(readSearchParam(resolvedSearchParams.next));
  const authError = readSearchParam(resolvedSearchParams.error);
  const authErrorMessage = formatAuthError(authError);
  const sessionRecoveryMessage = formatSessionRecovery(
    readSearchParam(resolvedSearchParams.session),
  );

  return (
    <main className="public-landing-page home-public-page flex-1 px-3 py-4 sm:px-5 sm:py-6">
      <div className="mx-auto grid max-w-[104rem] gap-12 pb-12">
        <PublicSiteNav current="product" />

        <section className="grid gap-8 -mt-6 lg:grid-cols-[minmax(0,0.92fr)_minmax(25rem,0.72fr)] lg:items-center lg:gap-10">
          <div className="flex min-h-0 flex-col">
            <ProductRibbon className="mb-5" label="Open-source agent system" />
            <h1
              className="max-w-4xl text-4xl font-bold leading-[1.12] text-[var(--ink-black)] sm:text-5xl sm:leading-[1.08] xl:text-6xl"
              style={{ fontFamily: "var(--font-crimson-pro)" }}
            >
              Multi-agent job search for{" "}
              <span className="italic text-[var(--ink-blue)]">
                recruiting email.
              </span>
            </h1>
            <p className="mt-5 max-w-3xl text-base leading-7 text-[var(--ink-black)]/78 sm:text-lg sm:leading-8">
              careeroc turns recruiting threads into an evidence-backed
              pipeline of companies, roles, deadlines, and next actions.
              Specialist agents review the search, you approve what they
              propose, and your own assistant can read it over MCP or A2A.
            </p>
            <div
              className="mt-5 flex flex-wrap gap-2"
              aria-label="Architecture highlights"
            >
              {architectureBadges.map((badge) => (
                <span
                  key={badge}
                  className="rounded-full border border-[var(--brand-blue)]/18 bg-[var(--blue-soft)] px-3 py-2 text-xs font-bold text-[var(--ink-blue)]"
                >
                  {badge}
                </span>
              ))}
            </div>
            <InboxToPipelineLoop />

            <div id="access" className="mt-6 grid gap-3">
              {isSignedIn ? (
                <Link
                  href={nextPath as Route}
                  className="btn btn-primary w-full justify-center sm:w-fit sm:px-6"
                >
                  Open workspace
                </Link>
              ) : isAuthEnabled ? (
                <form
                  action={async () => {
                    "use server";
                    await startSignIn(nextPath);
                  }}
                  className="w-full sm:max-w-sm"
                >
                  <GoogleSignInSubmit demo={isDemoMode} />
                </form>
              ) : (
                <div className="typewriter-text w-full max-w-md border-l-8 border-[var(--green)] bg-[var(--yellow-soft)] p-4 text-sm text-[var(--ink-black)]">
                  Google login is not configured yet. Set `AUTH_GOOGLE_ID`
                  and `AUTH_GOOGLE_SECRET`
                  {isGoogleAuthConfigured ? "" : " for the web app"}, then
                  refresh this page.
                </div>
              )}

              <div className="flex flex-wrap items-center gap-3">
                <Link href="/tech" className="btn btn-secondary justify-center px-4 text-xs">
                  Architecture
                </Link>
                <a href={repositoryUrl} className="btn btn-secondary justify-center px-4 text-xs">
                  Source on GitHub
                </a>
              </div>
            </div>

            <p className="mt-4 flex items-center gap-2 text-xs font-semibold leading-5 text-[var(--ink-black)]/58">
              <span className="grid h-4 w-4 place-items-center rounded-full border border-[var(--green)] text-[0.55rem] text-[var(--green)]">
                ✓
              </span>
              {isDemoMode
                ? "No sign-up. You get a private workspace built from a fictional inbox, deleted after 24 hours."
                : "Google sign-in and Gmail access are separate. Gmail sync is controlled inside the app."}
            </p>

            {authErrorMessage && (
              <div className="typewriter-text mt-5 border-l-8 border-[var(--accent)] bg-[var(--accent)]/10 p-4 text-sm text-[var(--accent)]">
                {authErrorMessage}
              </div>
            )}

            {sessionRecoveryMessage && (
              <div className="typewriter-text mt-5 border-l-8 border-[var(--green)] bg-[var(--green-soft)] p-4 text-sm text-[var(--green)]">
                {sessionRecoveryMessage}
              </div>
            )}

            <div className="mt-6 flex flex-wrap gap-2">
              {productPillars.map((item) => (
                <div
                  key={item}
                  className="rounded-full border border-[var(--ink-black)]/12 bg-[var(--surface-strong)] px-3 py-2 text-sm font-medium text-[var(--ink-black)]/80"
                >
                  {item}
                </div>
              ))}
            </div>
          </div>

          <LiveExtractionPanel />
        </section>

        <PipelineProofPanel />

        <section className="paper-card border border-[var(--border)] p-5 sm:p-8">
          <div className="max-w-2xl">
            <p className="eyebrow text-[var(--ink-blue)]">How it works</p>
            <h2 className="mt-3 text-3xl font-bold leading-tight text-[var(--ink-black)] sm:text-4xl">
              From inbox to pipeline in four steps.
            </h2>
          </div>
          <div className="relative mt-8 grid gap-5 md:grid-cols-2 lg:grid-cols-4">
            {howSteps.map(([step, title, body], index) => (
              <article key={step} className="home-tint-card rounded-xl border border-[var(--border)] p-5">
                <div
                  className={`grid h-10 w-10 place-items-center rounded-full border-2 text-sm font-black ${
                    index === 1
                      ? "border-[var(--brand-blue)] bg-[var(--brand-blue)] text-white"
                      : "border-[var(--border-active)] bg-[var(--paper)] text-[var(--ink-black)]"
                  }`}
                >
                  {step}
                </div>
                <h3 className="mt-4 text-lg font-bold text-[var(--ink-black)]">
                  {title}
                </h3>
                <p className="mt-2 text-sm font-medium leading-6 text-[var(--ink-black)]/68">
                  {body}
                </p>
              </article>
            ))}
          </div>
        </section>

        <section className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(24rem,0.72fr)]">
          <div className="paper-card border border-[var(--border)] p-5 sm:p-8">
            <p className="eyebrow text-[var(--ink-blue)]">What is inside</p>
            <h2 className="mt-3 max-w-2xl text-3xl font-bold leading-tight text-[var(--ink-black)] sm:text-4xl">
              Every tool a serious job search needs.
            </h2>
            <div className="mt-6 grid gap-3 md:grid-cols-2">
              {featureCards.map(([title, body, tone, asset]) => (
                <article key={title} className="home-tint-card rounded-xl border border-[var(--border)] p-4">
                  <SvgAsset
                    src={asset}
                    label={`${title} illustration`}
                    className={`mb-4 !h-12 !w-12 rounded-lg border p-1.5 ${
                      tone === "green"
                        ? "border-[var(--green)]/18 bg-[var(--green-soft)]"
                        : tone === "amber"
                          ? "border-[var(--yellow)]/18 bg-[var(--yellow-soft)]"
                          : tone === "navy"
                            ? "border-[var(--ink-black)]/14 bg-[var(--surface-overlay)]"
                            : "border-[var(--brand-blue)]/18 bg-[var(--blue-soft)]"
                    }`}
                  />
                  <h3 className="text-base font-bold text-[var(--ink-black)]">
                    {title}
                  </h3>
                  <p className="mt-2 text-sm font-medium leading-6 text-[var(--ink-black)]/66">
                    {body}
                  </p>
                </article>
              ))}
            </div>
          </div>

          <div className="paper-card border border-[var(--border-active)] p-5 sm:p-6">
            <p className="eyebrow text-[var(--ink-blue)]">Privacy and Gmail access</p>
            <h2 className="mt-3 text-2xl font-bold leading-tight text-[var(--ink-black)]">
              User-initiated Gmail sync, scoped product processing.
            </h2>
            <p className="mt-3 text-sm font-medium leading-6 text-[var(--ink-black)]/70">
              Google sign-in does not grant Gmail access. When you connect Gmail,
              careeroc uses readonly access, queries recruiting and
              job-search mail, and lets you disconnect, delete, or purge synced
              evidence.
            </p>

            <div className="mt-5 overflow-hidden rounded-xl border border-[var(--border)]">
              <div className="bg-[var(--ink-black)] px-4 py-3 text-sm font-bold text-[var(--paper)]">
                Gmail sync boundary
              </div>
              <div className="divide-y divide-[var(--border)] bg-[var(--surface-elevated)]">
                {gmailScopeRows.map(([label, status, tone]) => (
                  <div key={label} className="flex items-center justify-between gap-3 px-4 py-3">
                    <span className="text-sm font-semibold leading-5 text-[var(--ink-black)]/72">
                      {label}
                    </span>
                    <span
                      className={`shrink-0 text-xs font-black ${
                        tone === "green"
                          ? "text-[var(--green)]"
                          : "text-[var(--ink-black)]/42"
                      }`}
                    >
                      {status}
                    </span>
                  </div>
                ))}
              </div>
            </div>

            <a href={`${repositoryUrl}/blob/main/SECURITY.md`} className="btn btn-secondary mt-5 w-full justify-center">
              Read the security policy
            </a>
          </div>
        </section>

        <section className="paper-card border border-[var(--border)] bg-[var(--surface-strong)] p-5 sm:p-8">
          <div className="grid gap-6 lg:grid-cols-[minmax(0,0.78fr)_minmax(22rem,0.62fr)] lg:items-center">
            <div>
              <p className="eyebrow text-[var(--ink-blue)]">Why it exists</p>
              <h2 className="mt-3 max-w-3xl text-3xl font-bold leading-tight text-[var(--ink-black)] sm:text-4xl">
                Built for students and high-volume candidates who live in Gmail.
              </h2>
              <p className="mt-3 text-sm font-medium leading-6 text-[var(--ink-black)]/70 sm:text-base sm:leading-7">
                Built from a real high-volume student job search workflow:
                interviews, OAs, follow-ups, and offers are tracked from Gmail
                evidence instead of a manual spreadsheet.
              </p>
            </div>
            <div className="grid gap-3 sm:grid-cols-3 lg:grid-cols-1">
              {[
                ["Missed OA risk", "Assessment deadlines become visible next actions."],
                ["Follow-up drift", "Recruiter replies stay tied to the right application."],
                ["Offer context", "Evidence, resume version, and review state stay together."],
              ].map(([label, value]) => (
                <div
                  key={label}
                  className="rounded-lg border border-[var(--border)] bg-[var(--surface-elevated)] p-4"
                >
                  <p className="typewriter-text text-[0.66rem] uppercase tracking-[0.16em] text-[var(--ink-blue)]">
                    {label}
                  </p>
                  <p className="mt-2 text-sm font-bold leading-6 text-[var(--ink-black)]/72">
                    {value}
                  </p>
                </div>
              ))}
            </div>
          </div>
        </section>

        <section className="paper-card public-feature-card border border-[var(--border)] p-6 text-center sm:p-10">
          <div className="flex justify-center">
            <ProductRibbon label="Open source" />
          </div>
          <h2
            className="mx-auto mt-5 max-w-3xl text-3xl font-bold leading-tight text-[var(--ink-black)] sm:text-5xl"
            style={{ fontFamily: "var(--font-crimson-pro)" }}
          >
            See the product loop, then run your own copy.
          </h2>
          <p className="mx-auto mt-4 max-w-2xl text-base font-medium leading-7 text-[var(--ink-black)]/70 sm:text-lg">
            The demo walks the whole loop on a fictional inbox: sync,
            extraction, the review gate, application detail, and the
            multi-agent review. To use your own mail, clone the repository and
            connect Gmail with your own Google OAuth client.
          </p>
          <div className="mt-6 flex flex-wrap justify-center gap-3">
            <Link href={isSignedIn ? (nextPath as Route) : "#access"} className="btn btn-primary">
              {isSignedIn ? "Open workspace" : signInLabel}
            </Link>
            <a href={repositoryUrl} className="btn btn-secondary">
              Source on GitHub
            </a>
            <Link
              href="/tech"
              className="inline-flex min-h-[34px] items-center justify-center text-xs font-bold uppercase tracking-[0.14em] text-[var(--ink-blue)] underline-offset-4 hover:underline sm:px-2"
            >
              Architecture
            </Link>
          </div>
        </section>

        <PublicSiteFooter />
      </div>
    </main>
  );
}
