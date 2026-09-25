/**
 * Official Brother label media, as published by Brother. "Height" is the size across the tape
 * or roll (the label's short side as it leaves the printer); die-cut labels also fix the width.
 *
 * Sources: Brother TZe/HGe tape range (P-touch), HSe 3:1 heat-shrink tube range, and the QL
 * DK roll range (continuous and die-cut).
 */
export type MediaGroup = "tze" | "hse" | "dkContinuous" | "dkDieCut";

export interface MediaPreset {
  id: string;
  group: MediaGroup;
  /** Brother's product code, where one identifies the size (DK rolls, HSe tubes). */
  code?: string;
  heightMm: number;
  /** Only die-cut labels: their length along the roll is fixed too. */
  widthMm?: number;
  round?: boolean;
}

export const MEDIA_GROUPS: readonly MediaGroup[] = ["tze", "hse", "dkContinuous", "dkDieCut"];

const tze = (heightMm: number): MediaPreset => ({ id: `tze-${heightMm}`, group: "tze", heightMm });
const hse = (code: string, heightMm: number): MediaPreset => ({ id: code, group: "hse", code, heightMm });
const dk = (code: string, heightMm: number): MediaPreset => ({ id: code, group: "dkContinuous", code, heightMm });
const dieCut = (code: string, heightMm: number, widthMm: number, round = false): MediaPreset => ({
  id: code,
  group: "dkDieCut",
  code,
  heightMm,
  widthMm,
  round,
});

export const MEDIA_PRESETS: readonly MediaPreset[] = [
  // P-touch TZe laminated tape (HGe high-grade tape comes in the same widths).
  tze(3.5),
  tze(6),
  tze(9),
  tze(12),
  tze(18),
  tze(24),
  tze(36),
  // P-touch HSe heat-shrink tube, 3:1 series.
  hse("HSe-211E", 5.2),
  hse("HSe-221E", 9),
  hse("HSe-231E", 11.2),
  hse("HSe-241E", 17.7),
  hse("HSe-251E", 21),
  hse("HSe-261E", 31),
  // QL DK continuous-length rolls.
  dk("DK-22214", 12),
  dk("DK-22210", 29),
  dk("DK-22225", 38),
  dk("DK-22223", 50),
  dk("DK-N55224", 54),
  dk("DK-22205", 62),
  dk("DK-22243", 102),
  dk("DK-22246", 103.6),
  // QL DK die-cut labels (height across the roll × length).
  dieCut("DK-11219", 12, 12, true),
  dieCut("DK-11204", 17, 54),
  dieCut("DK-11203", 17, 87),
  dieCut("DK-11221", 23, 23),
  dieCut("DK-11218", 24, 24, true),
  dieCut("DK-11209", 29, 62),
  dieCut("DK-11201", 29, 90),
  dieCut("DK-11208", 38, 90),
  dieCut("DK-11207", 58, 58, true),
  dieCut("DK-11234", 60, 86),
  dieCut("DK-11202", 62, 100),
  dieCut("DK-11240", 102, 50),
  dieCut("DK-11241", 102, 152),
  dieCut("DK-11247", 103, 164),
];

const EPSILON = 0.05;
const same = (a: number, b: number) => Math.abs(a - b) < EPSILON;

export function getMediaPreset(id: string | undefined | null): MediaPreset | undefined {
  return id ? MEDIA_PRESETS.find((preset) => preset.id === id) : undefined;
}

function matches(preset: MediaPreset, widthMm: number, heightMm: number): boolean {
  return same(preset.heightMm, heightMm) && (preset.widthMm === undefined || same(preset.widthMm, widthMm));
}

/**
 * Which preset a label uses: the one it remembers if its size still matches, otherwise the
 * first official medium of that size (die-cut labels need the width to match as well).
 * Undefined for a non-standard size, e.g. an imported template.
 */
export function findMediaPreset(widthMm: number, heightMm: number, mediaId?: string | null): MediaPreset | undefined {
  const remembered = getMediaPreset(mediaId);
  if (remembered && matches(remembered, widthMm, heightMm)) {
    return remembered;
  }

  return (
    MEDIA_PRESETS.find((preset) => preset.widthMm !== undefined && matches(preset, widthMm, heightMm)) ??
    MEDIA_PRESETS.find((preset) => preset.widthMm === undefined && matches(preset, widthMm, heightMm))
  );
}
