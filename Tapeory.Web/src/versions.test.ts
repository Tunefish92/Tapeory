import { describe, expect, it } from "vitest";
import { compareVersions } from "./versions";

describe("compareVersions", () => {
  it.each([
    ["0.6.1", "0.6.0", 1],
    ["0.10.0", "0.9.9", 1],
    ["1.0.0", "0.99.0", 1],
    ["0.6.0", "0.6.0", 0],
    ["v0.6.0", "0.6.0", 0],
    ["0.6.0", "0.6.0-beta.3", 1],
    ["0.6.0-beta.10", "0.6.0-beta.2", 1],
    ["0.6.0-rc.1", "0.6.0-beta.9", 1],
    ["0.5.0", "0.6.0-beta.1", -1],
  ])("%s against %s", (a, b, expected) => {
    expect(Math.sign(compareVersions(a, b))).toBe(expected);
  });
});
