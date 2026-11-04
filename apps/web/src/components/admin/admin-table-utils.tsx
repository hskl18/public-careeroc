export function EmptyTableRow({
  colSpan,
  message,
}: {
  colSpan: number;
  message: string;
}) {
  return (
    <tr>
      <td className="py-4 pr-4 text-[var(--text-secondary)]" colSpan={colSpan}>
        {message}
      </td>
    </tr>
  );
}

export function formatUtcTimestamp(value: string | null) {
  if (!value) {
    return "Never";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "Invalid date";
  }

  const formatted = new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    timeZone: "UTC",
    hour12: false,
  }).format(date);

  return `${formatted}Z`;
}

export function buildAdminSearchText(values: Array<string | number | null | undefined>) {
  return values
    .filter((value): value is string | number => value !== null && value !== undefined)
    .map((value) => String(value).toLowerCase())
    .join(" ");
}
