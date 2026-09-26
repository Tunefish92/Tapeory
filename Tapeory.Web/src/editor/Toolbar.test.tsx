import { fireEvent, render, screen } from "@testing-library/react";
import type { ComponentProps } from "react";
import { describe, expect, it, vi } from "vitest";
import { Toolbar } from "./Toolbar";

function renderToolbar(overrides: Partial<ComponentProps<typeof Toolbar>> = {}) {
  const props: ComponentProps<typeof Toolbar> = {
    onAddObject: vi.fn(),
    onAddImage: vi.fn(),
    onUndo: vi.fn(),
    onRedo: vi.fn(),
    canUndo: false,
    canRedo: false,
    zoom: 2,
    onZoomChange: vi.fn(),
    previewMode: false,
    onTogglePreview: vi.fn(),
    onSave: vi.fn(),
    saving: false,
    saveError: null,
    ...overrides,
  };

  render(<Toolbar {...props} />);
  return props;
}

describe("Toolbar", () => {
  it("calls onAddObject with the right type for each add button", () => {
    const props = renderToolbar();

    fireEvent.click(screen.getByRole("button", { name: "Add Text" }));
    fireEvent.click(screen.getByRole("button", { name: "Add Field" }));
    fireEvent.click(screen.getByRole("button", { name: "Add Rectangle" }));
    fireEvent.click(screen.getByRole("button", { name: "Add Line" }));

    expect(props.onAddObject).toHaveBeenNthCalledWith(1, "text");
    expect(props.onAddObject).toHaveBeenNthCalledWith(2, "dynamicField");
    expect(props.onAddObject).toHaveBeenNthCalledWith(3, "rect");
    expect(props.onAddObject).toHaveBeenNthCalledWith(4, "line");
  });

  it("forwards the chosen file to onAddImage", () => {
    const props = renderToolbar();
    const file = new File(["bytes"], "logo.png", { type: "image/png" });

    const input = screen.getByTestId("image-file-input") as HTMLInputElement;
    fireEvent.change(input, { target: { files: [file] } });

    expect(props.onAddImage).toHaveBeenCalledWith(file);
  });

  it("disables undo/redo based on canUndo/canRedo", () => {
    renderToolbar({ canUndo: false, canRedo: true });

    expect(screen.getByRole("button", { name: "Undo" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Redo" })).toBeEnabled();
  });

  it("clamps zoom changes within [MIN_ZOOM, MAX_ZOOM]", () => {
    const props = renderToolbar({ zoom: 4 });

    fireEvent.click(screen.getByRole("button", { name: "Zoom in" }));

    expect(props.onZoomChange).toHaveBeenCalledWith(4); // already at MAX_ZOOM
  });

  it("shows the zoom percentage", () => {
    renderToolbar({ zoom: 1.5 });

    expect(screen.getByText("150%")).toBeInTheDocument();
  });

  it("disables add-object buttons in preview mode", () => {
    renderToolbar({ previewMode: true });

    expect(screen.getByRole("button", { name: "Add Text" })).toBeDisabled();
  });

  it("toggles the preview/edit button label", () => {
    const props = renderToolbar({ previewMode: false });

    fireEvent.click(screen.getByRole("button", { name: "Preview" }));

    expect(props.onTogglePreview).toHaveBeenCalledOnce();
  });

  it("shows Saving… and disables Save while saving", () => {
    renderToolbar({ saving: true });

    const button = screen.getByRole("button", { name: "Saving…" });
    expect(button).toBeDisabled();
  });

  it("shows the save error as an alert", () => {
    renderToolbar({ saveError: "Name is required." });

    expect(screen.getByRole("alert")).toHaveTextContent("Name is required.");
  });

  it("omits the Publish button when onPublish is not provided (new, unsaved template)", () => {
    renderToolbar({ onPublish: undefined });

    expect(screen.queryByRole("button", { name: "Publish" })).not.toBeInTheDocument();
  });

  it("calls onPublish when the Publish button is clicked", () => {
    const onPublish = vi.fn();
    renderToolbar({ onPublish });

    fireEvent.click(screen.getByRole("button", { name: "Publish" }));

    expect(onPublish).toHaveBeenCalledOnce();
  });
});
