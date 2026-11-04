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
import { PublicSiteShell } from "@/components/public-site-chrome";
import { StatusPill } from "@/components/status-pill";
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
  ["Northstar Labs", "Software Engineer Intern", "Interviewing"],
  ["Atlas Robotics", "Backend Engineer", "Oa Pending"],
  ["Linear Systems", "Product Infrastructure", "Applied"],
  ["Unknown Company", "Product Engineering", "Review"],
];

const howSteps = [
  ["1", "Connect Gmail", "Read-only access to recruiting mail, separate from sign-in."],
  ["2", "Extract evidence", "Threads become companies, roles, deadlines, and contacts."],
  ["3", "Review risky changes", "Low-confidence updates wait in a review queue."],
  ["4", "Work the pipeline", "Applications, reminders, and resume feedback in one place."],
];

const rowClass =
  "rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-2.5";

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
    <PublicSiteShell>
      <section className="card app-workspace-panel p-3 sm:p-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <p className="eyebrow">Open-source agent system</p>
            <h1 className="mt-1 text-xl font-semibold text-[var(--text-primary)] sm:mt-2 sm:text-2xl">
              Multi-agent job search for recruiting email
            </h1>
            <p className="mt-2 max-w-3xl text-base leading-6 text-[var(--text-secondary)]">
              careeroc turns recruiting email into an evidence-backed pipeline.
              Specialist agents review the search, and you approve what they propose.
            </p>
            <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">
              {isDemoSnapshot
                ? "No sign-up. You get a read-only workspace built from a fictional inbox."
                : isDemoMode
                  ? "No sign-up. You get a private workspace built from a fictional inbox, deleted after 24 hours."
                  : "Google sign-in and Gmail access are separate. Gmail sync is controlled inside the app."}
            </p>
          </div>

          <div
            id="access"
            className="flex w-full scroll-mt-16 flex-col gap-2 sm:w-auto sm:flex-row sm:items-center lg:shrink-0"
          >
            {isSignedIn ? (
              <Link
                href={nextPath as Route}
                className="btn btn-primary btn-sm justify-center"
              >
                Open workspace
              </Link>
            ) : isAuthEnabled ? (
              <form
                action={async () => {
                  "use server";
                  await startSignIn(nextPath);
                }}
              >
                <GoogleSignInSubmit demo={isDemoMode} />
              </form>
            ) : (
              <p className="rounded-md bg-[var(--yellow-soft)] px-3 py-2 text-sm text-[var(--text-primary)]">
                Google login is not configured yet. Set `AUTH_GOOGLE_ID`
                and `AUTH_GOOGLE_SECRET`
                {isGoogleAuthConfigured ? "" : " for the web app"}, then
                refresh this page.
              </p>
            )}
            <a href={repositoryUrl} className="btn btn-secondary btn-sm justify-center">
              Source on GitHub
            </a>
          </div>
        </div>

        {authErrorMessage && (
          <p className="mt-3 rounded-md bg-[var(--red-soft)] px-3 py-2 text-sm text-[var(--red)]">
            {authErrorMessage}
          </p>
        )}

        {sessionRecoveryMessage && (
          <p className="mt-3 rounded-md bg-[var(--green-soft)] px-3 py-2 text-sm text-[var(--green)]">
            {sessionRecoveryMessage}
          </p>
        )}
      </section>

      <div className="grid gap-3 sm:gap-4 lg:grid-cols-2">
        <section className="card app-workspace-panel p-3 sm:p-4">
          <p className="eyebrow">Example</p>
          <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">
            Active pipeline
          </h2>
          <div className="mt-3 grid gap-2">
            {pipelinePreview.map(([company, role, status]) => (
              <div
                key={company}
                className={`flex items-center justify-between gap-3 ${rowClass}`}
              >
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium text-[var(--text-primary)]">
                    {company}
                  </p>
                  <p className="truncate text-xs text-[var(--text-tertiary)]">{role}</p>
                </div>
                <StatusPill status={status} />
              </div>
            ))}
          </div>
        </section>

        <section className="card app-workspace-panel p-3 sm:p-4">
          <p className="eyebrow">How it works</p>
          <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">
            From inbox to pipeline
          </h2>
          <ol className="mt-3 grid gap-2">
            {howSteps.map(([step, title, body]) => (
              <li key={step} className={`flex items-baseline gap-3 ${rowClass}`}>
                <span className="eyebrow">{step}</span>
                <div className="min-w-0">
                  <h3 className="text-sm font-semibold text-[var(--text-primary)]">{title}</h3>
                  <p className="text-xs text-[var(--text-tertiary)]">{body}</p>
                </div>
              </li>
            ))}
          </ol>
        </section>
      </div>
    </PublicSiteShell>
  );
}
