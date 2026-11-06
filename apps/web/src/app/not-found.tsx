import Link from "next/link";
import { PublicSiteShell } from "@/components/public-site-chrome";

export default function NotFound() {
  return (
    <PublicSiteShell>
      <section className="card p-4">
        <h1 className="text-xl font-semibold text-[var(--text-primary)]">
          Page not found
        </h1>
        <nav className="mt-3 flex flex-wrap gap-2" aria-label="Recovery links">
          <Link href="/" className="btn btn-primary btn-sm">
            Overview
          </Link>
          <Link href="/tech" className="btn btn-secondary btn-sm">
            Architecture
          </Link>
          <Link href="/app" className="btn btn-secondary btn-sm">
            Open workspace
          </Link>
        </nav>
      </section>
    </PublicSiteShell>
  );
}
