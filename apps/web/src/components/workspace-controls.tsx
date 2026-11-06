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
      <span className="min-w-0 truncate">{label}</span>
      <span className="metric-tile-value" style={{ color: tone }}>
        {value}
      </span>
    </>
  );
  const classes = `metric-tile ${
    selected
      ? "border-[var(--accent)] ring-1 ring-[var(--accent)]"
      : highlighted
        ? "ring-1 ring-[var(--accent-soft)]"
        : ""
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
