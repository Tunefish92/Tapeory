const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

export interface BulkPrintProfileColumn {
  field: string;
  /** The column's position in the file, from 0. */
  column: number;
  /** The column's header text, when the file has a header row. */
  header: string | null;
}

/** Everything a bulk print needs to run again. */
export interface BulkPrintProfileSettings {
  fileName: string | null;
  /** Where the file is on the computer of the desktop app that saved the profile; a browser never knows it. */
  filePath: string | null;
  separator: string | null;
  sheet: number | null;
  hasHeader: boolean;
  columns: BulkPrintProfileColumn[] | null;
  quantityColumn: number | null;
  quantityHeader: string | null;
  printerId: number | null;
  printerName: string | null;
  quality: string | null;
  cutMode: string | null;
  /** How many times each label is printed (on top of a copies column). */
  copies?: number | null;
  /** The web address the data comes from, instead of a file, with its optional request header. */
  url?: string | null;
  urlHeaderName?: string | null;
  urlHeaderValue?: string | null;
}

export interface BulkPrintProfile {
  id: number;
  templateId: number;
  name: string;
  settings: BulkPrintProfileSettings;
  updatedAt: string;
}

async function parseJsonOrThrow<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail ?? problem?.title ?? `Request failed with status ${response.status}`);
  }

  return (await response.json()) as T;
}

/** The signed-in account's saved bulk print setups for a template. */
export async function listBulkPrintProfiles(templateId: number): Promise<BulkPrintProfile[]> {
  return parseJsonOrThrow(await fetch(`${API_BASE_URL}/templates/${templateId}/bulk-print-profiles`));
}

/** A profile with this name already exists; saving again with `replace` overwrites it. */
export class ProfileNameTakenError extends Error {}

/** Saves a profile. A name that is taken is refused with {@link ProfileNameTakenError} unless `replace` is set. */
export async function saveBulkPrintProfile(
  templateId: number,
  name: string,
  settings: BulkPrintProfileSettings,
  replace = false,
): Promise<BulkPrintProfile> {
  const response = await fetch(`${API_BASE_URL}/templates/${templateId}/bulk-print-profiles`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name, settings, replace }),
  });

  if (response.status === 409) {
    const problem = await response.json().catch(() => null);
    throw new ProfileNameTakenError(problem?.detail ?? "A profile with this name already exists.");
  }

  return parseJsonOrThrow(response);
}

export async function deleteBulkPrintProfile(id: number): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/bulk-print-profiles/${id}`, { method: "DELETE" });
  if (!response.ok) await parseJsonOrThrow(response);
}
