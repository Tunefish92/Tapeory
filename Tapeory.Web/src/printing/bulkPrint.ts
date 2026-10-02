import type { BulkPrintProfileSettings } from "../api/bulkPrintProfiles";
import type { PrintData } from "../api/printData";

/** Which column fills which field; null = no column (the field's default is printed). */
export type ColumnMapping = Record<string, number | null>;

export interface FieldInfo {
  name: string;
  label: string | null;
  defaultValue: string | null;
  required: boolean;
}

/** One label to print: a row of the file, turned into field values. */
export interface BulkRow {
  /** The row's number in the file (1-based, the header counted), for messages. */
  fileRow: number;
  values: Record<string, string>;
  quantity: number;
  /** Set when the quantity cell isn't a whole number of 1 or more. */
  quantityError: boolean;
}

/** "A", "B", … "Z", "AA", … like a spreadsheet's columns. */
export function columnLetter(index: number): string {
  let letters = "";
  for (let n = index; n >= 0; n = Math.floor(n / 26) - 1) {
    letters = String.fromCharCode(65 + (n % 26)) + letters;
  }
  return letters;
}

/** A required field without a default needs a column, or no row could be printed. */
export function fieldNeedsColumn(field: FieldInfo): boolean {
  return field.required && !(field.defaultValue ?? "").trim();
}

export function missingFields(fields: FieldInfo[], mapping: ColumnMapping): FieldInfo[] {
  return fields.filter((field) => fieldNeedsColumn(field) && mapping[field.name] == null);
}

const MAX_QUANTITY = 999;

/** The most copies of each label that can be set for a whole bulk print. */
export const MAX_COPIES = 99;

/** A copies setting as typed or saved, kept within 1…MAX_COPIES. */
export function clampCopies(value: number | null | undefined): number {
  return Number.isFinite(value) ? Math.min(MAX_COPIES, Math.max(1, Math.round(value as number))) : 1;
}

/** Turns the file's rows into labels. */
export function buildRows(
  data: PrintData,
  hasHeader: boolean,
  fields: FieldInfo[],
  mapping: ColumnMapping,
  quantityColumn: number | null,
): BulkRow[] {
  return data.rows.slice(hasHeader ? 1 : 0).map((cells, index) => {
    const values: Record<string, string> = {};

    for (const field of fields) {
      const column = mapping[field.name];
      if (column != null) values[field.name] = cells[column] ?? "";
    }

    const quantityText = quantityColumn === null ? "" : (cells[quantityColumn] ?? "").trim();
    const quantity = quantityText === "" ? 1 : Number(quantityText);
    const quantityError = !Number.isInteger(quantity) || quantity < 1 || quantity > MAX_QUANTITY;

    return { fileRow: index + (hasHeader ? 2 : 1), values, quantity: quantityError ? 1 : quantity, quantityError };
  });
}

interface SavedMapping {
  /** Field name → the header text of its column. */
  headers: Record<string, string>;
  /** Field name → column index, for files without a header. */
  columns: Record<string, number>;
  quantityHeader?: string;
  quantityColumn?: number;
}

const storageKey = (templateId: number) => `tapeory.bulkPrint.mapping.${templateId}`;

/** Remembers the matching per template, so the same file needs no clicks next time. */
export function saveMapping(
  templateId: number,
  data: PrintData,
  hasHeader: boolean,
  mapping: ColumnMapping,
  quantityColumn: number | null,
) {
  const saved: SavedMapping = { headers: {}, columns: {} };

  for (const [field, column] of Object.entries(mapping)) {
    if (column == null) continue;
    if (hasHeader) saved.headers[field] = data.rows[0][column];
    else saved.columns[field] = column;
  }

  if (quantityColumn !== null) {
    if (hasHeader) saved.quantityHeader = data.rows[0][quantityColumn];
    else saved.quantityColumn = quantityColumn;
  }

  try {
    localStorage.setItem(storageKey(templateId), JSON.stringify(saved));
  } catch {
    // Remembering is a convenience.
  }
}

/** The matching to start with: what the server found, completed by what was used last time. */
export function initialMapping(
  templateId: number,
  data: PrintData,
  fields: FieldInfo[],
): { mapping: ColumnMapping; quantityColumn: number | null } {
  let saved: SavedMapping | null = null;
  try {
    saved = JSON.parse(localStorage.getItem(storageKey(templateId)) ?? "null") as SavedMapping | null;
  } catch {
    saved = null;
  }

  const width = data.rows[0]?.length ?? 0;
  const header = data.hasHeader ? data.rows[0] : null;
  const mapping: ColumnMapping = {};
  const taken = new Set<number>(Object.values(data.fields));

  for (const field of fields) {
    let column: number | null = data.fields[field.name] ?? null;

    if (column === null && saved) {
      const remembered = header
        ? header.indexOf(saved.headers?.[field.name] ?? "\u0000")
        : (saved.columns?.[field.name] ?? -1);
      if (remembered >= 0 && remembered < width && !taken.has(remembered)) {
        column = remembered;
        taken.add(remembered);
      }
    }

    mapping[field.name] = column;
  }

  let quantityColumn = data.quantityColumn;
  if (quantityColumn === null && saved) {
    const remembered = header ? header.indexOf(saved.quantityHeader ?? "\u0000") : (saved.quantityColumn ?? -1);
    if (remembered >= 0 && remembered < width && !taken.has(remembered)) quantityColumn = remembered;
  }

  return { mapping, quantityColumn };
}

/** The settings to save as a profile: how the file was read and which column fills which field. */
export function profileSettings(
  fileName: string,
  data: PrintData,
  hasHeader: boolean,
  mapping: ColumnMapping,
  quantityColumn: number | null,
  print: { printerId: number | null; printerName: string | null; quality?: string; cutMode?: string },
  copies = 1,
): BulkPrintProfileSettings {
  const header = hasHeader ? data.rows[0] : null;

  return {
    fileName,
    filePath: null,
    separator: data.kind === "text" ? data.separator : null,
    sheet: data.kind === "spreadsheet" ? data.sheet : null,
    hasHeader,
    columns: Object.entries(mapping)
      .filter((entry): entry is [string, number] => entry[1] != null)
      .map(([field, column]) => ({ field, column, header: header?.[column] ?? null })),
    quantityColumn,
    quantityHeader: quantityColumn === null ? null : (header?.[quantityColumn] ?? null),
    printerId: print.printerId,
    printerName: print.printerName,
    quality: print.quality ?? null,
    cutMode: print.cutMode ?? null,
    copies,
  };
}

/**
 * A profile's matching for a file as it is now: a column is found by its header text if the file
 * has a header (so columns may have moved), otherwise by its position.
 */
export function applyProfile(
  settings: BulkPrintProfileSettings,
  data: PrintData,
  fields: FieldInfo[],
): { hasHeader: boolean; mapping: ColumnMapping; quantityColumn: number | null } {
  const hasHeader = settings.hasHeader && data.rows.length > 1;
  const header = hasHeader ? data.rows[0] : null;
  const width = data.rows[0]?.length ?? 0;

  const find = (column: number | null | undefined, text: string | null | undefined): number | null => {
    const byHeader = header && text ? header.indexOf(text) : -1;
    if (byHeader >= 0) return byHeader;
    return column != null && column >= 0 && column < width ? column : null;
  };

  const mapping: ColumnMapping = {};
  for (const field of fields) {
    const saved = settings.columns?.find((column) => column.field === field.name);
    mapping[field.name] = saved ? find(saved.column, saved.header) : null;
  }

  return { hasHeader, mapping, quantityColumn: find(settings.quantityColumn, settings.quantityHeader) };
}
