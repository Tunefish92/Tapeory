const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

export interface DatabaseSetupRequest {
  host: string;
  port: number;
  database: string;
  user: string;
  password: string;
}

export interface DatabaseTestResult {
  isSuccess: boolean;
  errorMessage: string | null;
  /** False when the server is reachable but the database will be created during setup. */
  databaseExists: boolean;
}

/** The first field error (validation), else the problem's detail or title. */
async function problemMessage(response: Response): Promise<string | null> {
  const problem = await response.json().catch(() => null);
  const fieldErrors: string[] = problem?.errors ? Object.values<string[]>(problem.errors).flat() : [];
  return fieldErrors[0] ?? problem?.detail ?? problem?.title ?? null;
}

async function post(path: string, request: DatabaseSetupRequest): Promise<Response> {
  const response = await fetch(`${API_BASE_URL}/setup/${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error((await problemMessage(response)) ?? `Setup request failed with status ${response.status}`);
  }

  return response;
}

/**
 * True only when the server explicitly reports that no database is configured yet. Anything else
 * (unreachable server, older API) counts as "set up", so the app shows its usual error states.
 */
export async function isSetupRequired(): Promise<boolean> {
  try {
    const response = await fetch(`${API_BASE_URL}/setup/status`);
    if (!response.ok) return false;
    const body = (await response.json()) as { configured?: unknown } | null;
    return body?.configured === false;
  } catch {
    return false;
  }
}

export async function testDatabaseConnection(request: DatabaseSetupRequest): Promise<DatabaseTestResult> {
  return (await (await post("database/test", request)).json()) as DatabaseTestResult;
}

/** Tests the connection, creates the tables and saves the details on the server. */
export async function configureDatabase(request: DatabaseSetupRequest): Promise<void> {
  await post("database", request);
}
