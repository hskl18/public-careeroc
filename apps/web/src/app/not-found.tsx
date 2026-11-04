import Link from "next/link";
import { PublicSiteNav } from "@/components/public-site-chrome";

export default function NotFound() {
  return (
    <main className="public-landing-page flex-1 px-4 py-4 sm:px-6 lg:px-8">
      <div className="mx-auto flex max-w-[1600px] flex-col gap-4">
        <PublicSiteNav />
        <section className="paper-card public-feature-card relative overflow-hidden border border-[var(--border)] p-7 sm:p-9">
          <div className="absolute bottom-0 left-0 h-2 w-full bg-[var(--accent)]" />
          <p className="eyebrow text-[var(--ink-blue)]">Not found</p>
          <h1 className="page-title mt-4 max-w-4xl">
            This page is not part of the current workspace.
          </h1>
          <p className="page-lead mt-5 max-w-3xl">
            The public site is intentionally small. Start from the overview or
            open the signed-in console if you are looking for workspace tools.
          </p>
          <nav className="mt-6 flex flex-wrap gap-3" aria-label="Recovery links">
            <Link href="/" className="btn btn-primary">
              Overview
            </Link>
            <Link href="/tech" className="btn btn-secondary">
              Architecture
            </Link>
            <Link href="/app" className="btn btn-secondary">
              Open app
            </Link>
          </nav>
        </section>
      </div>
    </main>
  );
}
