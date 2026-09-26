/** Lowercase and strip accents: "Étiketten" and "etiketten" are the same group, matching how the
 * database (MySQL's default case/accent-insensitive collation) compares them. */
export function normalizeText(value: string): string {
  return value.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLowerCase();
}

export function groupKey(group: string): string {
  return normalizeText(group.trim());
}

export interface GroupSummary {
  key: string;
  /** The spelling of the first template seen in this group. */
  name: string;
  count: number;
}

export function collectGroups(templates: { category: string | null }[]): GroupSummary[] {
  const groups = new Map<string, GroupSummary>();

  for (const template of templates) {
    const name = template.category?.trim();
    if (!name) continue;

    const key = groupKey(name);
    const existing = groups.get(key);
    if (existing) {
      existing.count += 1;
    } else {
      groups.set(key, { key, name, count: 1 });
    }
  }

  return [...groups.values()].sort((a, b) => a.name.localeCompare(b.name));
}

/** A stable hue per group name, so a group keeps its color everywhere without storing one. */
export function groupHue(group: string): number {
  let hash = 0;
  for (const char of groupKey(group)) {
    hash = (hash * 31 + char.codePointAt(0)!) | 0;
  }
  return Math.abs(hash) % 360;
}
