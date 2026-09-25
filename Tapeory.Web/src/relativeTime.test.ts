import { describe, expect, it } from "vitest";
import { formatRelativeTime } from "./relativeTime";

const now = new Date("2026-09-23T12:00:00Z");
const ago = (seconds: number) => new Date(now.getTime() - seconds * 1000);

describe("formatRelativeTime", () => {
  it("says 'now' for the last minute", () => {
    expect(formatRelativeTime(ago(20), "en", now)).toBe("now");
  });

  it("picks the largest fitting unit", () => {
    expect(formatRelativeTime(ago(5 * 60), "en", now)).toBe("5 minutes ago");
    expect(formatRelativeTime(ago(3 * 3600), "en", now)).toBe("3 hours ago");
    expect(formatRelativeTime(ago(24 * 3600), "en", now)).toBe("yesterday");
    expect(formatRelativeTime(ago(14 * 24 * 3600), "en", now)).toBe("2 weeks ago");
    expect(formatRelativeTime(ago(400 * 24 * 3600), "en", now)).toBe("last year");
  });

  it("accepts ISO strings and follows the UI language", () => {
    expect(formatRelativeTime(ago(2 * 3600).toISOString(), "de", now)).toBe("vor 2 Stunden");
  });
});
