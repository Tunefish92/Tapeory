import { describe, expect, it } from "vitest";
import type { PrintJobItemResponse } from "../api/printJobs";
import { isPrintJobSortKey, labelCount, sortPrintJobs } from "./printJobSort";

/** Items with the given quantities. */
function items(...quantities: number[]): PrintJobItemResponse[] {
  return quantities.map((quantity, index) => ({
    id: index + 1,
    fieldValues: {},
    quantity,
    status: "Completed",
    errorMessage: null,
    previewUrl: null,
  }));
}

function job(id: number, overrides: Partial<Parameters<typeof sortPrintJobs>[0][number]> = {}) {
  return {
    id,
    templateName: "Label",
    printerName: null as string | null,
    status: "Completed",
    createdAt: `2026-01-0${id}T00:00:00Z`,
    items: [] as PrintJobItemResponse[],
    ...overrides,
  };
}

const ids = (jobs: { id: number }[]) => jobs.map((item) => item.id);

describe("labelCount", () => {
  it("adds up the quantity of every item", () => {
    expect(labelCount({ items: items(3, 2) })).toBe(5);
    expect(labelCount({ items: [] })).toBe(0);
  });
});

describe("sortPrintJobs", () => {
  it("sorts by job number and by creation time", () => {
    const jobs = [job(2), job(3), job(1)];

    expect(ids(sortPrintJobs(jobs, "job", "desc"))).toEqual([3, 2, 1]);
    expect(ids(sortPrintJobs(jobs, "created", "asc"))).toEqual([1, 2, 3]);
  });

  it("sorts by total labels", () => {
    const jobs = [
      job(1, { items: items(10) }),
      job(2, { items: items(2, 3) }),
      job(3, { items: items(40) }),
    ];

    expect(ids(sortPrintJobs(jobs, "labels", "desc"))).toEqual([3, 1, 2]);
  });

  it("keeps jobs without a printer last in both directions", () => {
    const jobs = [job(1, { printerName: null }), job(2, { printerName: "Office" }), job(3, { printerName: "Garage" })];

    expect(ids(sortPrintJobs(jobs, "printer", "asc"))).toEqual([3, 2, 1]);
    expect(ids(sortPrintJobs(jobs, "printer", "desc"))).toEqual([2, 3, 1]);
  });

  it("sorts template names naturally and breaks ties newest first", () => {
    const jobs = [job(1, { templateName: "Jar 10" }), job(2, { templateName: "jar 2" }), job(3, { templateName: "Jar 2" })];

    expect(ids(sortPrintJobs(jobs, "template", "asc"))).toEqual([3, 2, 1]);
  });
});

describe("isPrintJobSortKey", () => {
  it("accepts only known columns", () => {
    expect(isPrintJobSortKey("labels")).toBe(true);
    expect(isPrintJobSortKey("name")).toBe(false);
    expect(isPrintJobSortKey(null)).toBe(false);
  });
});
