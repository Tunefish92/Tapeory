const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

/** App-wide preferences as stored on the server. Null = not chosen yet. */
export interface ServerSettings {
  language: string | null;
  theme: string | null;
  unit: string | null;
  /** The update check also offers pre-releases (beta versions). */
  preReleases: boolean;
}

export type SettingsPatch = Partial<Record<"language" | "theme" | "unit", string>> & { preReleases?: boolean };

async function parse(response: Response): Promise<ServerSettings> {
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail ?? problem?.title ?? `Settings request failed with status ${response.status}`);
  }

  const body = (await response.json()) as Partial<ServerSettings> | null;
  return {
    language: body?.language ?? null,
    theme: body?.theme ?? null,
    unit: body?.unit ?? null,
    preReleases: body?.preReleases === true,
  };
}

export async function getSettings(): Promise<ServerSettings> {
  return parse(await fetch(`${API_BASE_URL}/settings`));
}

/** Partial update: only the given keys change. */
export async function updateSettings(patch: SettingsPatch): Promise<ServerSettings> {
  return parse(
    await fetch(`${API_BASE_URL}/settings`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(patch),
    }),
  );
}
