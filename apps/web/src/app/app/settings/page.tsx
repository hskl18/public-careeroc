import Link from "next/link";
import { requireUser } from "@/lib/require-user";
import { AssistantAccessPanel } from "@/components/assistant-access-panel";
import { ProfileSettingsPanel } from "@/components/profile-settings-panel";
import { getAccessTokensForUser } from "@/lib/access-tokens-server";
import { isDemoSnapshot } from "@/lib/demo";
import { getProfileForUser } from "@/lib/profile-server";
import { WorkspaceMetricTile } from "@/components/workspace-controls";

export default async function SettingsPage() {
  const user = await requireUser();
  const [{ profile, source: profileSource, proxyPrefix }, accessTokens] = await Promise.all([
    getProfileForUser(user),
    getAccessTokensForUser(user),
  ]);
  const displayName = profile.name || user.name || "careeroc user";
  const apiPublicBaseUrl = (process.env.API_PUBLIC_BASE_URL ?? process.env.API_BASE_URL)?.replace(/\/+$/, "");
  const agentPublicBaseUrl = process.env.AGENT_PUBLIC_BASE_URL?.replace(/\/+$/, "");

  return (
    <main className="app-scroll-main">
      <div className="workspace-shell flex w-full flex-col gap-3 px-3 py-4 animate-in sm:gap-4 sm:px-5 sm:py-6">
        <div className="flex shrink-0 flex-wrap items-center justify-between gap-3">
          <h1 className="text-xl font-semibold text-[var(--text-primary)]">Settings</h1>
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

        <div className="grid shrink-0 grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
          <WorkspaceMetricTile className="last:col-span-2 lg:last:col-span-1" label="Readiness" value={profile.evaluation.readinessScore} />
          <WorkspaceMetricTile className="last:col-span-2 lg:last:col-span-1" label="Apps" value={profile.evaluation.evidence.totalApplications} />
          <WorkspaceMetricTile className="last:col-span-2 lg:last:col-span-1"
            label="Resume"
            value={
              profile.evaluation.evidence.latestResumeScore === null
                ? "None"
                : `${profile.evaluation.evidence.latestResumeScore}/100`
            }
          />
          <WorkspaceMetricTile className="last:col-span-2 lg:last:col-span-1"
            label="Inbox"
            value={`${profile.evaluation.evidence.connectedInboxes}/${profile.evaluation.evidence.totalInboxes}`}
          />
          <WorkspaceMetricTile className="last:col-span-2 lg:last:col-span-1" label="Prefs" value={profile.evaluation.evidence.memoryEntries} />
        </div>

        {/* One panel fills the screen; with live access tokens, the two share it side by side. */}
        <div className="grid min-h-0 flex-1 gap-3 sm:gap-4 xl:auto-cols-fr xl:grid-flow-col xl:[grid-template-rows:minmax(0,1fr)]">
          <ProfileSettingsPanel
            initialProfile={profile}
            loadedFromApi={profileSource === "api"}
            proxyPrefix={proxyPrefix}
            email={profile.email}
            displayName={displayName}
          />
          {isDemoSnapshot ? null : (
            <div className="workspace-scroll-region min-h-0">
              <AssistantAccessPanel
                initialTokens={accessTokens}
                proxyPrefix={proxyPrefix}
                mcpUrl={apiPublicBaseUrl ? `${apiPublicBaseUrl}/mcp` : null}
                agentCardUrl={agentPublicBaseUrl ? `${agentPublicBaseUrl}/.well-known/agent-card.json` : null}
              />
            </div>
          )}
        </div>
      </div>
    </main>
  );
}
