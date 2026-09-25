export type ComparisonKey = "creditCard" | "a4Sheet" | "door" | "cityBus" | "footballPitch" | "eiffelTower" | "marathon";

// Ordered smallest to largest; lengths in millimetres.
const COMPARISONS: { key: ComparisonKey; lengthMm: number }[] = [
  { key: "creditCard", lengthMm: 85.6 },
  { key: "a4Sheet", lengthMm: 297 },
  { key: "door", lengthMm: 2000 },
  { key: "cityBus", lengthMm: 12_000 },
  { key: "footballPitch", lengthMm: 105_000 },
  { key: "eiffelTower", lengthMm: 330_000 },
  { key: "marathon", lengthMm: 42_195_000 },
];

/** Picks the largest real-world object the printed tape is at least as long as, so the fact
 * always reads as "N× something" with N ≥ 1. Null until at least one credit card's worth. */
export function pickComparison(lengthMm: number): { key: ComparisonKey; factor: number } | null {
  const match = [...COMPARISONS].reverse().find((c) => lengthMm >= c.lengthMm);
  return match ? { key: match.key, factor: lengthMm / match.lengthMm } : null;
}

function number(value: number, locale: string, maxFractionDigits: number): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits: maxFractionDigits }).format(value);
}

export function formatLength(mm: number, locale: string): string {
  if (mm >= 1_000_000) return `${number(mm / 1_000_000, locale, 2)} km`;
  if (mm >= 1000) return `${number(mm / 1000, locale, 2)} m`;
  if (mm >= 10) return `${number(mm / 10, locale, 1)} cm`;
  return `${number(mm, locale, 1)} mm`;
}

export function formatArea(mm2: number, locale: string): string {
  if (mm2 >= 1_000_000) return `${number(mm2 / 1_000_000, locale, 2)} m²`;
  if (mm2 >= 100) return `${number(mm2 / 100, locale, 1)} cm²`;
  return `${number(mm2, locale, 1)} mm²`;
}

export function formatCount(value: number, locale: string): string {
  return number(value, locale, 0);
}

export function formatFactor(value: number, locale: string): string {
  return number(value, locale, value < 10 ? 1 : 0);
}
