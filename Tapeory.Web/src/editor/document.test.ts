import { describe, expect, it } from "vitest";
import {
  addObject,
  createBarcodeObject,
  createDynamicFieldObject,
  createImageObject,
  createLineObject,
  createRectObject,
  createTextObject,
  duplicateObject,
  extractFields,
  generateId,
  normalizeDocument,
  removeObject,
  reorderObject,
  updateObject,
} from "./document";
import { createEmptyDocument } from "./types";

describe("generateId", () => {
  it("produces unique ids across calls", () => {
    const ids = new Set(Array.from({ length: 50 }, () => generateId()));
    expect(ids.size).toBe(50);
  });
});

describe("addObject / updateObject / removeObject", () => {
  it("adds an object to the document", () => {
    const doc = createEmptyDocument(50, 25);
    const text = createTextObject({ text: "Hello" });

    const result = addObject(doc, text);

    expect(result.objects).toHaveLength(1);
    expect(result.objects[0]).toBe(text);
    expect(doc.objects).toHaveLength(0); // original document is untouched
  });

  it("updates only the targeted object, leaving others untouched", () => {
    const a = createTextObject({ text: "A" });
    const b = createTextObject({ text: "B" });
    const doc = addObject(addObject(createEmptyDocument(50, 25), a), b);

    const result = updateObject(doc, a.id, { x: 42 });

    const updatedA = result.objects.find((o) => o.id === a.id);
    const untouchedB = result.objects.find((o) => o.id === b.id);

    expect(updatedA).toMatchObject({ x: 42, text: "A" });
    expect(untouchedB).toEqual(b);
  });

  it("updateObject is a no-op when the id does not exist", () => {
    const doc = addObject(createEmptyDocument(50, 25), createTextObject());

    const result = updateObject(doc, "missing-id", { x: 99 });

    expect(result.objects).toEqual(doc.objects);
  });

  it("removes the targeted object only", () => {
    const a = createTextObject();
    const b = createTextObject();
    const doc = addObject(addObject(createEmptyDocument(50, 25), a), b);

    const result = removeObject(doc, a.id);

    expect(result.objects).toEqual([b]);
  });
});

describe("duplicateObject", () => {
  it("creates a copy with a new id offset from the original", () => {
    const original = createTextObject({ x: 10, y: 10, text: "Original" });
    const doc = addObject(createEmptyDocument(50, 25), original);

    const result = duplicateObject(doc, original.id);

    expect(result.objects).toHaveLength(2);
    const copy = result.objects[1];
    expect(copy.id).not.toBe(original.id);
    expect(copy.x).toBe(15);
    expect(copy.y).toBe(15);
    expect((copy as typeof original).text).toBe("Original");
  });

  it("is a no-op when the id does not exist", () => {
    const doc = addObject(createEmptyDocument(50, 25), createTextObject());

    const result = duplicateObject(doc, "missing-id");

    expect(result).toBe(doc);
  });
});

describe("reorderObject", () => {
  function docWithThree() {
    const a = createTextObject({ text: "A" });
    const b = createTextObject({ text: "B" });
    const c = createTextObject({ text: "C" });
    let doc = createEmptyDocument(50, 25);
    doc = addObject(doc, a);
    doc = addObject(doc, b);
    doc = addObject(doc, c);
    return { doc, a, b, c };
  }

  it("moves an object to the front (end of the array)", () => {
    const { doc, a } = docWithThree();

    const result = reorderObject(doc, a.id, "front");

    expect(result.objects.map((o) => o.id)).toEqual(
      [doc.objects[1].id, doc.objects[2].id, a.id],
    );
  });

  it("moves an object to the back (start of the array)", () => {
    const { doc, c } = docWithThree();

    const result = reorderObject(doc, c.id, "back");

    expect(result.objects[0].id).toBe(c.id);
  });

  it("moves an object one step forward", () => {
    const { doc, a, b } = docWithThree();

    const result = reorderObject(doc, a.id, "forward");

    expect(result.objects.map((o) => o.id)).toEqual([b.id, a.id, doc.objects[2].id]);
  });

  it("moves an object one step backward", () => {
    const { doc, b, c } = docWithThree();

    const result = reorderObject(doc, c.id, "backward");

    expect(result.objects.map((o) => o.id)).toEqual([doc.objects[0].id, c.id, b.id]);
  });

  it("clamps at the boundary instead of throwing", () => {
    const { doc, a, c } = docWithThree();

    expect(reorderObject(doc, a.id, "backward").objects.map((o) => o.id)[0]).toBe(a.id);
    expect(reorderObject(doc, c.id, "forward").objects.map((o) => o.id).at(-1)).toBe(c.id);
  });

  it("is a no-op when the id does not exist", () => {
    const { doc } = docWithThree();

    const result = reorderObject(doc, "missing-id", "front");

    expect(result).toBe(doc);
  });
});

