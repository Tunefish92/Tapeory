import type {
  BarcodeObject,
  DynamicFieldObject,
  EllipseObject,
  ImageObject,
  LabelDocument,
  LabelObject,
  LabelObjectPatch,
  LineObject,
  RectObject,
  TextObject,
} from "./types";

let idCounter = 0;

/** A portable id generator that doesn't depend on the Web Crypto API being present. */
export function generateId(): string {
  idCounter += 1;
  return `obj-${Date.now().toString(36)}-${idCounter}-${Math.random().toString(36).slice(2, 8)}`;
}

/** editorJson isn't always produced by this editor's own object factories — the .lbx importer
 * builds it on the backend too, and neither the backend's parser nor the on-disk format actually
 * requires an `id` (it's a frontend-only concern, used only as the object's React key and as a
 * selection handle). Backfill any missing or duplicate id so every object the editor loads is
 * safe to key and select, regardless of where the document came from. */
export function normalizeDocument(document: LabelDocument): LabelDocument {
  const seen = new Set<string>();

  const objects = document.objects.map((object) => {
    if (!object.id || seen.has(object.id)) {
      return { ...object, id: generateId() };
    }

    seen.add(object.id);
    return object;
  });

  return { ...document, objects };
}

export function createTextObject(overrides: Partial<TextObject> = {}): TextObject {
  return {
    id: generateId(),
    type: "text",
    x: 5,
    y: 5,
    width: 30,
    height: 8,
    rotation: 0,
    locked: false,
    hidden: false,
    text: "Text",
    fontSize: 12,
    fontFamily: "Arial",
    fontWeight: "normal",
    align: "left",
    fill: "#000000",
    fit: "none",
    ...overrides,
  };
}

export function createDynamicFieldObject(
  overrides: Partial<DynamicFieldObject> = {},
): DynamicFieldObject {
  return {
    id: generateId(),
    type: "dynamicField",
    x: 5,
    y: 5,
    width: 30,
    height: 8,
    rotation: 0,
    locked: false,
    hidden: false,
    fieldName: "fieldName",
    label: "Field",
    defaultValue: "",
    required: true,
    fontSize: 12,
    fontFamily: "Arial",
    fontWeight: "normal",
    align: "left",
    fill: "#000000",
    // Values arrive at print time and can be any length, so fields keep to their box by default.
    fit: "shrink",
    ...overrides,
  };
}

export function createRectObject(overrides: Partial<RectObject> = {}): RectObject {
  return {
    id: generateId(),
    type: "rect",
    x: 5,
    y: 5,
    width: 20,
    height: 12,
    rotation: 0,
    locked: false,
    hidden: false,
    fill: "transparent",
    stroke: "#000000",
    strokeWidth: 0.5,
    cornerRadius: 0,
    ...overrides,
  };
}

export function createEllipseObject(overrides: Partial<EllipseObject> = {}): EllipseObject {
  return {
    id: generateId(),
    type: "ellipse",
    x: 5,
    y: 5,
    width: 12,
    height: 12,
    rotation: 0,
    locked: false,
    hidden: false,
    fill: "transparent",
    stroke: "#000000",
    strokeWidth: 0.5,
    ...overrides,
  };
}

export function createBarcodeObject(overrides: Partial<BarcodeObject> = {}): BarcodeObject {
  return {
    id: generateId(),
    type: "barcode",
    x: 2,
    y: 1,
    width: 30,
    height: 8,
    rotation: 0,
    locked: false,
    hidden: false,
    symbology: "code128",
    data: "12345678",
    fieldName: "",
    showText: true,
    fill: "#000000",
    ...overrides,
  };
}

export function createLineObject(overrides: Partial<LineObject> = {}): LineObject {
  return {
    id: generateId(),
    type: "line",
    x: 5,
    y: 5,
    rotation: 0,
    locked: false,
    hidden: false,
    points: [0, 0, 20, 0],
    stroke: "#000000",
    strokeWidth: 0.5,
    ...overrides,
  };
}

export function createImageObject(
  uploadedFileId: number,
  url: string,
  overrides: Partial<ImageObject> = {},
): ImageObject {
  return {
    id: generateId(),
    type: "image",
    x: 5,
    y: 5,
    width: 20,
    height: 20,
    rotation: 0,
    locked: false,
    hidden: false,
    uploadedFileId,
    url,
    ...overrides,
  };
}

