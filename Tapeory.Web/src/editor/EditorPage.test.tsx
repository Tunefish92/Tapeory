import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { EditorPage } from "./EditorPage";

const mockNavigate = vi.fn();

vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => mockNavigate };
});

// jsdom has no 2D canvas context, so Konva's <Stage> (inside LabelCanvas) can't mount here — see
// LabelCanvas.smoke.test.tsx. EditorPage's own logic (load/save/publish, image upload, toolbar
// wiring) doesn't depend on what LabelCanvas renders, so it's stubbed out.
vi.mock("./LabelCanvas", () => ({
  LabelCanvas: () => <div data-testid="label-canvas-stub" />,
}));

function renderPage(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/templates/new" element={<EditorPage />} />
        <Route path="/templates/:id/edit" element={<EditorPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

const templateDetail = {
  id: 5,
  name: "Shipping Label",
  description: "A label",
  category: "Shipping",
  tags: [],
  status: "Draft",
  sourceLbxUrl: null,
  conversionWarnings: [] as string[],
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
  currentVersion: {
    versionNumber: 1,
    widthMm: 62,
    heightMm: 29,
    editorJson: JSON.stringify({ formatVersion: 1, widthMm: 62, heightMm: 29, objects: [] }),
    fields: [],
    previewImageUrl: null,
    createdAt: "2026-01-01T00:00:00Z",
  },
};

function jsonResponse(body: unknown, ok = true, status = ok ? 200 : 400) {
  return { ok, status, json: async () => body } as Response;
}

describe("EditorPage", () => {
  beforeEach(() => {
    mockNavigate.mockClear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("loads an existing template's metadata into the form", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(templateDetail)));

    renderPage("/templates/5/edit");

    expect(await screen.findByDisplayValue("Shipping Label")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Shipping")).toBeInTheDocument();
    expect(screen.getByDisplayValue("A label")).toBeInTheDocument();
    expect(screen.getByDisplayValue("62")).toBeInTheDocument();
    expect(screen.getByDisplayValue("29")).toBeInTheDocument();
  });

  it("shows a load error when the template can't be fetched", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ detail: "Not found." }, false, 404)));

    renderPage("/templates/5/edit");

    expect(await screen.findByRole("alert")).toHaveTextContent("Not found.");
  });

  it("starts blank for a new template and shows no Publish button", async () => {
    renderPage("/templates/new");

    expect(await screen.findByTestId("label-canvas-stub")).toBeInTheDocument();
    expect(screen.getByLabelText("Name")).toHaveValue("");
    expect(screen.queryByRole("button", { name: "Publish" })).not.toBeInTheDocument();
  });

  it("requires a name before saving", async () => {
    vi.stubGlobal("fetch", vi.fn());
    renderPage("/templates/new");
    await screen.findByTestId("label-canvas-stub");

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Name is required.")).toBeInTheDocument();
  });

  it("creates a new template and navigates to its edit route", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();

      if (url.endsWith("/api/templates") && init?.method === "POST") {
        const body = JSON.parse(init.body as string);
        expect(body.name).toBe("New Label");
        return jsonResponse({ ...templateDetail, id: 42, name: "New Label" }, true, 201);
      }

      throw new Error(`Unexpected fetch: ${url} ${init?.method}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    renderPage("/templates/new");
    await screen.findByTestId("label-canvas-stub");

    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "New Label" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith("/templates/42/edit", { replace: true }));
  });

  it("shows a save error when creating a template fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse({ detail: "Name already taken." }, false, 400)),
    );

    renderPage("/templates/new");
    await screen.findByTestId("label-canvas-stub");

    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Dup" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Name already taken.")).toBeInTheDocument();
  });

  it("saves an existing template as a new version and updates its metadata", async () => {
    const calls: string[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();

      if (url.endsWith("/api/templates/5") && (!init || init.method === undefined)) {
        return jsonResponse(templateDetail);
      }

      if (url.endsWith("/api/templates/5/versions") && init?.method === "POST") {
        calls.push("version");
        return jsonResponse(templateDetail.currentVersion);
      }

      if (url.endsWith("/api/templates/5") && init?.method === "PUT") {
        calls.push("metadata");
        return jsonResponse(templateDetail);
      }

      throw new Error(`Unexpected fetch: ${url} ${init?.method}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    renderPage("/templates/5/edit");
    await screen.findByDisplayValue("Shipping Label");

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(calls).toEqual(["version", "metadata"]));
  });

  it("publishes an existing template", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();

      if (url.endsWith("/api/templates/5") && (!init || init.method === undefined)) {
        return jsonResponse(templateDetail);
      }

      if (url.endsWith("/api/templates/5") && init?.method === "PUT") {
        const body = JSON.parse(init.body as string);
        expect(body.status).toBe("Published");
        return jsonResponse({ ...templateDetail, status: "Published" });
      }

      throw new Error(`Unexpected fetch: ${url} ${init?.method}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    renderPage("/templates/5/edit");
    await screen.findByDisplayValue("Shipping Label");

    fireEvent.click(screen.getByRole("button", { name: "Publish" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining("/api/templates/5"),
        expect.objectContaining({ method: "PUT" }),
      ),
    );
  });

  it("shows the .lbx import banner and dismissible conversion warnings", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse({
          ...templateDetail,
          sourceLbxUrl: "/api/templates/5/original-lbx",
          conversionWarnings: ["Could not convert a barcode object."],
        }),
      ),
    );

    renderPage("/templates/5/edit");
    await screen.findByDisplayValue("Shipping Label");

    expect(screen.getByText("Imported from a .lbx file.")).toBeInTheDocument();
    expect(screen.getByText("Could not convert a barcode object.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Dismiss" }));

    expect(screen.queryByText("Could not convert a barcode object.")).not.toBeInTheDocument();
  });

  it("adding an object selects it and shows its properties in the panel", async () => {
    renderPage("/templates/new");
    await screen.findByTestId("label-canvas-stub");

    expect(screen.getByText("Select an object to edit its properties.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Add Text" }));

    expect(screen.queryByText("Select an object to edit its properties.")).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Text" })).toBeInTheDocument();
  });

  it("shows an error message when an image upload fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ detail: "Unsupported content type." }, false, 400)));

    renderPage("/templates/new");
    await screen.findByTestId("label-canvas-stub");

    const file = new File(["fake-bytes"], "logo.png", { type: "image/png" });
    const input = document.querySelector('[data-testid="image-file-input"]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [file] } });

    expect(await screen.findByText("Unsupported content type.")).toBeInTheDocument();
  });
});
