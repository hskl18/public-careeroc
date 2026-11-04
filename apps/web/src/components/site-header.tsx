import type { Route } from "next";
import Link from "next/link";
import { Logo } from "@/components/logo";

type SiteHeaderProps = {
  homeHref: Route;
  children: React.ReactNode;
};

// The workspace and the public pages share this bar so the two cannot drift apart.
export function SiteHeader({ homeHref, children }: SiteHeaderProps) {
  return (
    <header className="sticky top-0 z-50 shrink-0 border-b border-[var(--border)] bg-[var(--bg)]/82 backdrop-blur-xl">
      <div className="mx-auto flex h-14 w-full max-w-[104rem] items-center gap-4 px-5 sm:gap-6">
        <Link
          href={homeHref}
          className="flex shrink-0 items-center gap-2.5 rounded-md px-1.5 py-1 transition-colors hover:bg-[var(--surface)]"
        >
          <Logo className="h-7 w-7" />
          <span className="text-sm font-semibold text-[var(--text-primary)] hidden sm:block">
            careeroc
          </span>
        </Link>
        {children}
      </div>
    </header>
  );
}
