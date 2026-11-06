// Categories arrive as snake_case ("oa_invitation") and statuses as split enum names ("Oa Pending");
// both read as title-cased words, with OA kept as an acronym.
export function formatEnumLabel(value: string | null): string {
  if (!value) return "Unclassified";
  return value
    .split(/[_ ]/)
    .map((part) => (part.toLowerCase() === "oa" ? "OA" : part.charAt(0).toUpperCase() + part.slice(1)))
    .join(" ");
}

// Confidence everywhere reads as a percentage, so a score means the same on every page.
export function formatConfidence(value: number | null) {
  return value === null || !Number.isFinite(value) ? "n/a" : `${Math.round(value * 100)}%`;
}
