"use client";

import { useHydrated } from "@/lib/use-hydrated";

const formats = {
  dateTime: { month: "short", day: "numeric", year: "numeric", hour: "numeric", minute: "2-digit" },
  shortDateTime: { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" },
  date: { month: "short", day: "numeric", year: "numeric" },
} satisfies Record<string, Intl.DateTimeFormatOptions>;

// A timestamp in the viewer's time zone, which the server cannot know.
export function LocalTime({
  value,
  format = "dateTime",
}: {
  value: string;
  format?: keyof typeof formats;
}) {
  const hydrated = useHydrated();
  return (
    <time dateTime={value}>
      {hydrated ? new Intl.DateTimeFormat("en-US", formats[format]).format(new Date(value)) : null}
    </time>
  );
}
