import type { PrintJobResponse } from "../api/printJobs";

export type PrintJobSortKey = "job" | "template" | "printer" | "labels" | "status" | "created";

const KEYS: readonly PrintJobSortKey[] = ["job", "template", "printer", "labels", "status", "created"];

/** Newest job, most labels and latest date first on the first click. */
export const PRINT_JOB_SORT_DESCENDING_FIRST: readonly PrintJobSortKey[] = ["job", "labels", "created"];

export function isPrintJobSortKey(value: string | null): value is PrintJobSortKey {
  return value !== null && (KEYS as readonly string[]).includes(value);
}

/** Total labels printed by a job: every item's quantity added up. */
export function labelCount(job: Pick<PrintJobResponse, "items">): number {
  return job.items.reduce((sum, item) => sum + item.quantity, 0);
}

type Sortable = Pick<PrintJobResponse, "id" | "templateName" | "printerName" | "status" | "createdAt" | "items">;

/**
 * Returns a sorted copy. Jobs without a printer always sink to the bottom of the printer
 * sort, and ties fall back to newest job first so the order never jumps.
 */
export function sortPrintJobs<T extends Sortable>(
  jobs: readonly T[],
  key: PrintJobSortKey,
  direction: "asc" | "desc",
  locale?: string,
): T[] {
  const collator = new Intl.Collator(locale, { sensitivity: "base", numeric: true });
  const flip = direction === "asc" ? 1 : -1;

  const compareByKey = (a: T, b: T): number => {
    switch (key) {
      case "job":
        return a.id - b.id;
      case "template":
        return collator.compare(a.templateName, b.templateName);
      case "printer":
        return collator.compare(a.printerName ?? "", b.printerName ?? "");
      case "labels":
        return labelCount(a) - labelCount(b);
      case "status":
        return collator.compare(a.status, b.status);
      case "created":
        return Date.parse(a.createdAt) - Date.parse(b.createdAt);
    }
  };

  return [...jobs].sort((a, b) => {
    if (key === "printer" && !a.printerName !== !b.printerName) {
      return a.printerName ? -1 : 1;
    }

    return flip * compareByKey(a, b) || b.id - a.id;
  });
}
