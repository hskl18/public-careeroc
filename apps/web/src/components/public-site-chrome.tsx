import type { Route } from "next";
import Link from "next/link";
import { SiteHeader } from "@/components/site-header";
import { WorkspaceNav } from "@/components/workspace-nav";
import { repositoryUrl } from "@/lib/site-metadata";

const navItems: { href: Route; label: string }[] = [
  { href: "/", label: "Overview" },
  { href: "/tech", label: "Architecture" },
];
const navLabel = "Public showcase sections";

const footerLinkClass =
  "inline-flex min-h-6 items-center hover:text-[var(--ink-blue)]";

// Public pages are prerendered, so the header cannot depend on the session; the landing page
// shows the right action for signed-in and signed-out visitors.
// Pages fill the screen by giving their sections `grow`, so nothing is left blank under the footer.
export function PublicSiteShell({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-dvh flex-col">
      <SiteHeader homeHref="/">
        <WorkspaceNav items={navItems} label={navLabel} />
        <div className="flex-1" />
        <Link href="/#access" className="btn btn-primary btn-sm">
          Get started
        </Link>
      </SiteHeader>
      <main className="flex flex-1 flex-col gap-3 px-3 pb-[calc(5.25rem+env(safe-area-inset-bottom))] pt-3 sm:px-5 sm:pb-2">
        {children}
        <footer className="flex flex-wrap justify-end gap-4 text-xs text-[var(--text-tertiary)]">
          <a href={repositoryUrl} className={footerLinkClass}>
            Source
          </a>
          <a href={`${repositoryUrl}/blob/main/SECURITY.md`} className={footerLinkClass}>
            Security policy
          </a>
        </footer>
      </main>
      <WorkspaceNav mode="bottom" items={navItems} label={navLabel} />
    </div>
  );
}