describe("extractFields", () => {
  it("returns one entry per distinct dynamic field name", () => {
    let doc = createEmptyDocument(50, 25);
    doc = addObject(doc, createDynamicFieldObject({ fieldName: "name", label: "Name" }));
    doc = addObject(doc, createDynamicFieldObject({ fieldName: "sku", label: "SKU", required: false }));
    doc = addObject(doc, createTextObject());
    doc = addObject(doc, createRectObject());
    doc = addObject(doc, createLineObject());
    doc = addObject(doc, createImageObject(1, "/api/uploads/images/1"));

    const fields = extractFields(doc);

    expect(fields).toHaveLength(2);
    expect(fields).toContainEqual({ name: "name", label: "Name", defaultValue: null, required: true });
    expect(fields).toContainEqual({ name: "sku", label: "SKU", defaultValue: null, required: false });
  });

  it("deduplicates repeated field names, keeping the first occurrence", () => {
    let doc = createEmptyDocument(50, 25);
    doc = addObject(doc, createDynamicFieldObject({ fieldName: "name", label: "First" }));
    doc = addObject(doc, createDynamicFieldObject({ fieldName: "name", label: "Second" }));

    const fields = extractFields(doc);

    expect(fields).toHaveLength(1);
    expect(fields[0].label).toBe("First");
  });

  it("adds a field for a barcode bound to a name no text field uses, with its value as default", () => {
    let doc = createEmptyDocument(50, 25);
    doc = addObject(doc, createBarcodeObject({ fieldName: "serial", data: "SN-1" }));
    doc = addObject(doc, createBarcodeObject({ fieldName: "", data: "fixed" }));

    expect(extractFields(doc)).toEqual([{ name: "serial", label: null, defaultValue: "SN-1", required: false }]);
  });

  it("lets a barcode share a text field, keeping the text field's definition", () => {
    let doc = createEmptyDocument(50, 25);
    doc = addObject(doc, createBarcodeObject({ fieldName: "sku", data: "barcode default" }));
    doc = addObject(doc, createDynamicFieldObject({ fieldName: "sku", label: "SKU", defaultValue: "A-1" }));

    expect(extractFields(doc)).toEqual([{ name: "sku", label: "SKU", defaultValue: "A-1", required: true }]);
  });

  it("returns an empty array when there are no dynamic fields", () => {
    const doc = addObject(createEmptyDocument(50, 25), createTextObject());

    expect(extractFields(doc)).toEqual([]);
  });
});

describe("normalizeDocument", () => {
  it("leaves a document with unique ids unchanged", () => {
    const doc = addObject(addObject(createEmptyDocument(50, 25), createTextObject()), createTextObject());

    const result = normalizeDocument(doc);

    expect(result.objects.map((o) => o.id)).toEqual(doc.objects.map((o) => o.id));
  });

  // Regression test: editorJson isn't always produced by this editor's own factories -- the
  // backend's .lbx importer builds it too, and doesn't assign an `id` to each object (the
  // backend's renderer never reads it; it's a frontend-only concern). Objects that all share the
  // same missing/falsy id would otherwise collide as React keys in the canvas.
  it("assigns a fresh id to objects with no id", () => {
    const withoutId = { ...createTextObject(), id: "" };
    const doc = { ...createEmptyDocument(50, 25), objects: [withoutId, { ...withoutId }] };

    const result = normalizeDocument(doc);

    expect(result.objects[0].id).not.toBe("");
    expect(result.objects[1].id).not.toBe("");
    expect(result.objects[0].id).not.toBe(result.objects[1].id);
  });

  it("assigns a fresh id to later objects that duplicate an earlier one", () => {
    const shared = createTextObject();
    const doc = { ...createEmptyDocument(50, 25), objects: [shared, { ...shared }] };

    const result = normalizeDocument(doc);

    expect(result.objects[0].id).toBe(shared.id);
    expect(result.objects[1].id).not.toBe(shared.id);
  });

  it("does not mutate the original document", () => {
    const withoutId = { ...createTextObject(), id: "" };
    const doc = { ...createEmptyDocument(50, 25), objects: [withoutId] };

    normalizeDocument(doc);

    expect(doc.objects[0].id).toBe("");
  });
});
