import type { ReactNode } from "react";

export function WorkspaceMetricTile({
  label,
  value,
  tone = "var(--text-primary)",
  selected = false,
  highlighted = false,
  className = "",
  onClick,
}: {
  label: string;
  value: ReactNode;
  tone?: string;
  selected?: boolean;
  highlighted?: boolean;
  className?: string;
  onClick?: () => void;
}) {
  const content = (
    <>
      <p className="eyebrow text-[0.64rem] sm:text-[0.72rem]">{label}</p>
      <p
        className="mt-0.5 text-base font-semibold tabular-nums sm:mt-1 sm:text-lg"
        style={{ color: tone }}
      >
        {value}
      </p>
    </>
  );
  const classes = `rounded-md border px-3 py-2 text-left transition sm:py-2.5 ${
    selected
      ? "border-[var(--accent)] bg-[var(--surface-strong)] ring-2 ring-[var(--accent)]"
      : highlighted
        ? "border-[var(--accent-soft)] bg-[rgba(255,255,255,0.42)] ring-1 ring-[var(--accent-soft)] hover:bg-[rgba(255,255,255,0.62)]"
        : "border-[var(--border)] bg-[rgba(255,255,255,0.36)] hover:bg-[rgba(255,255,255,0.55)]"
  } ${className}`;

  if (!onClick) {
    return <div className={classes}>{content}</div>;
  }

  return (
    <button type="button" onClick={onClick} aria-pressed={selected} className={classes}>
      {content}
    </button>
  );
}

export function SortableHeaderButton<TColumn extends string>({
  column,
  label,
  activeColumn,
  direction,
  align = "left",
  onSort,
}: {
  column: TColumn;
  label: string;
  activeColumn: TColumn | null;
  direction: "asc" | "desc";
  align?: "left" | "right";
  onSort: (column: TColumn) => void;
}) {
  const active = activeColumn === column;
  const nextDirection =
    active && direction === "desc" ? "small to large" : "important first";
  const arrow = active ? (direction === "desc" ? "↓" : "↑") : "↕";

  return (
    <button
      type="button"
      className={`eyebrow inline-flex items-center gap-1.5 hover:text-[var(--text-primary)] ${
        align === "right" ? "justify-end text-right" : "justify-start text-left"
      }`}
      aria-pressed={active}
      aria-label={`Sort ${label} ${nextDirection}`}
      title={`Sort ${label} ${nextDirection}`}
      onClick={() => onSort(column)}
    >
      <span>{label}</span>
      <span
        aria-hidden="true"
        className={active ? "text-[var(--accent)]" : "text-[var(--text-tertiary)]"}
      >
        {arrow}
      </span>
    </button>
  );
}
