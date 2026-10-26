import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { BulkPrintPage } from "./BulkPrintPage";
import type { PrintData } from "../api/printData";
import type { BulkPrintProfile, BulkPrintProfileSettings } from "../api/bulkPrintProfiles";

/** What the (fake) server has saved. */
const savedProfiles: BulkPrintProfile[] = [];

const mockNavigate = vi.fn();

vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => mockNavigate };
});

// The file goes up with XMLHttpRequest (for upload progress); the tests answer in its place.
const parsePrintData = vi.fn();

vi.mock("../api/printData", async () => {
  const actual = await vi.importActual<typeof import("../api/printData")>("../api/printData");
  return { ...actual, parsePrintData: (...args: unknown[]) => parsePrintData(...args) };
});

const templateDetail = {
  id: 5,
  name: "Shelf label",
  currentVersion: {
    versionNumber: 1,
    widthMm: 50,
    heightMm: 9,
    editorJson: "{}",
    fields: [
      { name: "name", label: "Name", defaultValue: null, required: true },
      { name: "sku", label: "Article", defaultValue: null, required: true },
    ],
  },
};

function parsed(overrides: Partial<PrintData>): PrintData {
  return {
    kind: "text",
    sheets: [],
    sheet: 0,
    separator: ";",
    separatorDetected: true,
    hasHeader: true,
    rows: [],
    fields: {},
    quantityColumn: null,
    ...overrides,
  };
}

