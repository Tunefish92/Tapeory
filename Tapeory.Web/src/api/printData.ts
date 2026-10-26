import { SESSION_ENDED_EVENT } from "./session";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

/** Data for bulk printing (an Excel, CSV, text or JSON file, or a web address's answer) as the server read it. */
export interface PrintData {
  /** "json": a list of records, whose property names are always the header row. */
  kind: "text" | "spreadsheet" | "json";
  /** The workbook's sheet names; empty for a text file. */
  sheets: string[];
  sheet: number;
  /** "," ";" "|" …, "tab", "space", or "none" for one value per line; null for a spreadsheet. */
  separator: string | null;
  /** False when the file doesn't say clearly which separator it uses: ask the user. */
  separatorDetected: boolean;
  /** The first row names the columns and isn't a label itself. */
  hasHeader: boolean;
  /** Every row, the header included; all rows have the same number of cells. */
  rows: string[][];
  /** Field name → column index, for the fields matched by the header. */
  fields: Record<string, number>;
  quantityColumn: number | null;
}

/** The largest data file the server reads, in megabytes. */
export const MAX_FILE_MB = 100;

export interface ParseOptions {
  /** The user's choice; left out, the separator is detected. */
  separator?: string;
  sheet?: number;
  /** Called with 0…1 while the file uploads. */
  onUploadProgress?: (fraction: number) => void;
}

/**
 * Reads a data file on the server and matches its columns to the template's fields. Sent with
 * XMLHttpRequest, because fetch can't report upload progress.
 */
export function parsePrintData(templateId: number, file: File, options: ParseOptions = {}): Promise<PrintData> {
  const form = new FormData();
  form.append("file", file);
  form.append("templateId", String(templateId));
  form.append("locale", navigator.language);
  if (options.separator !== undefined) form.append("separator", options.separator);
  if (options.sheet !== undefined) form.append("sheet", String(options.sheet));

  return new Promise((resolve, reject) => {
    const request = new XMLHttpRequest();
    request.open("POST", `${API_BASE_URL}/print-data/parse`);
    request.setRequestHeader("X-Requested-With", "Tapeory");
    request.responseType = "json";

    request.upload.onprogress = (event) => {
      if (event.lengthComputable && event.total > 0) options.onUploadProgress?.(event.loaded / event.total);
    };

    request.onload = () => {
      if (request.status === 401) window.dispatchEvent(new Event(SESSION_ENDED_EVENT));

      if (request.status >= 200 && request.status < 300 && request.response) {
        resolve(request.response as PrintData);
      } else {
        const problem = request.response as { detail?: string; title?: string } | null;
        reject(new Error(problem?.detail ?? problem?.title ?? `Request failed with status ${request.status}`));
      }
    };
    request.onerror = () => reject(new Error("The file couldn't be sent."));
    request.send(form);
  });
}

/** A web address the data comes from, with an optional request header (e.g. an API key). */
export interface DataUrl {
  url: string;
  headerName: string;
  headerValue: string;
}

/** Lets the server get the data from a web address (a REST endpoint, or a file over HTTP) and read it. */
export async function fetchPrintData(
  templateId: number,
  source: DataUrl,
  options: { separator?: string; sheet?: number } = {},
): Promise<PrintData> {
  const response = await fetch(`${API_BASE_URL}/print-data/fetch`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      templateId,
      url: source.url,
      headerName: source.headerName || null,
      headerValue: source.headerValue || null,
      separator: options.separator ?? null,
      sheet: options.sheet ?? null,
      locale: navigator.language,
    }),
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail ?? problem?.title ?? `Request failed with status ${response.status}`);
  }

  return (await response.json()) as PrintData;
}

export interface CheckedRow {
  /** Why this row can't be printed. */
  errors: string[];
  /** What may look wrong on the label; the row can still be printed. */
  warnings: string[];
}

/** How many rows one check request takes; the caller sends a file's rows chunk by chunk. */
export const CHECK_CHUNK_SIZE = 50;

/** Checks rows the way a print job would: required fields, barcodes, and a trial render. */
export async function checkRows(
  templateId: number,
  rows: Record<string, string>[],
  signal?: AbortSignal,
): Promise<CheckedRow[]> {
  const response = await fetch(`${API_BASE_URL}/templates/${templateId}/check-rows`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ rows }),
    signal,
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail ?? problem?.title ?? `Request failed with status ${response.status}`);
  }

  return ((await response.json()) as { rows: CheckedRow[] }).rows;
}
