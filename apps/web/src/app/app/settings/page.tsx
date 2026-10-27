import Link from "next/link";
import { auth } from "@/auth";
import { AssistantAccessPanel } from "@/components/assistant-access-panel";
import { ProfileSettingsPanel } from "@/components/profile-settings-panel";
import { getAccessTokensForUser } from "@/lib/access-tokens-server";
import { getProfileForUser } from "@/lib/profile-server";

export default async function SettingsPage() {
  const session = await auth();
  const user = {
    email: session?.user?.email?.trim() || "local@careeroc.dev",
    name: session?.user?.name?.trim() || null,
  };
  const [{ profile, source: profileSource, proxyPrefix }, accessTokens] = await Promise.all([
    getProfileForUser(user),
    getAccessTokensForUser(user),
  ]);
  const displayName = profile.name || user.name || "careeroc user";
  const apiPublicBaseUrl = (process.env.API_PUBLIC_BASE_URL ?? process.env.API_BASE_URL)?.replace(/\/+$/, "");
  const agentPublicBaseUrl = process.env.AGENT_PUBLIC_BASE_URL?.replace(/\/+$/, "");

  return (
    <main className="app-scroll-main settings-main">
      <div className="mx-auto flex min-h-full w-full max-w-[104rem] flex-col gap-4 px-4 py-5 animate-in sm:px-5 sm:py-6">
        <section className="card app-workspace-panel shrink-0 p-4">
          <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
            <div className="flex min-w-0 items-start gap-4">
              <div className="min-w-0">
                <p className="eyebrow">Settings</p>
                <h1 className="mt-2 text-xl font-semibold text-[var(--text-primary)]">
                  Profile settings
                </h1>
                <p className="mt-2 max-w-2xl text-sm leading-7 text-[var(--text-secondary)]">
                  Manage your candidate profile, inbox connection, and privacy controls.
                </p>
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              <Link href="/app/inbox" className="btn btn-secondary btn-sm">
                Inbox
              </Link>
              <Link href="/app/memory" className="btn btn-secondary btn-sm">
                Memory
              </Link>
              <Link href="/app/settings/data" className="btn btn-secondary btn-sm">
                Data controls
              </Link>
            </div>
          </div>
        </section>

        <section className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_26rem]">
          <div className="space-y-4">
            <ProfileSettingsPanel
              initialProfile={profile}
              loadedFromApi={profileSource === "api"}
              proxyPrefix={proxyPrefix}
              email={profile.email}
              displayName={displayName}
            />
            <AssistantAccessPanel
              initialTokens={accessTokens}
              proxyPrefix={proxyPrefix}
              mcpUrl={apiPublicBaseUrl ? `${apiPublicBaseUrl}/mcp` : null}
              agentCardUrl={agentPublicBaseUrl ? `${agentPublicBaseUrl}/.well-known/agent-card.json` : null}
            />
          </div>

          <aside className="space-y-4 xl:sticky xl:top-20">
            <section className="card app-workspace-panel p-5">
              <p className="eyebrow">Your data</p>
              <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">
                Privacy controls
              </h2>
              <p className="mt-3 text-sm leading-6 text-[var(--text-secondary)]">
                We import application-related emails to power resume feedback,
                recommendations, and follow-up strategy.
              </p>
              <div className="mt-4 flex flex-wrap gap-2">
                <Link href="/app/settings/data" className="btn btn-secondary btn-sm">
                  Manage data
                </Link>
                <Link href="/app/memory" className="btn btn-secondary btn-sm">
                  Review memory
                </Link>
              </div>
            </section>

            <section className="card app-workspace-panel p-5">
              <p className="eyebrow">Profile readiness</p>
              <div className="mt-2 flex items-baseline gap-2">
                <span className="text-2xl font-semibold text-[var(--text-primary)]">
                  {profile.evaluation.readinessScore}/100
                </span>
                <span className="text-xs text-[var(--text-tertiary)]">ready</span>
              </div>
              <p className="mt-3 text-sm leading-6 text-[var(--text-secondary)]">
                {profile.evaluation.summary}
              </p>
              <div className="mt-4 grid grid-cols-2 gap-2">
                <SettingsMetric label="Apps" value={profile.evaluation.evidence.totalApplications} />
                <SettingsMetric
                  label="Resume"
                  value={
                    profile.evaluation.evidence.latestResumeScore === null
                      ? "None"
                      : `${profile.evaluation.evidence.latestResumeScore}/100`
                  }
                />
                <SettingsMetric
                  label="Inbox"
                  value={`${profile.evaluation.evidence.connectedInboxes}/${profile.evaluation.evidence.totalInboxes}`}
                />
                <SettingsMetric label="Prefs" value={profile.evaluation.evidence.memoryEntries} />
              </div>
              <Link href="/app/analytics" className="btn btn-secondary btn-sm mt-4">
                View recommendations
              </Link>
            </section>
          </aside>
        </section>
      </div>
    </main>
  );
}

function SettingsMetric({ label, value }: { label: string; value: number | string }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-2">
      <p className="eyebrow">{label}</p>
      <p className="mt-1 font-semibold text-[var(--text-primary)]">{value}</p>
    </div>
  );
}
