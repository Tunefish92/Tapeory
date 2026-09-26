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

  it("renders a preview image after clicking Update Preview", async () => {
    stubFetchRouter();

    renderPage();
    await screen.findByText("Print: Shipping Label");

    fireEvent.click(screen.getByRole("button", { name: "Update Preview" }));

    await waitFor(() => expect(screen.getByAltText("Label preview")).toHaveAttribute("src", "blob:mock"));
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

  it("shows a load error when the template can't be fetched", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false, status: 404, json: async () => null } as Response));

    renderPage();

    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });
});
