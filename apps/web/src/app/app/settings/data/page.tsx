import Link from "next/link";
import { requireUser } from "@/lib/require-user";
import { DataControlsPanel } from "@/components/data-controls-panel";
import { WorkspaceMetricTile } from "@/components/workspace-controls";
import { getDashboardForUser } from "@/lib/dashboard-server";
import { getInboxSetupForUser } from "@/lib/inbox-accounts-server";

export default async function DataSettingsPage() {
  const user = await requireUser();
  const [{ dashboard }, { setup, source: inboxSetupSource, proxyPrefix }] = await Promise.all([
    getDashboardForUser(user),
    getInboxSetupForUser(user),
  ]);
  const connected = setup.accounts.filter((account) => account.connectionStatus === "Connected").length;
  const tiles: [string, number][] = [
    ["Imported", dashboard.inbox.syncedEmails],
    ["Analyzed", dashboard.inbox.processedEmails],
    ["Inboxes", setup.accounts.length],
    ["Connected", connected],
    ["Disconnected", setup.accounts.length - connected],
  ];

  return (
    <main className="app-scroll-main">
      <div className="workspace-shell flex w-full flex-col gap-3 px-3 py-4 animate-in sm:gap-4 sm:px-5 sm:py-6">
        <div className="flex shrink-0 flex-wrap items-center justify-between gap-3">
          <h1 className="text-xl font-semibold text-[var(--text-primary)]">Data controls</h1>
          <div className="flex flex-wrap gap-2">
            <Link href="/app/inbox" className="btn btn-secondary btn-sm">
              Inbox
            </Link>
            <Link href="/app/settings" className="btn btn-secondary btn-sm">
              Settings
            </Link>
          </div>
        </div>

        <div className="grid shrink-0 grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
          {tiles.map(([label, value]) => (
            <WorkspaceMetricTile
              key={label}
              label={label}
              value={value.toLocaleString("en-US")}
              className="last:col-span-2 lg:last:col-span-1"
            />
          ))}
        </div>

        <div className="workspace-scroll-region fill-last min-h-0 flex-1">
          <DataControlsPanel
            accounts={setup.accounts}
            proxyPrefix={proxyPrefix}
            canPersist={inboxSetupSource === "api"}
          />
        </div>
      </div>
    </main>
  );
}
