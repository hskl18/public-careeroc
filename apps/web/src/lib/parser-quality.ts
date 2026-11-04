// Frontend heuristics that flag dashboard records that look like parser
// errors. The backend is expected to replace these with a real
// parserConfidence / requiresReview signal.

const NOISE_PATTERNS: RegExp[] = [
  /^https?\b/i,
  /^www\./i,
  /^the\s/i,
  /^this\s/i,
  /^a\s/i,
  /^an\s/i,
  /\bposition\b/i,
  /\bintern(ship)?\b/i,
  /\bopportunity\b/i,
];

const PERSON_NAME_PATTERN =
  /^[A-Z][a-z]+(?:[-'][A-Z][a-z]+)?(?:\s+[A-Z][a-z]+(?:[-'][A-Z][a-z]+)?){1,2}$/;

const KNOWN_BUSINESS_TOKENS = new Set([
  "inc",
  "llc",
  "ltd",
  "corp",
  "corporation",
  "labs",
  "systems",
  "studios",
  "group",
  "co",
  "company",
  "technologies",
  "tech",
  "ai",
  "io",
  "solutions",
  "consulting",
  "partners",
  "capital",
]);

function looksLikePersonName(value: string) {
  const trimmed = value.trim();
  if (!PERSON_NAME_PATTERN.test(trimmed)) return false;
  const tokens = trimmed.toLowerCase().split(/\s+/);
  return !tokens.some((token) => KNOWN_BUSINESS_TOKENS.has(token));
}

export function looksLikeParserError(company: string | null | undefined) {
  const value = (company ?? "").trim();
  if (!value) return true;
  if (value.toLowerCase() === "unknown company") return true;
  if (value.length > 40) return true;
  if (value.split(/\s+/).length >= 4) return true;
  if (NOISE_PATTERNS.some((pattern) => pattern.test(value))) return true;
  if (looksLikePersonName(value)) return true;
  return false;
}

export function hasUnknownRole(role: string | null | undefined) {
  const value = (role ?? "").trim().toLowerCase();
  return value === "" || value === "unknown role";
}
