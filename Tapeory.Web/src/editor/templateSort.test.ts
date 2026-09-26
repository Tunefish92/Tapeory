import { describe, expect, it } from "vitest";
import { isSortKey, nextSort, sortTemplates } from "./templateSort";

function template(id: number, overrides: Partial<Parameters<typeof sortTemplates>[0][number]> = {}) {
  return {
    id,
    name: `T${id}`,
    category: null as string | null,
    status: "Draft",
    sourceLbxUrl: null as string | null,
    widthMm: 50,
    heightMm: 25,
    updatedAt: "2026-01-01T00:00:00Z",
    ...overrides,
  };
}

const names = (items: { name: string }[]) => items.map((item) => item.name);

describe("sortTemplates", () => {
  it("sorts names naturally and case-insensitively", () => {
    const items = [template(1, { name: "label 10" }), template(2, { name: "Label 2" }), template(3, { name: "apple" })];

    expect(names(sortTemplates(items, "name", "asc"))).toEqual(["apple", "Label 2", "label 10"]);
    expect(names(sortTemplates(items, "name", "desc"))).toEqual(["label 10", "Label 2", "apple"]);
  });

  it("keeps ungrouped templates last in both directions", () => {
    const items = [
      template(1, { name: "A", category: null }),
      template(2, { name: "B", category: "Marmelade" }),
      template(3, { name: "C", category: "Cables" }),
      template(4, { name: "D", category: "  " }),
    ];

    expect(names(sortTemplates(items, "group", "asc"))).toEqual(["C", "B", "A", "D"]);
    expect(names(sortTemplates(items, "group", "desc"))).toEqual(["B", "C", "A", "D"]);
  });

  it("sorts size by height, then width", () => {
    const items = [
      template(1, { name: "wide 12", widthMm: 90, heightMm: 12 }),
      template(2, { name: "tall", widthMm: 30, heightMm: 24 }),
      template(3, { name: "narrow 12", widthMm: 40, heightMm: 12 }),
      template(4, { name: "tiny", widthMm: 60, heightMm: 9 }),
    ];

    expect(names(sortTemplates(items, "size", "asc"))).toEqual(["tiny", "narrow 12", "wide 12", "tall"]);
  });

  it("sorts by update time and by source", () => {
    const items = [
      template(1, { name: "old", updatedAt: "2026-01-01T00:00:00Z", sourceLbxUrl: "/x.lbx" }),
      template(2, { name: "new", updatedAt: "2026-06-01T00:00:00Z" }),
    ];

    expect(names(sortTemplates(items, "updated", "desc"))).toEqual(["new", "old"]);
    expect(names(sortTemplates(items, "source", "asc"))).toEqual(["new", "old"]);
  });

  it("breaks ties by name so equal values keep a stable order", () => {
    const items = [template(1, { name: "b" }), template(2, { name: "a" })];

    expect(names(sortTemplates(items, "status", "desc"))).toEqual(["a", "b"]);
  });

  it("does not mutate its input", () => {
    const items = [template(1, { name: "b" }), template(2, { name: "a" })];
    sortTemplates(items, "name", "asc");

    expect(names(items)).toEqual(["b", "a"]);
  });
});

describe("isSortKey", () => {
  it("accepts only known columns", () => {
    expect(isSortKey("size")).toBe(true);
    expect(isSortKey("id")).toBe(false);
    expect(isSortKey(null)).toBe(false);
  });
});

describe("nextSort", () => {
  it("flips the sorted column and starts new columns ascending, dates newest first", () => {
    expect(nextSort({ key: "name", direction: "asc" }, "name")).toEqual({ key: "name", direction: "desc" });
    expect(nextSort({ key: "name", direction: "desc" }, "name")).toEqual({ key: "name", direction: "asc" });
    expect(nextSort({ key: "name", direction: "desc" }, "size")).toEqual({ key: "size", direction: "asc" });
    expect(nextSort(null, "updated")).toEqual({ key: "updated", direction: "desc" });
  });
});
