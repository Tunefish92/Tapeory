export interface HealthStatus {
  status: string;
  storagePath: string;
  databaseConnected: boolean;
}

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

export async function fetchHealth(): Promise<HealthStatus> {
  const response = await fetch(`${API_BASE_URL}/health`);

  if (!response.ok) {
    throw new Error(`Health check failed with status ${response.status}`);
  }

  return (await response.json()) as HealthStatus;
}

export interface DashboardStats {
  templateCount: number;
  printerCount: number;
  printJobCount: number;
  completedPrintJobCount: number;
  failedPrintJobCount: number;
  labelsPrinted: number;
  totalPrintedLengthMm: number;
  totalPrintedAreaMm2: number;
  mostPrintedTemplateName: string | null;
  mostPrintedTemplateCount: number;
  lastPrintedAt: string | null;
  /** Null = all-time; otherwise only print jobs created since this moment are counted. */
  statsSince: string | null;
}

export async function fetchStats(): Promise<DashboardStats> {
  const response = await fetch(`${API_BASE_URL}/stats`);

  if (!response.ok) {
    throw new Error(`Stats request failed with status ${response.status}`);
  }

  return (await response.json()) as DashboardStats;
}

async function statsResetRequest(method: "POST" | "DELETE"): Promise<DashboardStats> {
  const response = await fetch(`${API_BASE_URL}/stats/reset`, { method });

  if (!response.ok) {
    throw new Error(`Stats reset failed with status ${response.status}`);
  }

  return (await response.json()) as DashboardStats;
}

/** Starts the print statistics from zero. Print history is kept, so this can be undone. */
export function resetStats(): Promise<DashboardStats> {
  return statsResetRequest("POST");
}

/** Undoes {@link resetStats}: statistics cover the whole print history again. */
export function restoreAllTimeStats(): Promise<DashboardStats> {
  return statsResetRequest("DELETE");
}

export interface UpdateCheck {
  currentVersion: string;
  latestVersion: string | null;
  updateAvailable: boolean;
  releaseUrl: string | null;
  checkedAt: string | null;
  /** Set when GitHub couldn't be asked. */
  errorMessage: string | null;
}

/** Compares this version with the latest release on GitHub; `refresh` skips the server's cache. */
export async function checkForUpdates(refresh = false): Promise<UpdateCheck> {
  const response = await fetch(`${API_BASE_URL}/updates${refresh ? "?refresh=true" : ""}`);

  if (!response.ok) {
    throw new Error(`Update check failed with status ${response.status}`);
  }

  return (await response.json()) as UpdateCheck;
}
