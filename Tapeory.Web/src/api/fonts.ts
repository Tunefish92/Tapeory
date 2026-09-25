const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

let familiesPromise: Promise<string[]> | null = null;

/** Font families installed on the server, i.e. the ones labels can be printed with. Cached. */
export function listFonts(): Promise<string[]> {
  familiesPromise ??= fetch(`${API_BASE_URL}/fonts`)
    .then(async (response) => {
      if (!response.ok) throw new Error(`Font list request failed with status ${response.status}`);
      const body = (await response.json()) as unknown;
      return Array.isArray(body) ? body.filter((item): item is string => typeof item === "string") : [];
    })
    .catch((err: unknown) => {
      familiesPromise = null; // Let the next caller retry.
      throw err;
    });

  return familiesPromise;
}

/** Test hook: forget the cached list. */
export function resetFontCache() {
  familiesPromise = null;
  loaded.clear();
}

export function fontFileUrl(family: string, bold: boolean): string {
  const params = new URLSearchParams({ family });
  if (bold) params.set("weight", "bold");
  return `${API_BASE_URL}/fonts/file?${params.toString()}`;
}

const loaded = new Map<string, Promise<boolean>>();

/**
 * Loads the server's own font file into the page under the same family name, so the editor
 * previews (and measures fitted text) with exactly the font the printed label uses — even when
 * this computer doesn't have it installed. Resolves false if it couldn't be loaded; the browser
 * then falls back to a local font of that name, if any.
 */
export function ensureFontLoaded(family: string, bold: boolean): Promise<boolean> {
  if (!family || typeof FontFace === "undefined" || typeof document === "undefined" || !document.fonts) {
    return Promise.resolve(false);
  }

  const key = `${family}|${bold ? "bold" : "normal"}`;
  let promise = loaded.get(key);

  if (!promise) {
    const face = new FontFace(family, `url("${fontFileUrl(family, bold)}")`, { weight: bold ? "700" : "400" });
    promise = face
      .load()
      .then((ready) => {
        document.fonts.add(ready);
        return true;
      })
      .catch(() => false);
    loaded.set(key, promise);
  }

  return promise;
}
