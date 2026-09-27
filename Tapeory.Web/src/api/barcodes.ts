import type { BarcodeSymbology } from "../editor/types";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

/** A barcode's modules as the server encodes them: row by row, "1" for dark. */
export interface EncodedBarcode {
  columns: number;
  rows: number;
  twoDimensional: boolean;
  modules: string;
  /** What prints under a 1D barcode's bars (EAN/UPC get their check digit). */
  text: string;
}

export type BarcodeEncoding = { barcode: EncodedBarcode; error: null } | { barcode: null; error: string };

const cache = new Map<string, Promise<BarcodeEncoding>>();

/** Encodes on the server, so the editor draws exactly what prints. Results are cached per
 * type and value; errors (e.g. a value EAN-13 can't encode) come back as a message. */
export function encodeBarcode(symbology: BarcodeSymbology, data: string): Promise<BarcodeEncoding> {
  const key = `${symbology}\u0000${data}`;
  let pending = cache.get(key);

  if (!pending) {
    pending = fetch(`${API_BASE_URL}/barcodes/encode`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ symbology, data }),
    })
      .then(async (response): Promise<BarcodeEncoding> => {
        if (response.ok) {
          return { barcode: (await response.json()) as EncodedBarcode, error: null };
        }

        const problem = await response.json().catch(() => null);
        return { barcode: null, error: problem?.detail ?? `Request failed with status ${response.status}` };
      })
      .catch((err: unknown) => {
        cache.delete(key); // network trouble: try again next time
        return { barcode: null, error: err instanceof Error ? err.message : String(err) };
      });

    cache.set(key, pending);
  }

  return pending;
}

/** Share of a 1D barcode's height used by its text line, capped at 4 mm (mirrors BarcodeLayout.cs). */
export function barcodeTextHeight(height: number): number {
  return Math.min(height * 0.25, 4);
}

/** The font size, as a share of the text line's height (mirrors BarcodeLayout.cs). */
export const BARCODE_TEXT_SIZE_RATIO = 0.8;
