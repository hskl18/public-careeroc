"use client";

import type { Route } from "next";
import Link from "next/link";
import { usePathname } from "next/navigation";

const navigation: { href: Route; label: string }[] = [
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
};

export function WorkspaceNav({ className, mode = "header" }: WorkspaceNavProps) {
  const pathname = usePathname();
  const isBottom = mode === "bottom";

  return (
    <nav
      className={
        isBottom
          ? `grid w-full min-w-0 grid-cols-4 gap-1 ${className ?? ""}`
          : `hidden gap-5 sm:inline-flex sm:w-auto lg:gap-6 ${className ?? ""}`
      }
      aria-label="Workspace"
    >
      {navigation.map((item) => {
        const active = isActive(pathname, item.href);

        return (
          <Link
            key={item.href}
            href={item.href}
            className={`relative min-w-0 px-1 text-center font-semibold transition-colors ${
              isBottom ? "py-2 text-[10px]" : "py-1.5 text-xs"
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
}
