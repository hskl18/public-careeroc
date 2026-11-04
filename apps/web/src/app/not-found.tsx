import Link from "next/link";
import { PublicSiteShell } from "@/components/public-site-chrome";

export default function NotFound() {
  return (
    <PublicSiteShell>
      <section className="card app-workspace-panel p-3 sm:p-4">
        <p className="eyebrow">Not found</p>
        <h1 className="mt-1 text-xl font-semibold text-[var(--text-primary)] sm:mt-2 sm:text-2xl">
          This page is not part of the current workspace
        </h1>
        <p className="mt-2 max-w-3xl text-base leading-6 text-[var(--text-secondary)]">
          The public site is intentionally small. Start from the overview or
          open the signed-in console if you are looking for workspace tools.
        </p>
        <nav className="mt-3 flex flex-wrap gap-2" aria-label="Recovery links">
          <Link href="/" className="btn btn-primary btn-sm">
            Overview
          </Link>
          <Link href="/tech" className="btn btn-secondary btn-sm">
            Architecture
          </Link>
          <Link href="/app" className="btn btn-secondary btn-sm">
            Open app
          </Link>
        </nav>
      </section>
    </PublicSiteShell>
  );
}
