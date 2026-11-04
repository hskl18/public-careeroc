import type { Route } from "next";
import Link from "next/link";
import { repositoryUrl } from "@/lib/site-metadata";
import { Logo } from "@/components/logo";

type PublicNavId = "product" | "technology";

type ProductRibbonProps = {
  label?: string;
  className?: string;
};

export function ProductRibbon({
  label = "Gmail-first recruiting workspace",
  className = "",
}: ProductRibbonProps) {
  return (
    <div
      className={`inline-flex items-center gap-2 rounded-full border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-1.5 ${className}`}
    >
      <span className="h-1.5 w-1.5 rounded-full bg-[var(--green)]" />
      <p className="typewriter-text text-[0.66rem] uppercase tracking-[0.16em] text-[var(--ink-blue)] sm:text-[0.68rem]">
        {label}
      </p>
    </div>
  );
}

const navLinks = [
  { href: "/", label: "Overview", id: "product" },
  { href: "/tech" as Route, label: "Architecture", id: "technology" },
] satisfies Array<{
  href: Route;
  label: string;
  id: PublicNavId;
}>;

const footerLinkClass =
  "inline-flex min-h-[34px] items-center rounded-sm py-1 hover:text-[var(--ink-blue)]";
const footerStrongLinkClass =
  "inline-flex min-h-[34px] min-w-[34px] items-center justify-center rounded-sm py-1 font-semibold text-[var(--ink-blue)]";

type PublicSiteNavProps = {
  current?: PublicNavId;
};

// Public pages are prerendered, so the nav cannot depend on the session; the landing page's
// sign-in section shows the right action for signed-in and signed-out visitors.
export function PublicSiteNav({ current }: PublicSiteNavProps) {
  return (
    <>
      <header className="public-nav-shell sticky top-3 z-50 mx-auto w-full max-w-[104rem] rounded-lg border px-3 py-2.5 backdrop-blur-xl sm:px-5">
        <div className="flex items-center justify-between gap-3">
          <div className="flex min-w-0 items-center justify-between gap-3">
            <Link
              href="/"
              className="flex min-h-9 min-w-0 items-center gap-2.5 sm:gap-3"
            >
              <Logo className="h-7 w-7 shrink-0" />
              <span className="min-w-0">
                <span className="block truncate text-sm font-semibold text-[var(--ink-black)] sm:text-base">
                  careeroc
                </span>
              </span>
            </Link>
          </div>
          <nav
            aria-label="Public showcase sections"
            className="public-site-nav hidden items-center gap-1 rounded-md p-1 text-center text-sm font-semibold lg:flex"
          >
            {navLinks.map((link) => {
              const isActive = current === link.id;
              return (
                <Link
                  key={link.href}
                  href={link.href}
                  aria-current={isActive ? "page" : undefined}
                  className={`public-site-nav-link ${
                    isActive ? "is-active" : ""
                  }`}
                >
                  {link.label}
                </Link>
              );
            })}
          </nav>
          <div className="flex shrink-0 items-center justify-end gap-2">
            <Link href="/#access" className="btn btn-primary btn-sm">
              Get started
            </Link>
          </div>
        </div>
      </header>
      <nav
        aria-label="Public showcase sections"
        className="public-bottom-nav fixed inset-x-0 bottom-0 z-50 grid grid-cols-2 gap-1.5 border-t px-3 pb-[calc(env(safe-area-inset-bottom)+0.55rem)] pt-2.5 text-center shadow-[0_-10px_30px_rgba(17,33,45,0.08)] backdrop-blur-xl lg:hidden"
      >
        {navLinks.map((link) => {
          const isActive = current === link.id;
          return (
            <Link
              key={link.href}
              href={link.href}
              aria-current={isActive ? "page" : undefined}
              className={`public-mobile-nav-link ${
                isActive ? "is-active" : ""
              }`}
            >
              {link.label}
            </Link>
          );
        })}
      </nav>
    </>
  );
}

export function PublicSiteFooter() {
  return (
    <footer className="relative z-10 mx-auto flex w-full max-w-[104rem] flex-col gap-2 py-1 text-xs text-[var(--ink-black)]/58 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex flex-wrap items-center gap-3">
        <p className="typewriter-text uppercase tracking-[0.18em]">
          Open source
        </p>
        <span className="typewriter-text inline-flex items-center gap-1.5 uppercase tracking-[0.18em] text-[var(--ink-blue)]">
          <span className="h-1.5 w-1.5 rounded-full bg-[var(--green)]" />
          Evidence-first recruiting workspace
        </span>
      </div>
      <nav className="flex flex-wrap gap-3">
        <Link href="/tech" className={footerStrongLinkClass}>
          Architecture
        </Link>
        <a href={repositoryUrl} className={footerLinkClass}>
          Source
        </a>
        <a href={`${repositoryUrl}/blob/main/SECURITY.md`} className={footerLinkClass}>
          Security policy
        </a>
      </nav>
    </footer>
  );
}
