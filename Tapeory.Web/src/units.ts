/** Everything is stored in millimetres; this is only how lengths are shown and typed. */
export type LengthUnit = "mm" | "inch";

export const LENGTH_UNITS: readonly LengthUnit[] = ["mm", "inch"];
export const MM_PER_INCH = 25.4;

export function isLengthUnit(value: unknown): value is LengthUnit {
  return value === "mm" || value === "inch";
}

export function unitSymbol(unit: LengthUnit): string {
  return unit === "inch" ? "in" : "mm";
}

/** Millimetres → the number shown in an input (3 decimals for inches ≈ 0.03 mm, 2 for mm). */
export function toDisplayValue(mm: number, unit: LengthUnit): number {
  const value = unit === "inch" ? mm / MM_PER_INCH : mm;
  const factor = unit === "inch" ? 1000 : 100;
  return Math.round(value * factor) / factor;
}

/** A number typed into an input → millimetres. */
export function fromDisplayValue(value: number, unit: LengthUnit): number {
  return unit === "inch" ? value * MM_PER_INCH : value;
}

/** Sensible input step: half a millimetre, or a hundredth of an inch. */
export function inputStep(unit: LengthUnit): number {
  return unit === "inch" ? 0.01 : 0.5;
}

function formatNumber(value: number, locale: string, maxFractionDigits: number): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits: maxFractionDigits }).format(value);
}

/** Just the number, e.g. "12" or "0.47". */
export function formatLengthNumber(mm: number, unit: LengthUnit, locale: string): string {
  return unit === "inch" ? formatNumber(mm / MM_PER_INCH, locale, 2) : formatNumber(mm, locale, 1);
}

/** A label dimension, e.g. "12 mm" or "0.47 in". */
export function formatDimension(mm: number, unit: LengthUnit, locale: string): string {
  return `${formatLengthNumber(mm, unit, locale)} ${unitSymbol(unit)}`;
}

/** A label size, e.g. "62 × 29 mm" or "2.44 × 1.14 in". */
export function formatSize(widthMm: number, heightMm: number, unit: LengthUnit, locale: string): string {
  return `${formatLengthNumber(widthMm, unit, locale)} × ${formatLengthNumber(heightMm, unit, locale)} ${unitSymbol(unit)}`;
}

/** A long total such as printed tape, in the most readable unit (mm/cm/m/km or in/ft/mi). */
export function formatTotalLength(mm: number, unit: LengthUnit, locale: string): string {
  if (unit === "inch") {
    const inches = mm / MM_PER_INCH;
    const feet = inches / 12;
    if (feet >= 5280) return `${formatNumber(feet / 5280, locale, 2)} mi`;
    if (inches >= 12) return `${formatNumber(feet, locale, 1)} ft`;
    return `${formatNumber(inches, locale, 1)} in`;
  }

  if (mm >= 1_000_000) return `${formatNumber(mm / 1_000_000, locale, 2)} km`;
  if (mm >= 1000) return `${formatNumber(mm / 1000, locale, 2)} m`;
  if (mm >= 10) return `${formatNumber(mm / 10, locale, 1)} cm`;
  return `${formatNumber(mm, locale, 1)} mm`;
}

/** A total area, e.g. "1.2 m²" or "13 ft²". */
export function formatTotalArea(mm2: number, unit: LengthUnit, locale: string): string {
  if (unit === "inch") {
    const squareInches = mm2 / (MM_PER_INCH * MM_PER_INCH);
    if (squareInches >= 144) return `${formatNumber(squareInches / 144, locale, 1)} ft²`;
    return `${formatNumber(squareInches, locale, 1)} in²`;
  }

  if (mm2 >= 1_000_000) return `${formatNumber(mm2 / 1_000_000, locale, 2)} m²`;
  if (mm2 >= 100) return `${formatNumber(mm2 / 100, locale, 1)} cm²`;
  return `${formatNumber(mm2, locale, 1)} mm²`;
}
