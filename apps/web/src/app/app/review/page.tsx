import { auth } from "@/auth";
import dynamic from "next/dynamic";
import { getInboxSetupForUser } from "@/lib/inbox-accounts-server";
import { getManualReviewForUser } from "@/lib/manual-review-server";
import { getProfileForUser } from "@/lib/profile-server";

const ManualReviewWorkspace = dynamic(() =>
  import("@/components/manual-review-workspace").then(
    (module) => module.ManualReviewWorkspace,
  ),
);

export default async function ReviewPage() {
  const session = await auth();
  const user = {
    email: session?.user?.email?.trim() || "local@careeroc.dev",
    name: session?.user?.name?.trim() || null,
  };
  const [
    { review, source, proxyPrefix },
    { setup, source: inboxSetupSource },
    { profile },
  ] = await Promise.all([
    getManualReviewForUser(user),
    getInboxSetupForUser(user),
    getProfileForUser(user),
  ]);

  const hasConnectedAccount = setup.accounts.some(
    (account) => account.connectionStatus === "Connected",
  );
  const hasSyncedOnce = setup.accounts.some(
    (account) => account.lastSyncedAtUtc !== null,
  );

  return (
    <main className="app-scroll-main">
      <div className="mx-auto flex min-h-full w-full max-w-[104rem] flex-col gap-4 px-5 py-6 animate-in md:h-full md:overflow-hidden">
        {inboxSetupSource === "fallback" || source === "fallback" ? (
          <div className="alert alert-warning shrink-0">
            Live API data is unavailable right now. The review queue is showing
            an empty local state until the API comes back.
          </div>
        ) : null}
        <ManualReviewWorkspace
          initialReview={review}
          source={source}
          proxyPrefix={proxyPrefix}
          currentUserEmail={user.email}
          currentUserName={user.name}
          timeZoneId={profile.timeZoneId}
          hasConnectedAccount={hasConnectedAccount}
          hasSyncedOnce={hasSyncedOnce}
        />
      </div>
    </main>
  );
}
