const timestampFormatterOptions = {
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
} as const;

const timeZoneFormatterCache = new Map<string, Intl.DateTimeFormat>();

type ZonedDateTimeParts = {
  year: number;
  month: number;
  day: number;
  hour: number;
  minute: number;
  second: number;
};

function getTimeZoneFormatter(timeZoneId: string) {
  const cached = timeZoneFormatterCache.get(timeZoneId);
  if (cached) {
    return cached;
  }

  const formatter = new Intl.DateTimeFormat("en-US", {
    timeZone: timeZoneId,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hourCycle: "h23",
  });
  timeZoneFormatterCache.set(timeZoneId, formatter);
  return formatter;
}

function parseZonedDateTimeParts(date: Date, timeZoneId: string): ZonedDateTimeParts {
  const parts = getTimeZoneFormatter(timeZoneId).formatToParts(date);
  const values = {
    year: 0,
    month: 0,
    day: 0,
    hour: 0,
    minute: 0,
    second: 0,
  };

  for (const part of parts) {
    switch (part.type) {
      case "year":
        values.year = Number(part.value);
        break;
      case "month":
        values.month = Number(part.value);
        break;
      case "day":
        values.day = Number(part.value);
        break;
      case "hour":
        values.hour = Number(part.value);
        break;
      case "minute":
        values.minute = Number(part.value);
        break;
      case "second":
        values.second = Number(part.value);
        break;
      default:
        break;
    }
  }

  return values;
}

function padDateTimePart(value: number, length = 2) {
  return value.toString().padStart(length, "0");
}

function getTimeZoneOffsetMs(date: Date, timeZoneId: string) {
  const parts = parseZonedDateTimeParts(date, timeZoneId);
  const utcAtZonedWallClock = Date.UTC(
    parts.year,
    parts.month - 1,
    parts.day,
    parts.hour,
    parts.minute,
    parts.second,
  );

  return utcAtZonedWallClock - date.getTime();
}

export function formatTimestampInTimeZone(
  value: string | null,
  timeZoneId: string,
  fallback: string,
) {
  if (!value) {
    return fallback;
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return fallback;
  }

  return new Intl.DateTimeFormat("en-US", {
    ...timestampFormatterOptions,
    timeZone: timeZoneId,
  }).format(date);
}

export function formatDateTimeLocalInputInTimeZone(
  value: string | null,
  timeZoneId: string,
) {
  if (!value) {
    return "";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "";
  }

  const parts = parseZonedDateTimeParts(date, timeZoneId);
  return `${padDateTimePart(parts.year, 4)}-${padDateTimePart(parts.month)}-${padDateTimePart(parts.day)}T${padDateTimePart(parts.hour)}:${padDateTimePart(parts.minute)}`;
}

export function toUtcIsoStringFromDateTimeLocal(
  value: string,
  timeZoneId: string,
) {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value.trim());
  if (!match) {
    return null;
  }

  const [, yearText, monthText, dayText, hourText, minuteText] = match;
  const year = Number(yearText);
  const month = Number(monthText);
  const day = Number(dayText);
  const hour = Number(hourText);
  const minute = Number(minuteText);

  let utcMs = Date.UTC(year, month - 1, day, hour, minute);

  // Resolve the wall-clock time against the user's configured timezone.
  for (let attempt = 0; attempt < 3; attempt += 1) {
    const offsetMs = getTimeZoneOffsetMs(new Date(utcMs), timeZoneId);
    const nextUtcMs =
      Date.UTC(year, month - 1, day, hour, minute) - offsetMs;

    if (nextUtcMs === utcMs) {
      break;
    }

    utcMs = nextUtcMs;
  }

  return new Date(utcMs).toISOString();
}
