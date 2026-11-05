import { Suspense } from "react";
import { requireUser } from "@/lib/require-user";
import dynamic from "next/dynamic";
import { AgentReviewPanel } from "@/components/agent-review-panel";
import { getDashboardForUser } from "@/lib/dashboard-server";
import { isDemoSnapshot } from "@/lib/demo";
import { getInboxSetupForUser } from "@/lib/inbox-accounts-server";
import { getMemoryReviewForUser } from "@/lib/profile-server";
import WorkspaceLoading from "./loading";

const DashboardWorkspace = dynamic(() =>
  import("@/components/dashboard-workspace").then(
    (module) => module.DashboardWorkspace,
  ),
);

export default async function DashboardPage() {
  return (
    <Suspense fallback={<WorkspaceLoading />}>
      <DashboardPageContent />
    </Suspense>
  );
}

async function DashboardPageContent() {
  const user = await requireUser();
  const [
    { dashboard, source: dashboardSource, proxyPrefix: dashboardProxyPrefix },
    { setup, source: inboxSetupSource, proxyPrefix: inboxProxyPrefix },
    { profile, pendingMemory },
  ] = await Promise.all([
    getDashboardForUser(user),
    getInboxSetupForUser(user),
    getMemoryReviewForUser(user),
  ]);
  const proxyPrefix = dashboardProxyPrefix ?? inboxProxyPrefix;

  return (
    <DashboardWorkspace
      initialDashboard={dashboard}
      dashboardSource={dashboardSource}
      initialInboxSetup={setup}
      inboxSetupSource={inboxSetupSource}
      proxyPrefix={proxyPrefix}
      agentReview={
        <AgentReviewPanel
          proxyPrefix={proxyPrefix}
          initialEntries={[...(profile.memory ?? []), ...pendingMemory]}
          readOnlyDemo={isDemoSnapshot}
        />
      }
      currentUserEmail={user.email}
      currentUserName={user.name}
      userTimeZoneId={profile.timeZoneId}
      welcomeDismissedAtUtc={profile.welcomeDismissedAtUtc}
    />
  );
}
