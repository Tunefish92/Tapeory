/**
 * Reopening a profile's data file without asking for it again. A web page never learns a file's
 * path; what a browser can give instead is a handle to the file the user picked, which may be
 * kept (in IndexedDB) and read again later. Only Chromium-based browsers offer this, and only on
 * HTTPS or localhost. Everywhere else these functions answer null and the user picks the file.
 */

/** A file the user picked, as the browser lets a page keep it (FileSystemFileHandle). */
export interface KeptFile {
  getFile(): Promise<File>;
  queryPermission?(options: { mode: "read" }): Promise<string>;
  requestPermission?(options: { mode: "read" }): Promise<string>;
}

type Picker = (options: unknown) => Promise<KeptFile[]>;

const DATABASE = "tapeory";
const STORE = "bulk-print-files";

function picker(): Picker | null {
  return (window as unknown as { showOpenFilePicker?: Picker }).showOpenFilePicker ?? null;
}

/** Whether this browser can keep a picked file for next time. */
export function canKeepFiles(): boolean {
  return picker() !== null && typeof indexedDB !== "undefined";
}

/** Asks for a data file with the browser's own dialog; null when the user cancels. */
export async function pickDataFile(): Promise<{ file: File; kept: KeptFile } | null> {
  try {
    const [kept] = await picker()!({
      types: [{ description: "Excel, CSV, Text, JSON", accept: { "text/plain": [".csv", ".txt", ".tsv"], "application/json": [".json"], "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet": [".xlsx"], "application/vnd.ms-excel": [".xls"] } }],
    });
    return { file: await kept.getFile(), kept };
  } catch {
    return null;
  }
}

function store(mode: IDBTransactionMode): Promise<IDBObjectStore> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, 1);
    request.onupgradeneeded = () => request.result.createObjectStore(STORE);
    request.onsuccess = () => resolve(request.result.transaction(STORE, mode).objectStore(STORE));
    request.onerror = () => reject(request.error);
  });
}

/** Keeps the file picked for a profile (or forgets it, with null). Failing is harmless. */
export async function keepFile(profileId: number, kept: KeptFile | null): Promise<void> {
  if (!canKeepFiles()) return;

  try {
    const files = await store("readwrite");
    if (kept) files.put(kept, profileId);
    else files.delete(profileId);
  } catch {
    // Without it, the profile just asks for its file.
  }
}

/** The file kept for a profile, read fresh from disk; null if there is none or the browser won't allow it. */
export async function reopenFile(profileId: number): Promise<{ file: File; kept: KeptFile } | null> {
  if (!canKeepFiles()) return null;

  try {
    const files = await store("readonly");
    const kept = await new Promise<KeptFile | undefined>((resolve, reject) => {
      const request = files.get(profileId);
      request.onsuccess = () => resolve(request.result as KeptFile | undefined);
      request.onerror = () => reject(request.error);
    });
    if (!kept) return null;

    // The browser asks once per visit whether the page may read the file again.
    if ((await kept.queryPermission?.({ mode: "read" })) !== "granted" && (await kept.requestPermission?.({ mode: "read" })) !== "granted") {
      return null;
    }

    return { file: await kept.getFile(), kept };
  } catch {
    return null;
  }
}

/** A dropped file, with a handle to keep where the browser gives one. */
export async function droppedFile(transfer: DataTransfer): Promise<{ file: File; kept: KeptFile | null } | null> {
  const item = Array.from(transfer.items ?? []).find((entry) => entry.kind === "file");
  const file = item?.getAsFile() ?? transfer.files[0];
  if (!file) return null;

  let kept: KeptFile | null = null;
  const handle = (item as unknown as { getAsFileSystemHandle?: () => Promise<KeptFile & { kind?: string }> } | undefined)
    ?.getAsFileSystemHandle;
  if (handle && canKeepFiles()) {
    try {
      const found = await handle.call(item);
      if (found?.kind === "file") kept = found;
    } catch {
      // The file itself is enough.
    }
  }

  return { file, kept };
}
