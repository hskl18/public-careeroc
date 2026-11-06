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
    "border-[var(--red-strong)] bg-[var(--red)] text-[var(--on-accent)]",
  Closed: "bg-[var(--surface-overlay)] text-[var(--text-tertiary)]",
};

export function StatusPill({ status }: StatusPillProps) {
  return (
    <span
      className={`badge ${toneMap[status] ?? "bg-[var(--surface-overlay)] text-[var(--text-secondary)]"}`}
    >
      {formatEnumLabel(status)}
    </span>
  );
}
