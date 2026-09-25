import { describe, expect, it } from "vitest";
import { collectGroups, groupHue, groupKey } from "./groups";

describe("groupKey", () => {
  it("ignores case, accents and surrounding whitespace", () => {
    expect(groupKey("  Étiketten ")).toBe(groupKey("etiketten"));
  });
});

describe("collectGroups", () => {
  it("counts templates per group, merging spellings, sorted by name and skipping ungrouped", () => {
    const groups = collectGroups([
      { category: "Kabel" },
      { category: "Marmelade" },
      { category: "kabel" },
      { category: null },
      { category: "  " },
    ]);

    expect(groups).toEqual([
      { key: "kabel", name: "Kabel", count: 2 },
      { key: "marmelade", name: "Marmelade", count: 1 },
    ]);
  });
});

describe("groupHue", () => {
  it("is stable per group regardless of spelling, and within the color wheel", () => {
    expect(groupHue("Cables")).toBe(groupHue("  cables"));
    expect(groupHue("Cables")).toBeGreaterThanOrEqual(0);
    expect(groupHue("Cables")).toBeLessThan(360);
  });

  it("usually differs between groups", () => {
    const hues = new Set(["Marmelade", "Kabel", "Lager", "Gewürze", "Werkstatt"].map(groupHue));
    expect(hues.size).toBeGreaterThan(3);
  });
});
