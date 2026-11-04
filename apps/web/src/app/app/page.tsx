import { Suspense } from "react";
import { auth } from "@/auth";
import dynamic from "next/dynamic";
import { getDashboardForUser } from "@/lib/dashboard-server";
import { getInboxSetupForUser } from "@/lib/inbox-accounts-server";
import { getProfileForUser } from "@/lib/profile-server";
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
  const session = await auth();
  const user = {
    email: session?.user?.email?.trim() || "local@careeroc.dev",
    name: session?.user?.name?.trim() || null,
  };
  const [
    { dashboard, source: dashboardSource, proxyPrefix: dashboardProxyPrefix },
    { setup, source: inboxSetupSource, proxyPrefix: inboxProxyPrefix },
    { profile },
  ] = await Promise.all([
    getDashboardForUser(user),
    getInboxSetupForUser(user),
    getProfileForUser(user),
  ]);

  return (
    <DashboardWorkspace
      initialDashboard={dashboard}
      dashboardSource={dashboardSource}
      initialInboxSetup={setup}
      inboxSetupSource={inboxSetupSource}
      proxyPrefix={dashboardProxyPrefix ?? inboxProxyPrefix}
      currentUserEmail={user.email}
      currentUserName={user.name}
      userTimeZoneId={profile.timeZoneId}
      welcomeDismissedAtUtc={profile.welcomeDismissedAtUtc}
    />
  );
}
