const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ["year", 365 * 24 * 3600],
  ["month", 30 * 24 * 3600],
  ["week", 7 * 24 * 3600],
  ["day", 24 * 3600],
  ["hour", 3600],
  ["minute", 60],
];

/** "3 hours ago", "yesterday", "just now" — in the given UI language. */
export function formatRelativeTime(date: Date | string, locale?: string, now: Date = new Date()): string {
  const then = typeof date === "string" ? new Date(date) : date;
  const seconds = (then.getTime() - now.getTime()) / 1000;
  const format = new Intl.RelativeTimeFormat(locale, { numeric: "auto" });

  for (const [unit, size] of UNITS) {
    if (Math.abs(seconds) >= size) {
      return format.format(Math.round(seconds / size), unit);
    }
  }

  return format.format(0, "second");
}
