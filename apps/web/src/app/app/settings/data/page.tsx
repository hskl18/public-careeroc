import Link from "next/link";
import { auth } from "@/auth";
import { DataControlsPanel } from "@/components/data-controls-panel";
import { getDashboardForUser } from "@/lib/dashboard-server";
import { getInboxSetupForUser } from "@/lib/inbox-accounts-server";
import { getProfileForUser } from "@/lib/profile-server";

export default async function DataSettingsPage() {
  const session = await auth();
  const user = {
    email: session?.user?.email?.trim() || "local@careeroc.dev",
    name: session?.user?.name?.trim() || null,
  };
  const [
    { dashboard },
    { setup, source: inboxSetupSource, proxyPrefix },
    { profile },
  ] = await Promise.all([
    getDashboardForUser(user),
    getInboxSetupForUser(user),
    getProfileForUser(user),
  ]);
  const connectedAccounts = setup.accounts.filter(
    (account) => account.connectionStatus === "Connected",
  );
  const disconnectedAccounts = setup.accounts.filter(
    (account) => account.connectionStatus !== "Connected",
  );

  return (
    <main className="app-scroll-main">
      <div className="workspace-shell mx-auto flex w-full max-w-[104rem] flex-col px-5 py-6 animate-in">
        <section className="workspace-scroll-region min-h-0 space-y-4">
          <div className="card p-5">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <div className="flex items-start gap-4">
                <div>
                  <p className="eyebrow">Settings</p>
                  <h1 className="mt-2 text-2xl font-semibold">Data controls</h1>
                  <p className="mt-1 text-sm text-[var(--text-secondary)]">
                    Review retention status and delete imported inbox data for {profile.email}.
                  </p>
                </div>
              </div>
              <Link href="/app/settings" className="btn btn-secondary btn-sm">
                Settings
              </Link>
            </div>
          </div>

          <div className="grid gap-3 md:grid-cols-3">
            <Metric label="Imported emails" value={dashboard.inbox.syncedEmails} />
            <Metric label="Analyzed emails" value={dashboard.inbox.processedEmails} />
            <Metric label="Gmail inboxes" value={setup.accounts.length} />
          </div>

          <section className="card p-5">
            <div className="mb-4 flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
              <div>
                <p className="eyebrow">Inbox retention</p>
                <h2 className="text-xl font-semibold">Connected and disconnected inboxes</h2>
              </div>
              <Link href="/app/inbox" className="btn btn-secondary btn-sm">
                Inbox
              </Link>
            </div>
            <div className="grid gap-3 md:grid-cols-2">
              <InboxState
                title="Connected"
                count={connectedAccounts.length}
                detail="Sync is allowed while OAuth tokens are present."
              />
              <InboxState
                title="Disconnected"
                count={disconnectedAccounts.length}
                detail="Tokens are removed and application history stays available."
              />
            </div>
          </section>

          <DataControlsPanel
            accounts={setup.accounts}
            proxyPrefix={proxyPrefix}
            canPersist={inboxSetupSource === "api"}
          />
        </section>
      </div>
    </main>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div className="card p-4">
      <p className="eyebrow">{label}</p>
      <p className="mt-2 text-3xl font-semibold text-[var(--text-primary)]">
        {value.toLocaleString()}
      </p>
    </div>
  );
}

function InboxState({
  title,
  count,
  detail,
}: {
  title: string;
  count: number;
  detail: string;
}) {
  return (
    <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-elevated)] p-4">
      <div className="flex items-start justify-between gap-3">
        <h3 className="text-base font-semibold">{title}</h3>
        <span className="badge">{count}</span>
      </div>
      <p className="mt-2 text-sm text-[var(--text-secondary)]">{detail}</p>
    </div>
  );
}
