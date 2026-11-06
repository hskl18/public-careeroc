import { requireUser } from "@/lib/require-user";
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
  const user = await requireUser();
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
      <div className="flex min-h-full w-full flex-col gap-4 px-3 py-4 sm:px-5 sm:py-6 animate-in md:h-full md:overflow-hidden">
        {inboxSetupSource === "fallback" || source === "fallback" ? (
          <div role="status" className="alert alert-warning shrink-0">
            API unavailable. Showing empty data.
          </div>
        ) : null}
        <ManualReviewWorkspace
          initialReview={review}
          source={source}
          proxyPrefix={proxyPrefix}
          timeZoneId={profile.timeZoneId}
          hasConnectedAccount={hasConnectedAccount}
          hasSyncedOnce={hasSyncedOnce}
        />
      </div>
    </main>
  );
}
