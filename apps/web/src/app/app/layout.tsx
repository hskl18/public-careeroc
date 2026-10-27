import type { Metadata } from "next";
import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { auth, isAuthEnabled, isDemoMode } from "@/auth";
import { HeaderSyncStatus } from "@/components/header-sync-status";
import { Logo } from "@/components/logo";
import { ProfileIcon } from "@/components/profile-icon";
import { SignOutButton } from "@/components/sign-out-button";
import { UserMenu } from "@/components/user-menu";
import { WorkspaceNav } from "@/components/workspace-nav";
import { getAdminAccessForUser } from "@/lib/admin-server";
import { getDashboardForUser } from "@/lib/dashboard-server";
import { getInboxSetupForUser } from "@/lib/inbox-accounts-server";
import { getProfileForUser } from "@/lib/profile-server";

export const metadata: Metadata = {
  robots: {
    index: false,
    follow: false,
  },
};

export default async function AppLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  if (!isAuthEnabled) {
    redirect("/");
  }

  const session = await auth();

  if (!session?.user) {
    const cookieStore = await cookies();
    const hasStaleSessionCookie =
      cookieStore.has("authjs.session-token") ||
      cookieStore.has("__Secure-authjs.session-token");

    if (hasStaleSessionCookie) {
      redirect("/api/auth/recover?next=/app");
    }

    redirect("/?next=/app");
  }

  const user = {
    email: session.user.email?.trim() || "local@careeroc.dev",
    name: session.user.name?.trim() || null,
  };
  const displayName = user.name || "careeroc user";
  const displayEmail = user.email || "Google account";
  const [
    { dashboard, source: dashboardSource },
    { setup, source: inboxSetupSource },
    { profile },
    isAdmin,
  ] = await Promise.all([
    getDashboardForUser(user),
    getInboxSetupForUser(user),
    getProfileForUser(user),
    getAdminAccessForUser(user),
  ]);

  return (
    <div className="flex min-h-dvh flex-col md:h-dvh md:overflow-hidden">
      <header className="sticky top-0 z-50 shrink-0 border-b border-[var(--border)] bg-[var(--bg)]/82 backdrop-blur-xl">
        <div className="mx-auto flex h-14 w-full max-w-[104rem] items-center gap-4 px-5 sm:gap-6">
          <Link
            href="/app"
            className="flex shrink-0 items-center gap-2.5 rounded-md px-1.5 py-1 transition-colors hover:bg-[var(--surface)]"
          >
            <Logo className="h-7 w-7" />
            <span className="text-sm font-semibold text-[var(--text-primary)] hidden sm:block">
              careeroc
            </span>
          </Link>

          <WorkspaceNav />

          <div className="flex min-w-0 flex-1 justify-center">
            {isDemoMode ? (
              <p className="hidden truncate rounded-full bg-[var(--yellow-soft)] px-3 py-1 text-xs font-medium text-[var(--text-primary)] lg:block">
                Demo workspace with fictional data, deleted after 24 hours
              </p>
            ) : null}
          </div>

          <HeaderSyncStatus
            initialDashboard={dashboard}
            dashboardSource={dashboardSource}
            initialInboxSetup={setup}
            inboxSetupSource={inboxSetupSource}
            timeZoneId={profile.timeZoneId}
          />

          <UserMenu
            summary={
              <>
                <ProfileIcon
                  icon={profile.profileIcon}
                  label={`${displayName} profile icon`}
                  className="h-8 w-8 rounded-full border border-[var(--border)] bg-[var(--surface-elevated)]"
                />
                <span className="hidden max-w-44 text-right xl:block">
                  <span className="block text-xs font-medium leading-tight text-[var(--text-primary)]">
                    {displayName}
                  </span>
                  <span className="block truncate text-[11px] leading-tight text-[var(--text-tertiary)]">
                    {displayEmail}
                  </span>
                </span>
              </>
            }
          >
            <div
              className="absolute right-0 top-[calc(100%+0.6rem)] z-[100] isolate w-[min(18rem,calc(100vw-2rem))] overflow-hidden rounded-lg border border-[var(--brand-blue)]/18 bg-[var(--surface-strong)]/92 p-2 text-[var(--text-primary)] shadow-[0_18px_48px_rgba(15,23,42,0.14)] backdrop-blur-xl before:pointer-events-none before:absolute before:inset-0 before:bg-white/40 sm:w-72"
            >
              <div className="relative flex items-center gap-3 border-b border-[var(--border)] px-3 py-3">
                <ProfileIcon
                  icon={profile.profileIcon}
                  decorative
                  className="h-11 w-11 rounded-md border border-[var(--brand-green)]/30 bg-[var(--surface-elevated)] shadow-[0_0_0_3px_rgba(22,163,74,0.08)]"
                />
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium text-[var(--text-primary)]">
                    {displayName}
                  </p>
                  <p className="truncate text-xs text-[var(--text-tertiary)]">
                    {displayEmail}
                  </p>
                </div>
              </div>

              <Link
                href="/"
                className="relative mt-2 flex w-full items-center justify-between rounded-md px-3 py-2 text-sm font-medium text-[var(--text-primary)] transition-colors hover:bg-[var(--blue-soft)] hover:text-[var(--brand-blue)]"
              >
                Product overview
                <span className="text-[var(--text-tertiary)]">&rarr;</span>
              </Link>

              <Link
                href="/app/settings"
                className="relative mt-1 flex w-full items-center justify-between rounded-md px-3 py-2 text-sm font-medium text-[var(--text-primary)] transition-colors hover:bg-[var(--blue-soft)] hover:text-[var(--brand-blue)]"
              >
                Settings
                <span className="text-[var(--text-tertiary)]">&rarr;</span>
              </Link>

              <Link
                href="/app/memory"
                className="relative mt-1 flex w-full items-center justify-between rounded-md px-3 py-2 text-sm font-medium text-[var(--text-primary)] transition-colors hover:bg-[var(--blue-soft)] hover:text-[var(--brand-blue)]"
              >
                Memory
                <span className="text-[var(--text-tertiary)]">&rarr;</span>
              </Link>

              {isAdmin ? (
                <Link
                  href="/app/admin"
                  className="relative mt-1 flex w-full items-center justify-between rounded-md px-3 py-2 text-sm font-medium text-[var(--text-primary)] transition-colors hover:bg-[var(--blue-soft)] hover:text-[var(--brand-blue)]"
                >
                  Admin
                  <span className="text-[var(--text-tertiary)]">&rarr;</span>
                </Link>
              ) : null}

              <SignOutButton />
            </div>
          </UserMenu>
        </div>
      </header>

      <div className="min-h-0 flex-1 md:overflow-hidden">
        {children}
      </div>

      <div className="fixed inset-x-0 bottom-0 z-50 min-w-0 max-w-full overflow-hidden border-t border-[var(--border)] bg-[var(--bg)]/90 px-3 pb-[calc(env(safe-area-inset-bottom)+0.35rem)] pt-1.5 backdrop-blur-xl sm:hidden">
        <WorkspaceNav mode="bottom" />
      </div>
    </div>
  );
}
