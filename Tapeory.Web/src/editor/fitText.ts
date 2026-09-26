/**
 * How a text box copes with content that is too big for it:
 * - "none": draw at the chosen size; long text may run past the box.
 * - "shrink": keep the lines as typed and shrink the font until everything fits.
 * - "wrap": wrap at spaces (never inside a word), shrinking only if it still doesn't fit.
 */
export type TextFit = "none" | "shrink" | "wrap";

/** Line spacing as a multiple of the font size — same as the print renderer. */
export const LINE_HEIGHT = 1.2;

/** Smallest size shrinking may go to: about 1pt, in mm. */
export const MIN_FONT_SIZE_MM = 0.35;

const EPSILON = 0.001;

export interface FittedText {
  fontSize: number;
  lines: string[];
}

/** Width of `text` at `fontSize`, in the same unit as the box. */
export type MeasureText = (text: string, fontSize: number) => number;

/** Greedy word wrap; a word wider than the box gets its own line rather than being cut. */
export function wrapParagraph(paragraph: string, width: number, fontSize: number, measure: MeasureText): string[] {
  const words = paragraph.split(" ").filter((word) => word.length > 0);
  if (words.length === 0) return [""];

  const lines: string[] = [];
  let current = words[0];

  for (const word of words.slice(1)) {
    const candidate = `${current} ${word}`;
    if (measure(candidate, fontSize) <= width + EPSILON) {
      current = candidate;
    } else {
      lines.push(current);
      current = word;
    }
  }

  lines.push(current);
  return lines;
}

/**
 * Fits text into a fixed box. Mirrors the backend's TextFitter.cs exactly so the editor shows
 * what will print — keep the two in sync.
 */
export function fitText(
  text: string,
  boxWidth: number,
  boxHeight: number,
  fontSize: number,
  mode: TextFit,
  measure: MeasureText,
): FittedText {
  const paragraphs = text.replace(/\r\n/g, "\n").split("\n");

  if (mode === "none" || boxWidth <= 0 || boxHeight <= 0 || fontSize <= 0) {
    return { fontSize, lines: paragraphs };
  }

  const layout = (size: number) =>
    mode === "wrap" ? paragraphs.flatMap((p) => wrapParagraph(p, boxWidth, size, measure)) : paragraphs;

  const fits = (lines: string[], size: number) =>
    lines.length * size * LINE_HEIGHT <= boxHeight + EPSILON &&
    lines.every((line) => measure(line, size) <= boxWidth + EPSILON);

  const atFullSize = layout(fontSize);
  if (fits(atFullSize, fontSize)) {
    return { fontSize, lines: atFullSize };
  }

  // Smaller text never needs more lines, so fitting is monotonic and a binary search finds the
  // largest size that fits.
  let low = Math.min(MIN_FONT_SIZE_MM, fontSize);
  let high = fontSize;
  let best = layout(low);

  for (let i = 0; i < 20; i++) {
    const mid = (low + high) / 2;
    const lines = layout(mid);

    if (fits(lines, mid)) {
      low = mid;
      best = lines;
    } else {
      high = mid;
    }
  }

  return { fontSize: low, lines: best };
}

let measuringContext: CanvasRenderingContext2D | null | undefined;

/**
 * Measures with the browser's canvas using the real font (see api/fonts.ts, which loads the
 * server's font files). Falls back to a rough average glyph width where canvas isn't available.
 */
export function createCanvasMeasure(fontFamily: string, bold: boolean): MeasureText {
  if (measuringContext === undefined) {
    try {
      measuringContext = typeof document === "undefined" ? null : document.createElement("canvas").getContext("2d");
    } catch {
      measuringContext = null;
    }
  }

  const context = measuringContext;
  if (!context) {
    return (text, size) => text.length * size * 0.55;
  }

  // Measure once at a reference size and scale: advances are linear in the font size.
  const referencePx = 100;
  const font = `${bold ? "bold " : ""}${referencePx}px "${fontFamily.replace(/"/g, "")}"`;
  const cache = new Map<string, number>();

  return (text, size) => {
    let width = cache.get(text);
    if (width === undefined) {
      context.font = font;
      width = context.measureText(text).width;
      cache.set(text, width);
    }
    return (width / referencePx) * size;
  };
}
