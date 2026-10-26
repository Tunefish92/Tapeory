/**
 * Compares two versions like "0.6.0" or "0.6.0-beta.2": negative when `a` is older, positive
 * when it is newer. A pre-release comes before the release it leads to, and "beta.10" after
 * "beta.2".
 */
export function compareVersions(a: string, b: string): number {
  const [aCore, aPre] = split(a);
  const [bCore, bPre] = split(b);

  for (let i = 0; i < Math.max(aCore.length, bCore.length); i++) {
    const difference = (aCore[i] ?? 0) - (bCore[i] ?? 0);
    if (difference !== 0) return difference;
  }

  if (aPre === bPre) return 0;
  if (aPre === null) return 1;
  if (bPre === null) return -1;

  const aParts = aPre.split(".");
  const bParts = bPre.split(".");
  for (let i = 0; i < Math.max(aParts.length, bParts.length); i++) {
    const x = aParts[i];
    const y = bParts[i];
    if (x === undefined) return -1;
    if (y === undefined) return 1;
    const difference = /^\d+$/.test(x) && /^\d+$/.test(y) ? Number(x) - Number(y) : x.localeCompare(y);
    if (difference !== 0) return difference;
  }
  return 0;
}

function split(version: string): [number[], string | null] {
  const trimmed = version.trim().replace(/^v/i, "").split("+")[0];
  const dash = trimmed.indexOf("-");
  const core = (dash < 0 ? trimmed : trimmed.slice(0, dash)).split(".").map((part) => Number(part) || 0);
  return [core, dash < 0 ? null : trimmed.slice(dash + 1)];
}
