import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { PrintTemplatePage } from "./PrintTemplatePage";

const mockNavigate = vi.fn();

vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => mockNavigate };
});

function renderPage(id = 5) {
  return render(
    <MemoryRouter initialEntries={[`/templates/${id}/print`]}>
      <Routes>
        <Route path="/templates/:id/print" element={<PrintTemplatePage />} />
      </Routes>
    </MemoryRouter>,
  );
}

const templateDetail = {
  id: 5,
  name: "Shipping Label",
  description: null,
  category: null,
  tags: [],
  status: "Draft",
  sourceLbxUrl: null,
  conversionWarnings: [],
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
  currentVersion: {
    versionNumber: 1,
    widthMm: 50,
    heightMm: 25,
    editorJson: "{}",
    fields: [{ name: "name", label: "Name", defaultValue: "Default Name", required: true }],
    previewImageUrl: null,
    createdAt: "2026-01-01T00:00:00Z",
  },
};

function stubFetchRouter() {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();

      if (url.endsWith("/api/templates/5") && (!init || init.method === undefined)) {
        return { ok: true, json: async () => templateDetail } as Response;
      }

      if (url.endsWith("/api/templates/5/preview")) {
        return { ok: true, blob: async () => new Blob(["png-bytes"], { type: "image/png" }) } as Response;
      }

      if (url.endsWith("/api/print-jobs")) {
        return { ok: true, json: async () => ({ id: 99 }) } as Response;
      }

      if (url.endsWith("/api/printers")) {
        return { ok: true, json: async () => [] } as Response;
      }

      throw new Error(`Unexpected fetch: ${url}`);
    }),
  );
}

describe("PrintTemplatePage", () => {
  beforeEach(() => {
    mockNavigate.mockClear();

    if (!URL.createObjectURL) {
      (URL as unknown as { createObjectURL: () => string }).createObjectURL = () => "blob:mock";
    }
    if (!URL.revokeObjectURL) {
      (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = () => {};
    }

    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:mock");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("loads the template and pre-fills field defaults", async () => {
    stubFetchRouter();

    renderPage();

    expect(await screen.findByText("Print: Shipping Label")).toBeInTheDocument();
    expect(screen.getByLabelText(/name \*/i)).toHaveValue("Default Name");
  });

  it("renders the preview on its own and re-renders it with the typed values", async () => {
    stubFetchRouter();

    renderPage();
    await screen.findByText("Print: Shipping Label");

    await waitFor(() => expect(screen.getByAltText("Label preview")).toHaveAttribute("src", "blob:mock"));

    const previewBodies = () =>
      vi
        .mocked(fetch)
        .mock.calls.filter(([input]) => String(input).endsWith("/api/templates/5/preview"))
        .map(([, init]) => JSON.parse(String(init?.body)));
    expect(previewBodies()).toEqual([{ fieldValues: { name: "Default Name" }, format: "png" }]);

    fireEvent.change(screen.getByLabelText(/name \*/i), { target: { value: "Ada" } });

    await waitFor(() => expect(previewBodies().at(-1)).toEqual({ fieldValues: { name: "Ada" }, format: "png" }));
  });

  it("submits the print job and navigates to the job detail page", async () => {
    stubFetchRouter();

    renderPage();
    await screen.findByText("Print: Shipping Label");

    fireEvent.click(screen.getByRole("button", { name: "Submit Print Job" }));

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith("/print-jobs/99"));
  });

  it("preselects the default printer and submits its id", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === "string" ? input : input.toString();

        if (url.endsWith("/api/templates/5") && (!init || init.method === undefined)) {
          return { ok: true, json: async () => templateDetail } as Response;
        }

        if (url.endsWith("/api/templates/5/preview")) {
          return { ok: true, blob: async () => new Blob(["png-bytes"], { type: "image/png" }) } as Response;
        }

        if (url.endsWith("/api/printers")) {
          return {
            ok: true,
            json: async () => [
              { id: 1, name: "Front Desk", isDefault: false, enabled: true },
              { id: 2, name: "Back Office", isDefault: true, enabled: true },
            ],
          } as Response;
        }

        if (url.endsWith("/api/print-jobs")) {
          const body = JSON.parse((init?.body as string) ?? "{}");
          expect(body.printerId).toBe(2);
          expect(body.printerName).toBeNull();
          return { ok: true, json: async () => ({ id: 99 }) } as Response;
        }

        throw new Error(`Unexpected fetch: ${url}`);
      }),
    );

    renderPage();
    await screen.findByText("Print: Shipping Label");

    expect(await screen.findByDisplayValue(/Back Office \(default\)/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Submit Print Job" }));

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith("/print-jobs/99"));
  });

  it("offers the selected printer's resolutions and submits the chosen quality", async () => {
    let submitted: Record<string, unknown> | null = null;

    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === "string" ? input : input.toString();

        if (url.endsWith("/api/templates/5") && (!init || init.method === undefined)) {
          return { ok: true, json: async () => templateDetail } as Response;
        }

        if (url.endsWith("/api/templates/5/preview")) {
          return { ok: true, blob: async () => new Blob(["png-bytes"], { type: "image/png" }) } as Response;
        }

        if (url.endsWith("/api/printers")) {
          return {
            ok: true,
            json: async () => [
              {
                id: 1,
                name: "P750W",
                isDefault: true,
                enabled: true,
                resolutions: [
                  { quality: "Standard", horizontalDpi: 180, verticalDpi: 180 },
                  { quality: "High", horizontalDpi: 360, verticalDpi: 180 },
                ],
              },
            ],
          } as Response;
        }

        if (url.endsWith("/api/printers/1/status")) {
          return {
            ok: true,
            json: async () => ({ available: true, loadedTapeMm: 12, display: "READY", problem: null }),
          } as Response;
        }

        if (url.endsWith("/api/print-jobs")) {
          submitted = JSON.parse((init?.body as string) ?? "{}");
          return { ok: true, json: async () => ({ id: 99 }) } as Response;
        }

        throw new Error(`Unexpected fetch: ${url}`);
      }),
    );

    renderPage();
    await screen.findByText("Print: Shipping Label");

    // The template is 25 mm high (24 mm tape), but the printer reports 12 mm tape.
    expect(await screen.findByRole("alert")).toHaveTextContent("12 mm tape loaded, but this template needs 24 mm");

    const qualitySelect = await screen.findByLabelText("Print quality");
    expect(screen.getByRole("option", { name: "High (180 × 360 dpi)" })).toBeInTheDocument();
    fireEvent.change(qualitySelect, { target: { value: "High" } });
    fireEvent.click(screen.getByRole("button", { name: "Submit Print Job" }));

    expect(screen.getByLabelText("Cutting")).toHaveValue("AutoCut");
    await waitFor(() => expect(submitted).toMatchObject({ printerId: 1, quality: "High", cutMode: "AutoCut" }));
  });

  it("shows a load error when the template can't be fetched", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false, status: 404, json: async () => null } as Response));

    renderPage();

    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });
});
