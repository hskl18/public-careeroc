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
import { GoogleSignInSubmit } from "@/components/google-sign-in-submit";
import { PublicSiteShell } from "@/components/public-site-chrome";
import { StatusPill } from "@/components/status-pill";
import { SvgAsset, mascotAssets } from "@/components/svg-asset";
import { buildPageMetadata, repositoryUrl } from "@/lib/site-metadata";
import { safeAppPath } from "@/lib/url-policy";

export const metadata: Metadata = buildPageMetadata({
  title: "Multi-agent job search for recruiting email",
});

type HomePageProps = {
  searchParams?: Promise<Record<string, string | string[] | undefined>>;
};

function readSearchParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
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

const demoCounts = [
  ["51", "Demo emails", "profile-inbox-blue"],
  ["20", "Applications", "profile-resume-stack"],
  ["4", "Interviewing", "profile-agent-spark"],
  ["1", "Offer", "profile-star-scout"],
  ["2", "Email review", "profile-inbox-alert"],
];

const agents = [
  ["Mailbox analyst", "profile-inbox-mint", "Reads outcomes."],
  ["Resume analyst", "profile-resume-card", "Checks evidence."],
  ["Strategy analyst", "profile-agent-radar", "Picks next actions."],
  ["Supervisor", "profile-agent-crown", "Merges all three."],
  ["Evidence gate", "profile-agent-flag", "Drops what it can't verify."],
  ["You", "profile-inbox-wink", "Approve or reject."],
];

const pipelinePreview = [
  ["Kestrel AI", "Backend Engineer, New Grad", "Offer"],
  ["Lumen Robotics", "Robotics Software Engineer", "Interviewing"],
  ["Redwood Security", "Security Engineer", "Oa Pending"],
  ["Summit Logistics", "Junior Software Developer", "Applied"],
  ["Agency email", "Backend Engineer, confidential client", "Email review"],
];

const howSteps = [
  [mascotAssets.loading, "Connect Gmail", "Read-only."],
  [mascotAssets.loadingResumeScan, "Extract evidence", "Company, role, deadline."],
  [mascotAssets.resumeBuddyStack, "Review risky changes", "Unsure mail waits for you."],
  ["/profile-icons/profile-agent-crown.svg", "Agent review", "Nothing applies until you approve."],
  [mascotAssets.resumeBuddyCard, "Work the pipeline", "Applications and reminders."],
];

function characterPath(name: string) {
  return `/profile-icons/${name}.svg`;
}

const rowClass =
  "rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-1.5";

export default async function HomePage({ searchParams }: HomePageProps) {
  const session = await getOptionalSession();
  const isSignedIn = Boolean(session?.user);
  const resolvedSearchParams = (await searchParams) ?? {};
  const nextPath = safeAppPath(readSearchParam(resolvedSearchParams.next));
  const authError = readSearchParam(resolvedSearchParams.error);
  const authErrorMessage = formatAuthError(authError);
  const sessionRecoveryMessage = formatSessionRecovery(
    readSearchParam(resolvedSearchParams.session),
  );

  return (
    <PublicSiteShell>
      <section className="card flex grow flex-col p-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="flex min-w-0 items-center gap-3 sm:gap-4">
            <SvgAsset
              src={mascotAssets.loading}
              decorative
              className="h-12 w-12"
            />
            <h1 className="min-w-0 text-xl font-semibold text-[var(--text-primary)]">
              Multi-agent job search for recruiting email
            </h1>
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

        <div className="mt-2 grid flex-1 grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
          {demoCounts.map(([count, label, character]) => (
            <div key={label} className={`flex items-center gap-3 last:col-span-2 lg:last:col-span-1 ${rowClass}`}>
              <SvgAsset src={characterPath(character)} decorative className="h-9 w-9" />
              <div className="min-w-0">
                <p className="text-base font-semibold text-[var(--text-primary)]">{count}</p>
                <p className="text-xs text-[var(--text-tertiary)]">{label}</p>
              </div>
            </div>
          ))}
        </div>
      </section>

      <section className="card flex grow flex-col p-4">
        <h2 className="text-base font-semibold text-[var(--text-primary)]">The agents</h2>
        <div className="mt-2 grid flex-1 gap-2 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
          {agents.map(([name, character, body]) => (
            <article key={name} className={`flex items-center gap-2 ${rowClass}`}>
              <SvgAsset src={characterPath(character)} decorative className="h-10 w-10" />
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-[var(--text-primary)]">{name}</h3>
                <p className="text-xs text-[var(--text-tertiary)]">{body}</p>
              </div>
            </article>
          ))}
        </div>
      </section>

      <div className="grid grow-[2] gap-3 lg:grid-cols-2">
        <section className="card flex flex-col p-4">
          <h2 className="text-base font-semibold text-[var(--text-primary)]">
            Demo pipeline
          </h2>
          <div className="mt-1 flex flex-1 flex-col divide-y divide-[var(--border)]">
            {pipelinePreview.map(([company, role, status]) => (
              <div key={company} className="flex flex-1 items-center justify-between gap-3 py-1">
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

        <section className="card flex flex-col p-4">
          <h2 className="text-base font-semibold text-[var(--text-primary)]">
            From inbox to pipeline
          </h2>
          <ol className="mt-1 flex flex-1 flex-col divide-y divide-[var(--border)]">
            {howSteps.map(([mascot, title, body]) => (
              <li key={title} className="flex flex-1 items-center gap-3 py-1">
                <SvgAsset src={mascot} decorative className="h-8 w-8" />
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
