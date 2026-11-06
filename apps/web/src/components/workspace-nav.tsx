"use client";

import type { Route } from "next";
import Link from "next/link";
import { usePathname } from "next/navigation";

type NavItem = { href: Route; label: string };

const navigation: NavItem[] = [
  { href: "/app", label: "Dashboard" },
  { href: "/app/applications", label: "Applications" },
  { href: "/app/analytics", label: "Analytics" },
  { href: "/app/resume", label: "Resume" },
];

function isActive(pathname: string, href: string) {
  if (href === "/app") {
    return pathname === href;
  }

  return pathname === href || pathname.startsWith(`${href}/`);
}

type WorkspaceNavProps = {
  className?: string;
  mode?: "header" | "bottom";
  items?: NavItem[];
  label?: string;
};

export function WorkspaceNav({
  className,
  mode = "header",
  items = navigation,
  label = "Workspace",
}: WorkspaceNavProps) {
  const pathname = usePathname();
  const isBottom = mode === "bottom";

  const nav = (
    <nav
      className={
        isBottom
          ? `grid w-full min-w-0 auto-cols-fr grid-flow-col gap-1 ${className ?? ""}`
          : `hidden gap-5 sm:inline-flex sm:w-auto lg:gap-6 ${className ?? ""}`
      }
      aria-label={label}
    >
      {items.map((item) => {
        const active = isActive(pathname, item.href);

        return (
          <Link
            key={item.href}
            href={item.href}
            className={`relative min-w-0 px-1 text-center font-semibold transition-colors ${
              isBottom ? "py-2 text-xs" : "py-1.5 text-xs"
            } ${
              active
                ? "text-[var(--text-primary)]"
                : "text-[var(--text-secondary)] hover:text-[var(--text-primary)]"
            }`}
            aria-current={active ? "page" : undefined}
          >
            {item.label}
            {active && (
              <span
                className={`absolute left-1/2 h-[2px] w-4 -translate-x-1/2 rounded-full bg-[var(--accent)] ${
                  isBottom ? "bottom-0" : "bottom-[-0.25rem]"
                }`}
              />
            )}
          </Link>
        );
      })}
    </nav>
  );

  return isBottom ? (
    <div className="fixed inset-x-0 bottom-0 z-50 min-w-0 max-w-full overflow-hidden border-t border-[var(--border)] bg-[var(--bg)] px-3 pb-[calc(env(safe-area-inset-bottom)+0.35rem)] pt-1.5 sm:hidden">
      {nav}
    </div>
  ) : (
    nav
  );
}
