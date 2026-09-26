const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

export type BackupKind = "database" | "labels";

/** A backup file stored on the server. */
export interface BackupInfo {
  fileName: string;
  sizeBytes: number;
  createdAt: string;
  /** Taken automatically right before a restore. */
  beforeRestore: boolean;
}

export interface RestoreBackupResult {
  restoredFileName: string;
  safetyBackup: BackupInfo;
  /** Label restores only. */
  restoredTemplates: number | null;
  replacedTemplates: number | null;
}

/** Carries the server's explanation (problem details), which is more useful than a status code. */
export class BackupRequestError extends Error {}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}/backups/${path}`, init);

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new BackupRequestError(problem?.detail ?? problem?.title ?? `Backup request failed with status ${response.status}`);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

const file = (kind: BackupKind, fileName: string) => `${kind}/${encodeURIComponent(fileName)}`;

/** Newest first. */
export async function listBackups(kind: BackupKind): Promise<BackupInfo[]> {
  const backups = await request<unknown>(kind);
  return Array.isArray(backups) ? (backups as BackupInfo[]) : [];
}

export function createBackup(kind: BackupKind): Promise<BackupInfo> {
  return request(kind, { method: "POST" });
}

/** Backs up the current state first, then replaces it with the backup. */
export function restoreBackup(kind: BackupKind, fileName: string): Promise<RestoreBackupResult> {
  return request(`${file(kind, fileName)}/restore`, { method: "POST" });
}

export function deleteBackup(kind: BackupKind, fileName: string): Promise<void> {
  return request(file(kind, fileName), { method: "DELETE" });
}

export function backupDownloadUrl(kind: BackupKind, fileName: string): string {
  return `${API_BASE_URL}/backups/${file(kind, fileName)}`;
}