/**
 * Moves and shrinks a new object so it lies inside a label of this size: the default boxes are
 * higher than a narrow tape. Round shapes and images keep their proportions.
 */
export function fitIntoLabel<T extends LabelObject>(object: T, labelWidth: number, labelHeight: number): T {
  const edge = 1;
  const roomX = Math.max(labelWidth - 2 * edge, 1);
  const roomY = Math.max(labelHeight - 2 * edge, 1);

  if (object.type === "line") {
    const [x1, y1, x2] = object.points;
    const length = Math.min(Math.abs(x2 - x1), roomX);
    return {
      ...object,
      points: [x1, y1, x1 + length, y1],
      x: Math.max(Math.min(object.x, labelWidth - edge - length), edge),
      y: Math.min(object.y, labelHeight / 2),
    };
  }

  const keepsProportions = object.type === "ellipse" || object.type === "image";
  const scale = Math.min(roomX / object.width, roomY / object.height, 1);
  const width = keepsProportions ? object.width * scale : Math.min(object.width, roomX);
  const height = keepsProportions ? object.height * scale : Math.min(object.height, roomY);

  return {
    ...object,
    width,
    height,
    x: Math.max(Math.min(object.x, labelWidth - edge - width), edge),
    y: Math.max(Math.min(object.y, labelHeight - edge - height), edge),
  };
}

/**
 * The font new text starts in: Arial where the server has it, otherwise the bundled Inter (or
 * the first font there is), so a new text isn't flagged "not installed" straight away.
 */
export function defaultFontFamily(installed: readonly string[] | null): string {
  if (!installed || installed.length === 0 || installed.some((family) => family.toLowerCase() === "arial")) {
    return "Arial";
  }
  return installed.find((family) => family === "Inter") ?? installed[0];
}

export function addObject(document: LabelDocument, object: LabelObject): LabelDocument {
  return { ...document, objects: [...document.objects, object] };
}

export function updateObject(
  document: LabelDocument,
  id: string,
  changes: LabelObjectPatch,
): LabelDocument {
  return {
    ...document,
    objects: document.objects.map((object) =>
      object.id === id ? ({ ...object, ...changes } as LabelObject) : object,
    ),
  };
}

export function removeObject(document: LabelDocument, id: string): LabelDocument {
  return { ...document, objects: document.objects.filter((object) => object.id !== id) };
}

export function duplicateObject(document: LabelDocument, id: string): LabelDocument {
  const source = document.objects.find((object) => object.id === id);

  if (!source) {
    return document;
  }

  const copy: LabelObject = { ...source, id: generateId(), x: source.x + 5, y: source.y + 5 };
  return { ...document, objects: [...document.objects, copy] };
}

export type ReorderDirection = "front" | "back" | "forward" | "backward";

export function reorderObject(
  document: LabelDocument,
  id: string,
  direction: ReorderDirection,
): LabelDocument {
  const index = document.objects.findIndex((object) => object.id === id);

  if (index === -1) {
    return document;
  }

  const objects = [...document.objects];
  const [object] = objects.splice(index, 1);

  switch (direction) {
    case "front":
      objects.push(object);
      break;
    case "back":
      objects.unshift(object);
      break;
    case "forward":
      objects.splice(Math.min(index + 1, objects.length), 0, object);
      break;
    case "backward":
      objects.splice(Math.max(index - 1, 0), 0, object);
      break;
  }

  return { ...document, objects };
}

export interface ExtractedField {
  name: string;
  label: string | null;
  defaultValue: string | null;
  required: boolean;
}

/** The template fields: every dynamic field on the canvas, plus fields that only a barcode uses,
 * deduplicated by name. A barcode sharing a dynamic field's name uses that field (and its label
 * and default), so one value fills both. */
export function extractFields(document: LabelDocument): ExtractedField[] {
  const seen = new Map<string, ExtractedField>();

  for (const object of document.objects) {
    if (object.type === "dynamicField" && !seen.has(object.fieldName)) {
      seen.set(object.fieldName, {
        name: object.fieldName,
        label: object.label || null,
        defaultValue: object.defaultValue || null,
        required: object.required,
      });
    }
  }

  for (const object of document.objects) {
    const name = object.type === "barcode" ? object.fieldName.trim() : "";

    if (object.type === "barcode" && name && !seen.has(name)) {
      seen.set(name, { name, label: null, defaultValue: object.data || null, required: false });
    }
  }

  return [...seen.values()];
}
