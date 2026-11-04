import type { Metadata } from "next";
import type { Route } from "next";
import Link from "next/link";
import {
  getOptionalSession,
  isAuthEnabled,
  isDemoMode,
  isGoogleAuthConfigured,
  startSignIn,
} from "@/auth";
import { isDemoSnapshot } from "@/lib/demo";
import { GoogleSignInSubmit } from "@/components/google-sign-in-submit";
import {
  PublicSiteFooter,
  PublicSiteNav,
  ProductRibbon,
} from "@/components/public-site-chrome";
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

const pipelinePreview = [
  ["N", "Northstar Labs", "Software Engineer Intern", "Interview", "green"],
  ["A", "Atlas Robotics", "Backend Engineer", "OA due", "amber"],
  ["L", "Linear Systems", "Product Infrastructure", "Applied", "blue"],
  ["?", "Unknown Company", "Product Engineering", "Review", "red"],
];

const howSteps = [
  ["1", "Connect Gmail", "Read-only access to recruiting mail, separate from sign-in."],
  ["2", "Extract evidence", "Threads become companies, roles, deadlines, and contacts."],
  ["3", "Review risky changes", "Low-confidence updates wait in a review queue."],
  ["4", "Work the pipeline", "Applications, reminders, and resume feedback in one place."],
];

function PipelinePreview() {
  return (
    <section
      aria-label="Example application pipeline"
      className="public-product-panel grid gap-2 rounded-xl border p-4 sm:p-5"
    >
      <p className="typewriter-text mb-1 text-[0.68rem] uppercase tracking-[0.18em] text-[var(--text-tertiary)]">
        Active pipeline
      </p>
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
    <main className="public-landing-page flex-1 px-3 py-4 sm:px-5 sm:py-6">
      <div className="mx-auto grid max-w-[104rem] gap-12 pb-12">
        <PublicSiteNav current="product" />

        <section className="grid gap-8 -mt-6 lg:grid-cols-[minmax(0,0.92fr)_minmax(25rem,0.72fr)] lg:items-center lg:gap-10">
          <div className="flex min-h-0 flex-col">
            <ProductRibbon className="mb-5 self-start" label="Open-source agent system" />
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
              careeroc turns recruiting email into an evidence-backed pipeline.
              Specialist agents review the search, and you approve what they propose.
            </p>

            <div id="access" className="mt-6 flex flex-wrap items-center gap-3">
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
              <a href={repositoryUrl} className="btn btn-secondary justify-center px-4 text-xs">
                Source on GitHub
              </a>
            </div>

            <p className="mt-4 flex items-center gap-2 text-xs font-semibold leading-5 text-[var(--ink-black)]/58">
              <span className="grid h-4 w-4 place-items-center rounded-full border border-[var(--green)] text-[0.55rem] text-[var(--green)]">
                ✓
              </span>
              {isDemoSnapshot
                ? "No sign-up. You get a read-only workspace built from a fictional inbox."
                : isDemoMode
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
          </div>

          <PipelinePreview />
        </section>

        <section
          aria-label="How careeroc works"
          className="grid gap-6 border-t border-[var(--border)] pt-8 md:grid-cols-2 lg:grid-cols-4"
        >
          {howSteps.map(([step, title, body]) => (
            <article key={step}>
              <p className="eyebrow text-[var(--ink-blue)]">Step {step}</p>
              <h2 className="mt-2 text-base font-bold text-[var(--ink-black)]">{title}</h2>
              <p className="mt-1 text-sm font-medium leading-6 text-[var(--ink-black)]/68">{body}</p>
            </article>
          ))}
        </section>

        <PublicSiteFooter />
      </div>
    </main>
  );
}
