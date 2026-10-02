import { beforeEach, describe, expect, it } from "vitest";
import type { PrintData } from "../api/printData";
import {
  applyProfile,
  buildRows,
  columnLetter,
  initialMapping,
  missingFields,
  profileSettings,
  saveMapping,
  type FieldInfo,
} from "./bulkPrint";

const fields: FieldInfo[] = [
  { name: "name", label: "Name", defaultValue: null, required: true },
  { name: "sku", label: "Article", defaultValue: "none", required: true },
  { name: "note", label: null, defaultValue: null, required: false },
];

function data(rows: string[][], overrides: Partial<PrintData> = {}): PrintData {
  return {
    kind: "text",
    sheets: [],
    sheet: 0,
    separator: ";",
    separatorDetected: true,
    hasHeader: false,
    rows,
    fields: {},
    quantityColumn: null,
    ...overrides,
  };
}

describe("bulk print helpers", () => {
  beforeEach(() => localStorage.clear());

  it("names columns like a spreadsheet", () => {
    expect([0, 1, 25, 26, 27, 701, 702].map(columnLetter)).toEqual(["A", "B", "Z", "AA", "AB", "ZZ", "AAA"]);
  });

  it("asks only for required fields without a default", () => {
    expect(missingFields(fields, { name: null, sku: null, note: null }).map((field) => field.name)).toEqual(["name"]);
    expect(missingFields(fields, { name: 0, sku: null, note: null })).toEqual([]);
  });

  it("turns rows into labels, skipping the header and leaving unmatched fields to their defaults", () => {
    const rows = buildRows(
      data([
        ["Name", "Copies", "Note"],
        ["Box", "3", "fragile"],
        ["Bag", "", ""],
      ]),
      true,
      fields,
      { name: 0, sku: null, note: 2 },
      1,
    );

    expect(rows).toEqual([
      { fileRow: 2, values: { name: "Box", note: "fragile" }, quantity: 3, quantityError: false },
      { fileRow: 3, values: { name: "Bag", note: "" }, quantity: 1, quantityError: false },
    ]);
  });

  it("flags a quantity that isn't a whole number from 1 to 999", () => {
    const rows = buildRows(data([["a", "0"], ["b", "two"], ["c", "1.5"], ["d", "1000"], ["e", " 7 "]]), false, fields, { name: 0 }, 1);

    expect(rows.map((row) => row.quantityError)).toEqual([true, true, true, true, false]);
    expect(rows.map((row) => row.quantity)).toEqual([1, 1, 1, 1, 7]);
  });

  it("starts with the server's matching", () => {
    const parsed = data([["Name", "Qty"], ["Box", "2"]], { hasHeader: true, fields: { name: 0 }, quantityColumn: 1 });

    expect(initialMapping(5, parsed, fields)).toEqual({ mapping: { name: 0, sku: null, note: null }, quantityColumn: 1 });
  });

  it("remembers a header-less file's matching per template", () => {
    const parsed = data([["Box", "A-1", "2"], ["Bag", "A-2", "1"]]);
    saveMapping(5, parsed, false, { name: 0, sku: 1, note: null }, 2);

    expect(initialMapping(5, parsed, fields)).toEqual({ mapping: { name: 0, sku: 1, note: null }, quantityColumn: 2 });
    expect(initialMapping(6, parsed, fields).mapping).toEqual({ name: null, sku: null, note: null });
    // A narrower file can't use column C.
    expect(initialMapping(5, data([["Box", "A-1"]]), fields).quantityColumn).toBeNull();
  });

  it("remembers a matching by header text, wherever the column moved to", () => {
    saveMapping(5, data([["Bezeichnung", "Nr"], ["Box", "1"]]), true, { name: 0, sku: 1, note: null }, null);

    const moved = data([["Nr", "Extra", "Bezeichnung"], ["1", "x", "Box"]], { hasHeader: true });

    expect(initialMapping(5, moved, fields).mapping).toEqual({ name: 2, sku: 0, note: null });
  });

  it("puts into a profile how the file was read and matched", () => {
    const file = data([["Bezeichnung", "Nr", "Stück"], ["Box", "1", "2"]], { hasHeader: true });

    const settings = profileSettings("stock.csv", file, true, { name: 0, sku: 1, note: null }, 2, {
      printerId: 4,
      printerName: null,
      quality: "High",
      cutMode: "HalfCut",
    });

    expect(settings).toEqual({
      fileName: "stock.csv",
      filePath: null,
      separator: ";",
      sheet: null,
      hasHeader: true,
      columns: [
        { field: "name", column: 0, header: "Bezeichnung" },
        { field: "sku", column: 1, header: "Nr" },
      ],
      quantityColumn: 2,
      quantityHeader: "Stück",
      printerId: 4,
      printerName: null,
      quality: "High",
      cutMode: "HalfCut",
      copies: 1,
    });
  });

  it("applies a profile by header text when columns moved, and by position without a header", () => {
    const file = data([["Bezeichnung", "Nr", "Stück"], ["Box", "1", "2"]], { hasHeader: true });
    const settings = profileSettings("stock.csv", file, true, { name: 0, sku: 1, note: null }, 2, { printerId: null, printerName: null });

    const moved = applyProfile(settings, data([["Stück", "Extra", "Bezeichnung", "Nr"], ["2", "x", "Box", "1"]]), fields);
    const plain = applyProfile({ ...settings, hasHeader: false }, data([["Box", "1", "2"]]), fields);
    const narrower = applyProfile({ ...settings, hasHeader: false }, data([["Box"]]), fields);

    expect(moved).toEqual({ hasHeader: true, mapping: { name: 2, sku: 3, note: null }, quantityColumn: 0 });
    expect(plain).toEqual({ hasHeader: false, mapping: { name: 0, sku: 1, note: null }, quantityColumn: 2 });
    expect(narrower).toEqual({ hasHeader: false, mapping: { name: 0, sku: null, note: null }, quantityColumn: null });
  });
});
