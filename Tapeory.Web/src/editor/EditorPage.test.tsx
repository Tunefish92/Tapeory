import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { EditorPage } from "./EditorPage";
import { AuthContext, type AuthContextValue } from "../auth/AuthContext";

const signedIn: AuthContextValue = {
  hasUsers: true,
  user: { id: 2, userName: "ada", displayName: "Ada", role: "User", mustChangePassword: false },
  openAccess: false,
  canAdminister: false,
  signOut: async () => {},
  startCreatingAccount: () => {},
  refresh: async () => {},
};

function renderSignedIn(path: string) {
  return render(
    <AuthContext.Provider value={signedIn}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/templates/:id/edit" element={<EditorPage />} />
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  );
}

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
    // 29 mm high is the 29 mm continuous roll (DK-11209 runs 62 mm across the roll instead).
    expect(screen.getByLabelText("Height (mm)")).toHaveDisplayValue("29 mm · DK-22210");
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

  it("zooms with the mouse wheel over the canvas, within the same limits as the buttons", async () => {
    renderPage("/templates/new");
    const canvas = await screen.findByTestId("label-canvas-stub");
    const area = canvas.closest(".editor-canvas-scroll")!;

    expect(screen.getByText("200%")).toBeInTheDocument();
    fireEvent.wheel(area, { deltaY: -100 });
    expect(screen.getByText("225%")).toBeInTheDocument();

    fireEvent.wheel(area, { deltaY: 100 });
    fireEvent.wheel(area, { deltaY: 100 });
    expect(screen.getByText("175%")).toBeInTheDocument();

    for (let i = 0; i < 20; i++) fireEvent.wheel(area, { deltaY: 100 });
    expect(screen.getByText("50%")).toBeInTheDocument();
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
  it("opens someone else's public template read-only, with a way to duplicate it", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith("/templates/5/duplicate") && init?.method === "POST") return jsonResponse({ ...templateDetail, id: 9 });
      if (url.endsWith("/templates/5")) return jsonResponse({ ...templateDetail, isPublic: true, ownerName: "Grace", canEdit: false });
      return jsonResponse([]);
    });
    vi.stubGlobal("fetch", fetchMock);

    renderSignedIn("/templates/5/edit");

    expect(await screen.findByText(/This is Grace's template/)).toBeInTheDocument();
    expect(screen.getByDisplayValue("Shipping Label")).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add Text" })).not.toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "Visibility" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Duplicate to edit" }));
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith("/templates/9/edit"));
  });

  it("lets the owner make a template public", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith("/templates/5/visibility")) {
        return jsonResponse({ ...templateDetail, isPublic: JSON.parse(String(init?.body)).isPublic, ownerName: "Ada", canEdit: true });
      }
      if (url.endsWith("/templates/5")) return jsonResponse({ ...templateDetail, isPublic: false, ownerName: "Ada", canEdit: true, isMine: true });
      return jsonResponse([]);
    });
    vi.stubGlobal("fetch", fetchMock);

    renderSignedIn("/templates/5/edit");

    const visibility = await screen.findByRole("group", { name: "Visibility" });
    expect(screen.getByRole("button", { name: "Private" })).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(screen.getByRole("button", { name: "Public" }));

    await waitFor(() => expect(screen.getByRole("button", { name: "Public" })).toHaveAttribute("aria-pressed", "true"));
    expect(visibility).toBeInTheDocument();
    const call = fetchMock.mock.calls.find(([url]) => String(url).endsWith("/visibility"));
    expect(call?.[1]?.method).toBe("PUT");
  });
});
