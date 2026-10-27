"use client";

import { useEffect, useId, useMemo, useState } from "react";

type AdminTableShellProps = {
  eyebrow: string;
  title: string;
  count?: number;
  description?: string;
  searchPlaceholder?: string;
  children: React.ReactNode;
};

export function AdminTableShell({
  eyebrow,
  title,
  count,
  description,
  searchPlaceholder = "Filter rows",
  children,
}: AdminTableShellProps) {
  const tableId = useId();
  const [query, setQuery] = useState("");
  const [sortLabel, setSortLabel] = useState<string | null>(null);
  const normalizedQuery = query.trim().toLowerCase();
  const sortStatus = useMemo(
    () => (sortLabel ? `Sorted by ${sortLabel}.` : "Click a column header to sort this table."),
    [sortLabel],
  );

  useEffect(() => {
    const root = document.querySelector<HTMLElement>(
      `[data-admin-table="${CSS.escape(tableId)}"]`,
    );
    if (!root) {
      return;
    }

    root.querySelectorAll<HTMLElement>("[data-admin-row]").forEach((row) => {
      const text = row.dataset.adminSearch ?? row.textContent ?? "";
      row.hidden = Boolean(normalizedQuery) && !text.toLowerCase().includes(normalizedQuery);
    });
  }, [normalizedQuery, tableId]);

  useEffect(() => {
    const root = document.querySelector<HTMLElement>(
      `[data-admin-table="${CSS.escape(tableId)}"]`,
    );
    const table = root?.querySelector("table");
    const tbody = table?.querySelector("tbody");
    const headers = table
      ? Array.from(table.querySelectorAll<HTMLTableCellElement>("thead th"))
      : [];

    if (!root || !table || !tbody || headers.length === 0) {
      return;
    }

    let sortState: { index: number; direction: "ascending" | "descending" } | null = null;

    const cleanupCallbacks = headers.map((header, index) => {
      const headerText = normalizeCellText(header.textContent);
      if (!headerText) {
        return () => {};
      }

      header.tabIndex = 0;
      header.setAttribute("role", "button");
      header.setAttribute("aria-sort", "none");
      header.title = `Sort by ${headerText}`;
      header.classList.add("cursor-pointer", "select-none");

      const sortRows = () => {
        const rows = Array.from(tbody.querySelectorAll<HTMLTableRowElement>("tr[data-admin-row]"));
        if (rows.length < 2) {
          return;
        }

        const direction =
          sortState?.index === index && sortState.direction === "ascending"
            ? "descending"
            : "ascending";
        sortState = { index, direction };

        headers.forEach((candidate) => candidate.setAttribute("aria-sort", "none"));
        header.setAttribute("aria-sort", direction);

        const multiplier = direction === "ascending" ? 1 : -1;
        rows
          .map((row, originalIndex) => ({ row, originalIndex }))
          .sort((left, right) => {
            const comparison = compareAdminCellValues(
              left.row.children[index]?.textContent ?? "",
              right.row.children[index]?.textContent ?? "",
            );
            return comparison === 0
              ? left.originalIndex - right.originalIndex
              : comparison * multiplier;
          })
          .forEach(({ row }) => tbody.appendChild(row));

        setSortLabel(`${headerText} ${direction === "ascending" ? "ascending" : "descending"}`);
      };

      const onKeyDown = (event: KeyboardEvent) => {
        if (event.key !== "Enter" && event.key !== " ") {
          return;
        }

        event.preventDefault();
        sortRows();
      };

      header.addEventListener("click", sortRows);
      header.addEventListener("keydown", onKeyDown);

      return () => {
        header.removeEventListener("click", sortRows);
        header.removeEventListener("keydown", onKeyDown);
      };
    });

    return () => {
      cleanupCallbacks.forEach((cleanup) => cleanup());
    };
  }, [tableId]);

  return (
    <section className="card overflow-hidden p-5" data-admin-table={tableId}>
      <div className="mb-4 flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
        <div className="min-w-0">
          <p className="eyebrow">{eyebrow}</p>
          <h2 className="mt-1 text-2xl font-semibold">{title}</h2>
          {description ? (
            <p className="mt-2 max-w-3xl text-sm leading-6 text-[var(--text-secondary)]">
              {description}
            </p>
          ) : null}
        </div>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
          {typeof count === "number" ? (
            <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
              {count.toLocaleString()} shown
            </span>
          ) : null}
          <input
            type="search"
            className="input h-9 min-w-0 sm:w-60"
            value={query}
            onChange={(event) => setQuery(event.currentTarget.value)}
            placeholder={searchPlaceholder}
            aria-label={`Filter ${title}`}
          />
        </div>
      </div>
      <p className="sr-only" aria-live="polite">
        {sortStatus}
      </p>
      <div className="overflow-x-auto">{children}</div>
    </section>
  );
}

function compareAdminCellValues(left: string, right: string) {
  const leftText = normalizeCellText(left);
  const rightText = normalizeCellText(right);
  const leftNumber = parseAdminNumber(leftText);
  const rightNumber = parseAdminNumber(rightText);

  if (leftNumber !== null && rightNumber !== null) {
    return leftNumber - rightNumber;
  }

  return leftText.localeCompare(rightText, undefined, {
    numeric: true,
    sensitivity: "base",
  });
}

function normalizeCellText(value: string | null | undefined) {
  return (value ?? "").replace(/\s+/g, " ").trim();
}

function parseAdminNumber(value: string) {
  const match = value.match(/-?\$?\d[\d,]*(?:\.\d+)?/);
  if (!match) {
    return null;
  }

  const parsed = Number(match[0].replace(/[$,]/g, ""));
  return Number.isFinite(parsed) ? parsed : null;
}
