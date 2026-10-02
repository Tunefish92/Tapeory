import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ObjectList } from "./ObjectList";
import type { LabelObject } from "./types";

const base = { x: 0, y: 0, rotation: 0, locked: false, hidden: false };
const text = { fontSize: 10, fontFamily: "Inter", fontWeight: "normal", align: "left", fill: "#000", width: 10, height: 4 } as const;

const objects: LabelObject[] = [
  { ...base, ...text, id: "t1", type: "text", text: "Tapeory\n  workshop" },
  { ...base, id: "r1", type: "rect", width: 5, height: 5, fill: "", stroke: "#000", strokeWidth: 0.2, cornerRadius: 0 },
  { ...base, ...text, id: "f1", type: "dynamicField", fieldName: "name", label: "Name", defaultValue: "", required: true },
  { ...base, id: "b1", type: "barcode", symbology: "ean13", data: "4006381333931", fieldName: "gtin", showText: false, width: 20, height: 8, fill: "#000" },
  { ...base, id: "i1", type: "image", uploadedFileId: 3, url: "/x", width: 5, height: 5, hidden: true },
];

describe("ObjectList", () => {
  it("lists what is on the label by name, alphabetically", () => {
    render(<ObjectList objects={objects} selectedId="f1" onSelect={() => {}} onDelete={() => {}} />);

    const names = within(screen.getByRole("list"))
      .getAllByRole("button", { pressed: false })
      .concat(within(screen.getByRole("list")).getAllByRole("button", { pressed: true }))
      .map((button) => button.textContent);
    const inOrder = screen.getAllByRole("listitem").map((item) => item.querySelector(".object-list__name")!.textContent);

    expect(inOrder).toEqual(["Barcode (EAN-13): gtin", "Field: name", "Imagehidden", "Rectangle", "Text: Tapeory workshop"]);
    expect(names).toHaveLength(5);
    expect(screen.getByRole("heading", { name: /Elements/ })).toHaveTextContent("5");
    expect(screen.getByRole("button", { name: "Field: name" })).toHaveAttribute("aria-pressed", "true");
  });

  it("selects an element when its name is clicked, and deletes it with its bin", () => {
    const onSelect = vi.fn();
    const onDelete = vi.fn();
    render(<ObjectList objects={objects} selectedId={null} onSelect={onSelect} onDelete={onDelete} />);

    fireEvent.click(screen.getByRole("button", { name: "Rectangle" }));
    fireEvent.click(screen.getByRole("button", { name: "Delete “Barcode (EAN-13): gtin”" }));

    expect(onSelect).toHaveBeenCalledWith("r1");
    expect(onDelete).toHaveBeenCalledWith("b1");
  });

  it("says so when the label is empty", () => {
    render(<ObjectList objects={[]} selectedId={null} onSelect={() => {}} onDelete={() => {}} />);

    expect(screen.getByText("Nothing on the label yet.")).toBeInTheDocument();
  });
});