/** Answers check-rows: a row whose name is "bad" has an error, "long" a warning. */
function stubFetch() {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);

      if (url.endsWith("/api/templates/5")) return { ok: true, json: async () => templateDetail } as Response;
      if (url.endsWith("/api/templates/5/bulk-print-profiles")) {
        if (init?.method === "PUT") {
          const body = JSON.parse(String(init.body)) as { name: string; settings: BulkPrintProfileSettings; replace: boolean };
          if (!body.replace && savedProfiles.some((profile) => profile.name.toLowerCase() === body.name.toLowerCase())) {
            return { ok: false, status: 409, json: async () => ({ detail: "A profile with this name already exists." }) } as Response;
          }
          savedProfiles.splice(0, savedProfiles.length, { id: 3, templateId: 5, updatedAt: "2026-10-02T00:00:00Z", ...body });
          return { ok: true, json: async () => savedProfiles[0] } as Response;
        }
        return { ok: true, json: async () => [...savedProfiles] } as Response;
      }
      if (url.endsWith("/api/bulk-print-profiles/3") && init?.method === "DELETE") {
        savedProfiles.length = 0;
        return { ok: true, status: 204 } as Response;
      }
      if (url.endsWith("/api/printers")) return { ok: true, json: async () => [] } as Response;
      if (url.endsWith("/api/templates/5/preview")) return { ok: true, blob: async () => new Blob(["png"]) } as Response;

      if (url.endsWith("/api/templates/5/check-rows")) {
        const rows = (JSON.parse(String(init?.body)) as { rows: Record<string, string>[] }).rows;
        return {
          ok: true,
          json: async () => ({
            rows: rows.map((row) => ({
              errors: row.name === "bad" ? ["Barcode can't be encoded."] : [],
              warnings: row.name === "long" ? ["Text is cut off."] : [],
            })),
          }),
        } as Response;
      }

      if (url.endsWith("/api/print-data/fetch")) {
        return {
          ok: true,
          json: async () =>
            parsed({ kind: "json", separator: null, rows: [["name", "sku"], ["Box", "A-1"], ["Bag", "A-2"]], fields: { name: 0, sku: 1 } }),
        } as Response;
      }

      if (url.endsWith("/api/print-jobs")) return { ok: true, json: async () => ({ id: 77 }) } as Response;

      throw new Error(`Unexpected fetch: ${url}`);
    }),
  );
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/templates/5/bulk-print"]}>
      <Routes>
        <Route path="/templates/:id/bulk-print" element={<BulkPrintPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

async function chooseFile(name = "stock.csv") {
  await screen.findByText("Bulk print: Shelf label");
  fireEvent.change(screen.getByTestId("bulk-file-input"), { target: { files: [new File(["x"], name)] } });
}

const printJobBody = () =>
  JSON.parse(String(vi.mocked(fetch).mock.calls.find(([input]) => String(input).endsWith("/api/print-jobs"))?.[1]?.body));

describe("BulkPrintPage", () => {
  beforeEach(() => {
    mockNavigate.mockClear();
    parsePrintData.mockReset();
    savedProfiles.length = 0;
    localStorage.clear();
    stubFetch();
    (URL as unknown as { createObjectURL: () => string }).createObjectURL = () => "blob:mock";
    (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = () => {};
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("goes straight to the check when the header names every field, and prints the rows as one job", async () => {
    parsePrintData.mockResolvedValue(
      parsed({
        rows: [
          ["Name", "Article", "Copies"],
          ["Box", "A-1", "2"],
          ["Bag", "A-2", ""],
        ],
        fields: { name: 0, sku: 1 },
        quantityColumn: 2,
      }),
    );

    renderPage();
    await chooseFile();

    expect(await screen.findByText(/3 labels will be printed/)).toBeInTheDocument();
    expect(screen.getByText(/about 150 mm of 9 mm tape/)).toBeInTheDocument();
    await waitFor(() => expect(screen.getByAltText("Label 1 of 2")).toHaveAttribute("src", "blob:mock"));

    fireEvent.click(screen.getByRole("button", { name: /Next/ }));
    expect(await screen.findByAltText("Label 2 of 2")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Continue to printing" }));
    fireEvent.click(screen.getByRole("button", { name: "Print 3 labels" }));

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith("/print-jobs/77"));
    expect(printJobBody().items).toEqual([
      { fieldValues: { name: "Box", sku: "A-1" }, quantity: 2 },
      { fieldValues: { name: "Bag", sku: "A-2" }, quantity: 1 },
    ]);
  });

  it("asks which column belongs to which field when the file has no header", async () => {
    parsePrintData.mockResolvedValue(
      parsed({
        hasHeader: false,
        rows: [
          ["A-1", "Box"],
          ["A-2", "Bag"],
        ],
      }),
    );

    renderPage();
    await chooseFile();

    expect(await screen.findByText("No header row was found. Which column belongs to which field?")).toBeInTheDocument();
    const next = screen.getByRole("button", { name: "Check the labels" });
    expect(next).toBeDisabled();
    expect(screen.getByText("Still needed: a column for Name, Article.")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Name *"), { target: { value: "1" } });
    fireEvent.change(screen.getByLabelText("Article *"), { target: { value: "0" } });
    expect(next).toBeEnabled();
    fireEvent.click(next);

    expect(await screen.findByText(/2 labels will be printed/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Continue to printing" }));
    fireEvent.click(screen.getByRole("button", { name: "Print 2 labels" }));

    await waitFor(() => expect(mockNavigate).toHaveBeenCalled());
    expect(printJobBody().items[0]).toEqual({ fieldValues: { name: "Box", sku: "A-1" }, quantity: 1 });
  });

  it("asks for the separator when it couldn't be detected, and reads the file again with the answer", async () => {
    parsePrintData.mockResolvedValueOnce(
      parsed({ separator: "none", separatorDetected: false, hasHeader: false, rows: [["Smith, Anna;12"], ["Jones, Ben;7"]] }),
    );
    parsePrintData.mockResolvedValueOnce(
      parsed({
        separator: ";",
        hasHeader: false,
        rows: [
          ["Smith, Anna", "12"],
          ["Jones, Ben", "7"],
        ],
      }),
    );

    renderPage();
    await chooseFile("names.txt");

    expect(await screen.findByText("Which separator does the file use?")).toBeInTheDocument();
    expect(screen.queryByLabelText("Name *")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Check the labels" })).toBeDisabled();

    fireEvent.click(screen.getByLabelText("Semicolon ( ; )"));

    expect(await screen.findByLabelText("Name *")).toBeInTheDocument();
    expect(parsePrintData.mock.calls[1][2]).toMatchObject({ separator: ";" });
    expect(screen.getByLabelText("Semicolon ( ; )")).toBeChecked();
    expect(screen.getByRole("cell", { name: "Smith, Anna" })).toBeInTheDocument();
  });

  it("leaves rows with errors out, keeps rows with warnings, and lets a row be unticked", async () => {
    parsePrintData.mockResolvedValue(
      parsed({
        rows: [
          ["Name", "Article"],
          ["Box", "1"],
          ["bad", "2"],
          ["long", "3"],
        ],
        fields: { name: 0, sku: 1 },
      }),
    );

    renderPage();
    await chooseFile();

    expect(await screen.findByText(/2 labels will be printed/)).toBeInTheDocument();
    expect(screen.getByText("1 row has an error and is left out.")).toBeInTheDocument();
    expect(screen.getByLabelText("Print row 2")).toBeDisabled();
    expect(screen.getByLabelText("Print row 3")).toBeChecked();

    fireEvent.click(screen.getByLabelText("Only rows with problems (2)"));
    expect(screen.queryByRole("cell", { name: "Box" })).not.toBeInTheDocument();
    expect(within(screen.getByRole("table")).getByText("Barcode can't be encoded.")).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText("Print row 3"));
    expect(screen.getByText(/1 label will be printed/)).toBeInTheDocument();
  });

  it("says why a file couldn't be read", async () => {
    parsePrintData.mockRejectedValue(new Error("The file has no data."));

    renderPage();
    await chooseFile();

    expect(await screen.findByRole("alert")).toHaveTextContent("The file has no data.");
    expect(screen.getByTestId("bulk-file-input")).toBeInTheDocument();
  });

  it("refuses a file over the size limit before sending it", async () => {
    renderPage();
    await screen.findByText("Bulk print: Shelf label");
    const huge = new File(["x"], "huge.csv");
    Object.defineProperty(huge, "size", { value: 100 * 1024 * 1024 + 1 });

    fireEvent.change(screen.getByTestId("bulk-file-input"), { target: { files: [huge] } });

    expect(await screen.findByRole("alert")).toHaveTextContent("The file is larger than 100 MB.");
    expect(parsePrintData).not.toHaveBeenCalled();
  });

  it("shows a long file a page at a time, and the table follows the label being looked at", async () => {
    const rows = [["Name", "Article"], ...Array.from({ length: 250 }, (_, i) => [`Item ${i + 1}`, `A-${i + 1}`])];
    parsePrintData.mockResolvedValue(parsed({ rows, fields: { name: 0, sku: 1 } }));

    renderPage();
    await chooseFile();

    expect(await screen.findByText(/250 labels will be printed/)).toBeInTheDocument();
    expect(screen.getByText("Rows 1–100 of 250")).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Item 100" })).toBeInTheDocument();
    expect(screen.queryByRole("cell", { name: "Item 101" })).not.toBeInTheDocument();

    fireEvent.change(screen.getByRole("spinbutton", { name: /^Label/ }), { target: { value: "205" } });

    expect(await screen.findByText("Rows 201–250 of 250")).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Item 205" })).toBeInTheDocument();
  });

  it("saves the setup as a profile and loads it again with the file", async () => {
    parsePrintData.mockResolvedValue(
      parsed({
        hasHeader: false,
        separator: "tab",
        rows: [
          ["A-1", "Box", "2"],
          ["A-2", "Bag", "1"],
        ],
      }),
    );

    const first = renderPage();
    await chooseFile("stock.txt");
    fireEvent.change(await screen.findByLabelText("Name *"), { target: { value: "1" } });
    fireEvent.change(screen.getByLabelText("Article *"), { target: { value: "0" } });
    fireEvent.change(screen.getByLabelText("Copies per row"), { target: { value: "2" } });
    fireEvent.click(screen.getByRole("button", { name: "Check the labels" }));
    await screen.findByText(/3 labels will be printed/);

    expect(screen.getByRole("button", { name: "Save profile" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Save as profile"), { target: { value: "Weekly stock" } });
    fireEvent.click(screen.getByRole("button", { name: "Save profile" }));

    expect(await screen.findByText(/Profile “Weekly stock” saved/)).toBeInTheDocument();
    expect(savedProfiles[0].settings).toMatchObject({
      fileName: "stock.txt",
      separator: "tab",
      hasHeader: false,
      columns: [
        { field: "name", column: 1, header: null },
        { field: "sku", column: 0, header: null },
      ],
      quantityColumn: 2,
      printerId: null,
    });

    // A new visit: the profile is offered, asks for its file (this browser can't reopen it), and
    // goes straight to the check with everything set.
    first.unmount();
    localStorage.clear();
    parsePrintData.mockClear();
    renderPage();
    await screen.findByText("Bulk print: Shelf label");
    fireEvent.click(await screen.findByRole("button", { name: /^Weekly stock/ }));

    expect(await screen.findByText(/Choose the file for the profile “Weekly stock” \(stock.txt\)/)).toBeInTheDocument();
    fireEvent.change(screen.getByTestId("bulk-file-input"), { target: { files: [new File(["x"], "stock.txt")] } });

    expect(await screen.findByText(/3 labels will be printed/)).toBeInTheDocument();
    expect(parsePrintData.mock.calls[0][2]).toMatchObject({ separator: "tab" });
    expect(screen.getByLabelText("Save as profile")).toHaveValue("Weekly stock");

    fireEvent.click(screen.getByRole("button", { name: "Continue to printing" }));
    fireEvent.click(screen.getByRole("button", { name: "Print 3 labels" }));
    await waitFor(() => expect(mockNavigate).toHaveBeenCalled());
    expect(printJobBody().items[0]).toEqual({ fieldValues: { name: "Box", sku: "A-1" }, quantity: 2 });
  });

  it("deletes a profile after asking", async () => {
    savedProfiles.push({
      id: 3,
      templateId: 5,
      name: "Old list",
      updatedAt: "2026-10-02T00:00:00Z",
      settings: { fileName: "old.csv", filePath: "/home/ada/old.csv" } as BulkPrintProfileSettings,
    });
    vi.spyOn(window, "confirm").mockReturnValue(true);

    renderPage();
    expect(await screen.findByText("/home/ada/old.csv")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Delete the profile “Old list”" }));

    await waitFor(() => expect(screen.queryByText("Old list")).not.toBeInTheDocument());
    expect(window.confirm).toHaveBeenCalledWith("Delete the profile “Old list”?");
  });

  it("prints each label as many times as set, on top of a row's own copies", async () => {
    parsePrintData.mockResolvedValue(
      parsed({
        rows: [
          ["Name", "Article", "Copies"],
          ["Box", "A-1", "2"],
          ["Bag", "A-2", ""],
        ],
        fields: { name: 0, sku: 1 },
        quantityColumn: 2,
      }),
    );

    renderPage();
    await chooseFile();
    await screen.findByText(/3 labels will be printed/);

    fireEvent.change(screen.getByLabelText(/Copies of each label/), { target: { value: "3" } });

    expect(screen.getByText(/9 labels will be printed/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Continue to printing" }));
    fireEvent.click(screen.getByRole("button", { name: "Print 9 labels" }));

    await waitFor(() => expect(mockNavigate).toHaveBeenCalled());
    expect(printJobBody().items.map((item: { quantity: number }) => item.quantity)).toEqual([6, 3]);
  });

  it("takes a file dropped anywhere on the page", async () => {
    parsePrintData.mockResolvedValue(parsed({ rows: [["Name", "Article"], ["Box", "A-1"]], fields: { name: 0, sku: 1 } }));

    const { container } = renderPage();
    await screen.findByText("Bulk print: Shelf label");
    const page = container.querySelector("section")!;
    const dropped = new File(["x"], "dropped.csv");
    const dataTransfer = { types: ["Files"], files: [dropped], items: [{ kind: "file", getAsFile: () => dropped }], dropEffect: "none" };

    fireEvent.dragOver(page, { dataTransfer });
    expect(screen.getByText("Drop the file to load it")).toBeInTheDocument();
    fireEvent.drop(page, { dataTransfer });

    expect(await screen.findByText(/1 label will be printed/)).toBeInTheDocument();
    expect(parsePrintData.mock.calls[0][1]).toBe(dropped);
  });

  it("asks before saving over a profile with the same name", async () => {
    savedProfiles.push({
      id: 3,
      templateId: 5,
      name: "Weekly stock",
      updatedAt: "2026-10-02T00:00:00Z",
      settings: { fileName: "old.csv", filePath: null } as BulkPrintProfileSettings,
    });
    parsePrintData.mockResolvedValue(parsed({ rows: [["Name", "Article"], ["Box", "A-1"]], fields: { name: 0, sku: 1 } }));
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);

    renderPage();
    await chooseFile("new.csv");
    await screen.findByText(/1 label will be printed/);
    fireEvent.change(screen.getByLabelText("Save as profile"), { target: { value: "weekly stock" } });

    fireEvent.click(screen.getByRole("button", { name: "Save profile" }));
    await waitFor(() => expect(confirm).toHaveBeenCalledWith("A profile named “weekly stock” already exists. Replace it?"));
    await waitFor(() => expect(screen.getByRole("button", { name: "Save profile" })).toBeEnabled());
    expect(savedProfiles[0].settings.fileName).toBe("old.csv");

    confirm.mockReturnValue(true);
    fireEvent.click(screen.getByRole("button", { name: "Save profile" }));

    expect(await screen.findByText(/Profile “weekly stock” saved/)).toBeInTheDocument();
    expect(savedProfiles[0].settings.fileName).toBe("new.csv");
  });

  it("gets the data from a web address, and a profile for it loads with one click", async () => {
    const first = renderPage();
    await screen.findByText("Bulk print: Shelf label");
    // The first step has one tab per way of importing; the file tab comes first.
    expect(screen.getByRole("tab", { name: "File" })).toHaveAttribute("aria-selected", "true");
    expect(screen.queryByLabelText("Web address")).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Saved profiles: File" })).toBeInTheDocument();
    expect(screen.getByText("No profiles here yet. Save one on the Check or Print step.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("tab", { name: "API" }));
    expect(screen.queryByTestId("bulk-file-input")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Load data" })).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Web address"), { target: { value: " https://erp.example/api/items " } });
    fireEvent.change(screen.getByLabelText("Header name, e.g. Authorization"), { target: { value: "X-Api-Key" } });
    fireEvent.change(screen.getByLabelText("Header value"), { target: { value: "secret" } });
    fireEvent.click(screen.getByRole("button", { name: "Load data" }));

    expect(await screen.findByText(/2 labels will be printed/)).toBeInTheDocument();
    const fetchBody = () =>
      JSON.parse(String(vi.mocked(fetch).mock.calls.filter(([input]) => String(input).endsWith("/api/print-data/fetch")).at(-1)?.[1]?.body));
    expect(fetchBody()).toMatchObject({ templateId: 5, url: "https://erp.example/api/items", headerName: "X-Api-Key", headerValue: "secret" });

    fireEvent.change(screen.getByLabelText("Save as profile"), { target: { value: "ERP items" } });
    fireEvent.click(screen.getByRole("button", { name: "Save profile" }));
    expect(await screen.findByText("Profile “ERP items” saved.")).toBeInTheDocument();
    expect(savedProfiles[0].settings).toMatchObject({
      fileName: null,
      url: "https://erp.example/api/items",
      urlHeaderName: "X-Api-Key",
      urlHeaderValue: "secret",
      hasHeader: true,
    });

    // A new visit: one click on the profile gets the data again and goes to the check.
    first.unmount();
    vi.mocked(fetch).mockClear();
    renderPage();
    await screen.findByText("Bulk print: Shelf label");
    // The profiles card lists the profiles of the open tab: the address profile is under API.
    expect(screen.queryByRole("button", { name: /^ERP items/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("tab", { name: "API" }));
    expect(screen.getByRole("heading", { name: "Saved profiles: API" })).toBeInTheDocument();
    fireEvent.click(await screen.findByRole("button", { name: /^ERP items/ }));

    expect(await screen.findByText(/2 labels will be printed/)).toBeInTheDocument();
    expect(fetchBody()).toMatchObject({ url: "https://erp.example/api/items", headerName: "X-Api-Key", headerValue: "secret" });
    expect(parsePrintData).not.toHaveBeenCalled();
  });

  it("keeps the property names of JSON records as the header", async () => {
    parsePrintData.mockResolvedValue(parsed({ kind: "json", separator: null, rows: [["title", "code"], ["Box", "A-1"]] }));

    renderPage();
    await chooseFile("items.json");

    expect(await screen.findByLabelText("The first row is a header (don't print it)")).toBeDisabled();
    expect(screen.getByLabelText("The first row is a header (don't print it)")).toBeChecked();
    expect(screen.queryByText("Separator")).not.toBeInTheDocument();
    expect(screen.getAllByRole("option", { name: "title (column A)" }).length).toBeGreaterThan(0);
  });
});
