import type { TextFit } from "./fitText";

export type { TextFit };

export type Unit = "mm" | "inch";

export type TextAlign = "left" | "center" | "right";

export type FontWeight = "normal" | "bold";

interface BaseObject {
  id: string;
  x: number;
  y: number;
  rotation: number;
  locked: boolean;
  hidden: boolean;
}

export interface TextObject extends BaseObject {
  type: "text";
  text: string;
  width: number;
  height: number;
  fontSize: number;
  fontFamily: string;
  fontWeight: FontWeight;
  align: TextAlign;
  fill: string;
  /** Fixed-size behaviour; absent in documents saved before it existed (= "none"). */
  fit?: TextFit;
}

export interface DynamicFieldObject extends BaseObject {
  type: "dynamicField";
  fieldName: string;
  label: string;
  defaultValue: string;
  required: boolean;
  width: number;
  height: number;
  fontSize: number;
  fontFamily: string;
  fontWeight: FontWeight;
  align: TextAlign;
  fill: string;
  fit?: TextFit;
}

export interface RectObject extends BaseObject {
  type: "rect";
  width: number;
  height: number;
  fill: string;
  stroke: string;
  strokeWidth: number;
  cornerRadius: number;
}

export interface LineObject extends BaseObject {
  type: "line";
  points: [number, number, number, number];
  stroke: string;
  strokeWidth: number;
}

export interface EllipseObject extends BaseObject {
  type: "ellipse";
  width: number;
  height: number;
  fill: string;
  stroke: string;
  strokeWidth: number;
}

/** Barcode types the server can encode (see Tapeory.Api/Barcodes/BarcodeEncoder.cs). */
export type BarcodeSymbology =
  | "code128"
  | "code39"
  | "ean13"
  | "ean8"
  | "upca"
  | "upce"
  | "itf"
  | "codabar"
  | "qr"
  | "datamatrix"
  | "pdf417"
  | "aztec";

export interface BarcodeObject extends BaseObject {
  type: "barcode";
  symbology: BarcodeSymbology;
  /** The value to encode; with a field, the default used until a value is typed in. */
  data: string;
  /** Empty for a fixed value; otherwise the template field whose value is encoded at print time. */
  fieldName: string;
  /** 1D barcodes: print the value under the bars. */
  showText: boolean;
  width: number;
  height: number;
  fill: string;
}

export interface ImageObject extends BaseObject {
  type: "image";
  uploadedFileId: number;
  url: string;
  width: number;
  height: number;
}

export type LabelObject =
  | TextObject
  | DynamicFieldObject
  | RectObject
  | LineObject
  | ImageObject
  | EllipseObject
  | BarcodeObject;

export type LabelObjectType = LabelObject["type"];

/** A patch applied to a LabelObject by id. `Partial<LabelObject>` would only expose fields
 * common to every variant (keyof over a union is an intersection of keys), so this instead
 * lists every field from every object type explicitly, all optional. Callers never need to
 * change an object's `type` through a patch, so it's deliberately omitted. */
export interface LabelObjectPatch {
  x?: number;
  y?: number;
  rotation?: number;
  locked?: boolean;
  hidden?: boolean;
  text?: string;
  width?: number;
  height?: number;
  fontSize?: number;
  fontFamily?: string;
  fontWeight?: FontWeight;
  align?: TextAlign;
  fill?: string;
  fit?: TextFit;
  fieldName?: string;
  label?: string;
  defaultValue?: string;
  required?: boolean;
  stroke?: string;
  strokeWidth?: number;
  cornerRadius?: number;
  points?: [number, number, number, number];
  uploadedFileId?: number;
  url?: string;
  symbology?: BarcodeSymbology;
  data?: string;
  showText?: boolean;
}

export const CURRENT_FORMAT_VERSION = 1;

export interface LabelDocument {
  formatVersion: typeof CURRENT_FORMAT_VERSION;
  widthMm: number;
  heightMm: number;
  /** Id of the Brother medium chosen in the editor (see brotherMedia.ts); optional. */
  media?: string;
  objects: LabelObject[];
}

export function createEmptyDocument(widthMm: number, heightMm: number): LabelDocument {
  return {
    formatVersion: CURRENT_FORMAT_VERSION,
    widthMm,
    heightMm,
    objects: [],
  };
}
