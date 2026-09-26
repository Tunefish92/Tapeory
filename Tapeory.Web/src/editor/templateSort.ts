import type { TemplateSummaryResponse } from "../api/templates";
import { nextGridSort, type GridSort, type SortDirection } from "../components/DataGrid";

export type SortKey = "name" | "group" | "status" | "source" | "size" | "updated";
export type { SortDirection };
export type TemplateSort = GridSort<SortKey>;

/** Newest first is almost always what you want for dates. */
export const TEMPLATE_SORT_DESCENDING_FIRST: readonly SortKey[] = ["updated"];

export const SORT_KEYS: readonly SortKey[] = ["name", "group", "status", "source", "size", "updated"];

export function isSortKey(value: string | null): value is SortKey {
  return value !== null && (SORT_KEYS as readonly string[]).includes(value);
}

type Sortable = Pick<
  TemplateSummaryResponse,
  "id" | "name" | "category" | "status" | "sourceLbxUrl" | "widthMm" | "heightMm" | "updatedAt"
>;

/**
 * Returns a sorted copy. Templates without a group always sink to the bottom whichever way
 * the group column is sorted, and ties fall back to name then id so the order never jumps.
 */
export function sortTemplates<T extends Sortable>(
  templates: readonly T[],
  key: SortKey,
  direction: SortDirection,
  locale?: string,
): T[] {
  const collator = new Intl.Collator(locale, { sensitivity: "base", numeric: true });
  const flip = direction === "asc" ? 1 : -1;

  const compareByKey = (a: T, b: T): number => {
    switch (key) {
      case "name":
        return collator.compare(a.name, b.name);
      case "group":
        return collator.compare(a.category?.trim() ?? "", b.category?.trim() ?? "");
      case "status":
        return collator.compare(a.status, b.status);
      case "source":
        return Number(!!a.sourceLbxUrl) - Number(!!b.sourceLbxUrl);
      case "size":
        // Height first: on tape printers it's the tape width, the thing people group by.
        return a.heightMm - b.heightMm || a.widthMm - b.widthMm;
      case "updated":
        return Date.parse(a.updatedAt) - Date.parse(b.updatedAt);
    }
  };

  return [...templates].sort((a, b) => {
    if (key === "group") {
      const aEmpty = !a.category?.trim();
      const bEmpty = !b.category?.trim();
      if (aEmpty !== bEmpty) return aEmpty ? 1 : -1;
    }

    return flip * compareByKey(a, b) || collator.compare(a.name, b.name) || a.id - b.id;
  });
}

export function nextSort(current: TemplateSort | null, key: SortKey): TemplateSort {
  return nextGridSort(current, key, TEMPLATE_SORT_DESCENDING_FIRST);
}
