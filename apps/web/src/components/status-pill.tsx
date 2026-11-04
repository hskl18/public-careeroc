import type { CSSProperties } from "react";
import { formatEnumLabel } from "@/lib/labels";

type StatusPillProps = {
  status: string;
};

const toneMap: Record<string, string> = {
  Applied: "bg-[var(--surface-overlay)] text-[var(--text-secondary)]",
  "Recruiter Contact": "bg-[var(--accent-soft)] text-[var(--accent)]",
  "Oa Pending": "bg-[var(--blue-soft)] text-[var(--brand-blue-ink)]",
  Interviewing: "bg-[var(--blue-soft)] text-[var(--blue)]",
  Offer: "bg-[var(--green-soft)] text-[var(--green)]",
  Rejected:
    "border-[var(--red-strong)] bg-[var(--red)] text-white shadow-[0_8px_18px_rgba(37,99,235,0.18)]",
  Closed: "bg-[var(--surface-overlay)] text-[var(--text-tertiary)]",
};

const styleMap: Record<string, CSSProperties> = {
  Rejected: {
    backgroundColor: "var(--red)",
    borderColor: "var(--red-strong)",
    boxShadow: "0 8px 18px rgba(37, 99, 235, 0.18)",
    color: "#ffffff",
  },
};

export function StatusPill({ status }: StatusPillProps) {
  return (
    <span
      className={`badge ${toneMap[status] ?? "bg-[var(--surface-overlay)] text-[var(--text-secondary)]"}`}
      style={styleMap[status]}
    >
      {formatEnumLabel(status)}
    </span>
  );
}
