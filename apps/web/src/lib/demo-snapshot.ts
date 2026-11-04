import "server-only";

import type { DashboardResponse } from "./dashboard";
import { demoReadOnlyMessage } from "./demo";
import recording from "./demo-snapshot.json";

type RecordedResponse = { status: number; body: unknown };

const { recordedAt, applicationIdsByRole, responses } = recording as unknown as {
  recordedAt: string;
  applicationIdsByRole: Record<string, string[]>;
  responses: Record<string, RecordedResponse>;
};

const dayMs = 86_400_000;
const numericDate =
  /\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?|(?<![\d:-])\d{4}-\d{2}-\d{2}(?![\d-])/g;
// Covers "November 7, 2026" in email bodies and "Nov 7" in priority reasons.
const textDate =
  /\b(Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?) (\d{1,2})(, \d{4})?\b/g;

// Runs over the serialized text so it also reaches dates in email bodies and in JSON nested inside
// strings. Whole days keep every age and deadline the same distance from today as when recorded.
function shiftDates(json: string) {
  const days = Math.max(0, Math.floor((Date.now() - Date.parse(recordedAt)) / dayMs));
  if (days === 0) {
    return json;
  }

  const shifted = json.replace(numericDate, (match) => {
    const date = new Date(match);
    if (Number.isNaN(date.getTime())) {
      return match;
    }

    date.setUTCDate(date.getUTCDate() + days);
    return match.includes("T") ? date.toISOString() : date.toISOString().slice(0, 10);
  });

  return shifted.replace(textDate, (match, month: string, day: string, year?: string) => {
    const date = new Date(`${month} ${day}${year ?? `, ${recordedAt.slice(0, 4)}`} UTC`);
    if (Number.isNaN(date.getTime())) {
      return match;
    }

    date.setUTCDate(date.getUTCDate() + days);
    return date.toLocaleDateString("en-US", {
      month: month.length > 3 ? "long" : "short",
      day: "numeric",
      year: year ? "numeric" : undefined,
      timeZone: "UTC",
    });
  });
}

function respond({ status, body }: RecordedResponse) {
  return new Response(body === null ? null : shiftDates(JSON.stringify(body)), {
    status,
    headers: { "content-type": "application/json", "cache-control": "no-store" },
  });
}

function contains(value: string, filter: string) {
  return value.toLowerCase().includes(filter.toLowerCase());
}

// Follows DashboardQueryService.MatchesDashboardFilters: metrics stay global, and actions and activity
// follow the visible applications.
// shortcut: source and location filters are ignored because no demo link sets them, add them if one does.
function dashboardResponse(searchParams: URLSearchParams) {
  const recorded =
    responses[`GET /api/dashboard?view=${searchParams.get("view")}`] ??
    responses["GET /api/dashboard?view=all"];
  const q = searchParams.get("q")?.trim();
  const company = searchParams.get("company")?.trim();
  const role = searchParams.get("roleCanonical")?.trim();
  if (!q && !company && !role) {
    return respond(recorded);
  }

  const dashboard = recorded.body as DashboardResponse;
  const roleIds = role && Object.hasOwn(applicationIdsByRole, role) ? applicationIdsByRole[role] : undefined;
  const applications = dashboard.applications.filter(
    (application) =>
      (!company || contains(application.company, company)) &&
      (!role || (roleIds ? roleIds.includes(application.id) : contains(application.role, role))) &&
      (!q ||
        contains(application.company, q) ||
        contains(application.role, q) ||
        contains(application.status, q)),
  );
  const visibleIds = new Set(applications.map((application) => application.id));

  return respond({
    status: 200,
    body: {
      ...dashboard,
      applications,
      pendingActions: dashboard.pendingActions.filter((action) => visibleIds.has(action.jobApplicationId)),
      recentActivity: dashboard.recentActivity.filter((activity) => visibleIds.has(activity.jobApplicationId)),
    },
  });
}

export function demoSnapshotResponse(method: string, pathAndQuery: string) {
  const { pathname, searchParams } = new URL(pathAndQuery, "http://snapshot");
  if (method === "GET" && pathname === "/api/dashboard") {
    return dashboardResponse(searchParams);
  }

  // The fictional account has one mail source, so the provider filter cannot narrow anything.
  const range = searchParams.get("range");
  const key =
    method === "GET" && pathname === "/api/analytics"
      ? `GET /api/analytics${range && range !== "all" ? `?range=${range}` : ""}`
      : `${method} ${pathAndQuery}`;

  const recorded = responses[key];
  if (recorded) {
    return respond(recorded);
  }
  if (method !== "GET") {
    return respond({ status: 403, body: { error: demoReadOnlyMessage } });
  }

  console.warn(`[demo-snapshot] miss ${key}`);
  return respond({ status: 404, body: { error: "Not part of the hosted demo." } });
}
