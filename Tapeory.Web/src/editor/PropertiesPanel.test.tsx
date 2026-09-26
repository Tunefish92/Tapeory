import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  createDynamicFieldObject,
  createImageObject,
  createLineObject,
  createRectObject,
  createTextObject,
} from "./document";
import { PropertiesPanel } from "./PropertiesPanel";

// The font picker lists the server's installed fonts.
vi.mock("../api/fonts", () => ({
  listFonts: vi.fn().mockResolvedValue(["Arial", "Helvetica"]),
  ensureFontLoaded: vi.fn().mockResolvedValue(true),
}));

describe("PropertiesPanel", () => {
  it("shows a placeholder when nothing is selected", () => {
    render(
      <PropertiesPanel
        object={null}
        onChange={vi.fn()}
        onDelete={vi.fn()}
        onDuplicate={vi.fn()}
        onReorder={vi.fn()}
      />,
    );

    expect(screen.getByText(/select an object/i)).toBeInTheDocument();
  });

  it("edits a text object's content", () => {
    const onChange = vi.fn();
    const object = createTextObject({ text: "Hello" });

    render(
      <PropertiesPanel
        object={object}
        onChange={onChange}
        onDelete={vi.fn()}
        onDuplicate={vi.fn()}
        onReorder={vi.fn()}
      />,
    );

    fireEvent.change(screen.getByLabelText("Text"), { target: { value: "Updated" } });

    expect(onChange).toHaveBeenCalledWith({ text: "Updated" });
  });

  it("edits a dynamic field's name, label, default value, and required flag", () => {
    const onChange = vi.fn();
    const object = createDynamicFieldObject({ fieldName: "sku", label: "SKU", required: true });

    render(
      <PropertiesPanel
        object={object}
        onChange={onChange}
        onDelete={vi.fn()}
        onDuplicate={vi.fn()}
        onReorder={vi.fn()}
      />,
    );

    fireEvent.change(screen.getByLabelText("Field name"), { target: { value: "orderNumber" } });
    expect(onChange).toHaveBeenCalledWith({ fieldName: "orderNumber" });

    fireEvent.click(screen.getByLabelText("Required"));
    expect(onChange).toHaveBeenCalledWith({ required: false });
  });

  it("edits rectangle dimensions and appearance", () => {
    const onChange = vi.fn();
    const object = createRectObject({ width: 20, height: 10 });

    render(
      <PropertiesPanel
        object={object}
        onChange={onChange}
        onDelete={vi.fn()}
        onDuplicate={vi.fn()}
        onReorder={vi.fn()}
      />,
    );

    fireEvent.change(screen.getByLabelText("Width (mm)"), { target: { value: "30" } });

    expect(onChange).toHaveBeenCalledWith({ width: 30 });
  });

  it("edits text font, alignment, color, and bold", async () => {
    const onChange = vi.fn();
    const object = createTextObject({ fontFamily: "Arial", fontWeight: "normal", align: "left" });

    render(
      <PropertiesPanel
        object={object}
        onChange={onChange}
        onDelete={vi.fn()}
        onDuplicate={vi.fn()}
        onReorder={vi.fn()}
      />,
    );

    fireEvent.change(screen.getByLabelText("Font size (pt)"), { target: { value: "14" } });
    expect(onChange).toHaveBeenCalledWith({ fontSize: 14 });

    // The label also holds a font preview line, so match its name by prefix; the select stays
    // disabled until the font list has loaded.
    const fontFamily = screen.getByRole("combobox", { name: /^Font family/ });
    await waitFor(() => expect(fontFamily).toBeEnabled());
    fireEvent.change(fontFamily, { target: { value: "Helvetica" } });
    expect(onChange).toHaveBeenCalledWith({ fontFamily: "Helvetica" });

    fireEvent.click(screen.getByLabelText("Bold"));
    expect(onChange).toHaveBeenCalledWith({ fontWeight: "bold" });

    fireEvent.change(screen.getByLabelText("Align"), { target: { value: "center" } });
    expect(onChange).toHaveBeenCalledWith({ align: "center" });

    fireEvent.change(screen.getByLabelText("Color"), { target: { value: "#ff0000" } });
    expect(onChange).toHaveBeenCalledWith({ fill: "#ff0000" });
  });

  it("edits rectangle fill, stroke, stroke width, and corner radius", () => {
    const onChange = vi.fn();
    const object = createRectObject();

    render(
      <PropertiesPanel
        object={object}
        onChange={onChange}
        onDelete={vi.fn()}
        onDuplicate={vi.fn()}
        onReorder={vi.fn()}
      />,
    );

    fireEvent.change(screen.getByLabelText("Fill"), { target: { value: "#00ff00" } });
    expect(onChange).toHaveBeenCalledWith({ fill: "#00ff00" });

    fireEvent.change(screen.getByLabelText("Stroke"), { target: { value: "#0000ff" } });
    expect(onChange).toHaveBeenCalledWith({ stroke: "#0000ff" });

    fireEvent.change(screen.getByLabelText("Stroke width (mm)"), { target: { value: "2" } });
    expect(onChange).toHaveBeenCalledWith({ strokeWidth: 2 });

    fireEvent.change(screen.getByLabelText("Corner radius (mm)"), { target: { value: "3" } });
    expect(onChange).toHaveBeenCalledWith({ cornerRadius: 3 });
  });

  it("edits a line's stroke and stroke width", () => {
    const onChange = vi.fn();
    const object = createLineObject();

    render(
      <PropertiesPanel
        object={object}
        onChange={onChange}
        onDelete={vi.fn()}
        onDuplicate={vi.fn()}
        onReorder={vi.fn()}
      />,
    );

    expect(screen.getByRole("heading", { name: "Line" })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Stroke width (mm)"), { target: { value: "1.5" } });
    expect(onChange).toHaveBeenCalledWith({ strokeWidth: 1.5 });

    fireEvent.change(screen.getByLabelText("Stroke"), { target: { value: "#123456" } });
    expect(onChange).toHaveBeenCalledWith({ stroke: "#123456" });
  });

  it("edits an image's width and height", () => {
    const onChange = vi.fn();
    const object = createImageObject(1, "/api/uploads/images/1");

    render(
      <PropertiesPanel
        object={object}
        onChange={onChange}
        onDelete={vi.fn()}
        onDuplicate={vi.fn()}
        onReorder={vi.fn()}
      />,
    );

    expect(screen.getByRole("heading", { name: "Image" })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Height (mm)"), { target: { value: "40" } });
    expect(onChange).toHaveBeenCalledWith({ height: 40 });
  });

  it("toggles locked and hidden", () => {
    const onChange = vi.fn();
    const object = createTextObject({ locked: false, hidden: false });

    render(
      <PropertiesPanel
        object={object}
        onChange={onChange}
        onDelete={vi.fn()}
        onDuplicate={vi.fn()}
        onReorder={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByLabelText("Locked"));
    expect(onChange).toHaveBeenCalledWith({ locked: true });

    fireEvent.click(screen.getByLabelText("Hidden"));
    expect(onChange).toHaveBeenCalledWith({ hidden: true });
  });

  it("wires up duplicate, delete, and reorder actions", () => {
    const onDelete = vi.fn();
    const onDuplicate = vi.fn();
    const onReorder = vi.fn();
    const object = createTextObject();

    render(
      <PropertiesPanel
        object={object}
        onChange={vi.fn()}
        onDelete={onDelete}
        onDuplicate={onDuplicate}
        onReorder={onReorder}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Duplicate" }));
    fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    fireEvent.click(screen.getByRole("button", { name: "Bring to Front" }));
    fireEvent.click(screen.getByRole("button", { name: "Send to Back" }));
    fireEvent.click(screen.getByRole("button", { name: "Forward" }));
    fireEvent.click(screen.getByRole("button", { name: "Backward" }));

    expect(onDuplicate).toHaveBeenCalledOnce();
    expect(onDelete).toHaveBeenCalledOnce();
    expect(onReorder).toHaveBeenNthCalledWith(1, "front");
    expect(onReorder).toHaveBeenNthCalledWith(2, "back");
    expect(onReorder).toHaveBeenNthCalledWith(3, "forward");
    expect(onReorder).toHaveBeenNthCalledWith(4, "backward");
  });
});
