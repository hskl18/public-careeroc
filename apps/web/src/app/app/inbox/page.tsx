import { auth, isDemoMode } from "@/auth";
import dynamic from "next/dynamic";
import Link from "next/link";
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
  const session = await auth();
  const user = {
    email: session?.user?.email?.trim() || "local@careeroc.dev",
    name: session?.user?.name?.trim() || null,
  };
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
      <div className="mx-auto flex min-h-full w-full max-w-[104rem] flex-col gap-4 px-5 py-6 animate-in md:h-full md:overflow-hidden">
        <section className="card app-workspace-panel shrink-0 p-4">
          <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
            <div className="flex min-w-0 items-start gap-4">
              <div className="min-w-0">
                <p className="eyebrow">Inbox</p>
                <h1 className="mt-2 text-xl font-semibold text-[var(--text-primary)]">
                  {isDemoMode
                    ? "Demo inbox is connected"
                    : hasConnectedGmail
                      ? "Gmail inbox is connected"
                      : "Connect your Gmail inbox"}
                </h1>
                <p className="mt-2 max-w-2xl text-sm leading-7 text-[var(--text-secondary)]">
                  {hasConnectedGmail
                    ? "Review sync health, analysis progress, and Gmail data controls for the connected recruiting inbox."
                    : "careeroc finds recruiting emails, application confirmations, interview invites, and recruiter updates from your Gmail inbox."}
                </p>
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              <Link href="/app/settings" className="btn btn-secondary btn-sm">
                Settings
              </Link>
              <Link href="/app/settings/data" className="btn btn-secondary btn-sm">
                Data controls
              </Link>
            </div>
          </div>
        </section>

        <section className="grid min-h-0 flex-1 items-stretch gap-4 [grid-template-rows:minmax(0,1fr)] xl:grid-cols-[1.14fr_0.86fr]">
          <div className="workspace-scroll-region min-h-0 space-y-4">
            {isDemoMode ? (
              <section className="card app-workspace-panel p-5">
                <p className="eyebrow">Demo inbox</p>
                <h2 className="mt-2 text-xl font-semibold text-[var(--text-primary)]">
                  A fictional recruiting inbox
                </h2>
                <p className="mt-2 text-sm leading-7 text-[var(--text-secondary)]">
                  This workspace was built from about fifty invented emails across twenty
                  companies, processed by the same triage, extraction, review, and reminder
                  agents a real inbox goes through. Connecting Gmail is turned off in the demo.
                </p>
                <a
                  href="https://github.com/hskl18/public-careeroc"
                  className="btn btn-secondary btn-sm mt-4"
                >
                  Run careeroc with your own inbox
                </a>
              </section>
            ) : (
              <InboxSetupPanel
                initialSetup={setup}
                canPersist={inboxSetupSource === "api"}
                proxyPrefix={proxyPrefix}
                currentUserEmail={user.email}
                currentUserName={user.name}
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

          <div className="workspace-scroll-region min-h-0">
            <MailSyncPanel
              dashboard={dashboard}
              dashboardSource={dashboardSource}
              setup={setup}
              inboxSetupSource={inboxSetupSource}
              timeZoneId={profile.timeZoneId}
            />
          </div>
        </section>
      </div>
    </main>
  );
}
