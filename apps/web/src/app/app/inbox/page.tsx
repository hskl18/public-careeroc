import { isDemoMode } from "@/auth";
import { requireUser } from "@/lib/require-user";
import dynamic from "next/dynamic";
import { getDashboardForUser } from "@/lib/dashboard-server";
import { getInboxSetupForUser } from "@/lib/inbox-accounts-server";
import { getProfileForUser } from "@/lib/profile-server";
import { getRecruitingContactsForUser } from "@/lib/recruiting-contacts-server";

const InboxSetupPanel = dynamic(() =>
  import("@/components/inbox-setup-panel").then(
    (module) => module.InboxSetupPanel,
  ),
);
const MailSyncPanel = dynamic(() =>
  import("@/components/mail-sync-panel").then((module) => module.MailSyncPanel),
);
const RecruitingContactHistoryPanel = dynamic(() =>
  import("@/components/recruiting-contact-history-panel").then(
    (module) => module.RecruitingContactHistoryPanel,
  ),
);

export default async function InboxPage() {
  const user = await requireUser();
  const [
    { dashboard, source: dashboardSource },
    { setup, source: inboxSetupSource, proxyPrefix },
    { profile },
    {
      contacts,
      source: recruitingContactsSource,
      proxyPrefix: recruitingContactsProxyPrefix,
    },
  ] = await Promise.all([
    getDashboardForUser(user),
    getInboxSetupForUser(user),
    getProfileForUser(user),
    getRecruitingContactsForUser(user),
  ]);
  const connectedGmailCount = setup.accounts.filter(
    (account) => account.provider === "gmail" && account.connectionStatus === "Connected",
  ).length;
  const hasConnectedGmail = connectedGmailCount > 0;

  return (
    <main className="app-scroll-main">
      <div className="flex min-h-full w-full flex-col gap-4 px-3 py-4 sm:px-5 sm:py-6 animate-in md:h-full md:overflow-hidden">
        <section className="shrink-0">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex min-w-0 items-start gap-4">
              <div className="min-w-0">
                <h1 className="text-xl font-semibold text-[var(--text-primary)]">
                  {isDemoMode ? "Demo inbox" : hasConnectedGmail ? "Gmail inbox" : "Connect Gmail"}
                </h1>
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              {isDemoMode ? (
                <a href="https://github.com/hskl18/public-careeroc" className="btn btn-secondary btn-sm">
                  Use your own inbox
                </a>
              ) : null}
            </div>
          </div>
        </section>

        <section className={`grid min-h-0 flex-1 items-stretch gap-4 [grid-template-rows:minmax(0,1fr)] ${isDemoMode ? "" : "md:[grid-template-rows:repeat(2,minmax(0,1fr))] xl:grid-cols-[1.14fr_0.86fr] xl:[grid-template-rows:minmax(0,1fr)]"}`}>
          <div className="workspace-scroll-region min-h-0 min-w-0 space-y-4">
            {isDemoMode ? null : (
              <InboxSetupPanel
                initialSetup={setup}
                canPersist={inboxSetupSource === "api"}
                proxyPrefix={proxyPrefix}
                currentUserEmail={user.email}
                timeZoneId={profile.timeZoneId}
              />
            )}
            <RecruitingContactHistoryPanel
              initialContacts={contacts.contacts}
              source={recruitingContactsSource}
              proxyPrefix={recruitingContactsProxyPrefix ?? proxyPrefix}
              timeZoneId={profile.timeZoneId}
            />
          </div>

          {isDemoMode ? null : (
            <div className="workspace-scroll-region min-h-0 min-w-0">
              <MailSyncPanel
                dashboard={dashboard}
                dashboardSource={dashboardSource}
                setup={setup}
                inboxSetupSource={inboxSetupSource}
                timeZoneId={profile.timeZoneId}
              />
            </div>
          )}
        </section>
      </div>
    </main>
  );
}
