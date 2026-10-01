import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { PrintersPage } from "./PrintersPage";

function renderPage() {
  return render(
    <MemoryRouter>
      <PrintersPage />
    </MemoryRouter>,
  );
}

function jsonResponse(body: unknown, ok = true, status = ok ? 200 : 400) {
  return { ok, status, json: async () => body } as Response;
}

const printerA = {
  id: 1,
  name: "Front Desk",
  model: "Brother QL-820NWB",
  connectionType: "IpAddress",
  address: "192.168.1.50",
  port: 9100,
  printServerAddress: null,
  usbIdentifier: null,
  labelMediaWidthMm: 62,
  labelMediaHeightMm: 29,
  isDefault: true,
  enabled: true,
  lastConnectionStatus: "Unknown",
  lastConnectionCheckedAt: null,
  lastErrorMessage: null,
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
};

describe("PrintersPage", () => {
  beforeEach(() => {
    vi.stubGlobal("confirm", vi.fn().mockReturnValue(true));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows a loading state, then an empty message when there are no printers", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([])));

    renderPage();

    expect(screen.getByText(/loading printers/i)).toBeInTheDocument();
    expect(await screen.findByText(/no printers configured yet/i)).toBeInTheDocument();
  });

  it("lists printers with status and default badges", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([printerA])));

    renderPage();

    expect(await screen.findByText("Front Desk")).toBeInTheDocument();
    expect(screen.getByText("Default")).toBeInTheDocument();
    expect(screen.getByText(/192\.168\.1\.50:9100/)).toBeInTheDocument();
  });

  it("shows an error message when the request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(null, false, 500)));

    renderPage();

    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });

  it("creates a printer via the add form", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();

      if (url.endsWith("/api/printers") && (!init || init.method === undefined)) {
        return jsonResponse([]);
      }

      if (url.endsWith("/api/printers") && init?.method === "POST") {
        const body = JSON.parse(init.body as string);
        expect(body.name).toBe("New Printer");
        return jsonResponse({ ...printerA, id: 2, name: "New Printer" });
      }

      throw new Error(`Unexpected fetch: ${url} ${init?.method}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    renderPage();
    await screen.findByText(/no printers configured yet/i);

    fireEvent.click(screen.getByRole("button", { name: "Add Printer" }));
    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "New Printer" } });

    fireEvent.click(screen.getByRole("button", { name: "Add Printer" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/api/printers"),
      expect.objectContaining({ method: "POST" }),
    ));
  });

  it("offers the supported models in a dropdown, with a text field for other models", async () => {
    const models = [
      { name: "PT-P750W", family: "Pt180", network: true, dpi: 180, highResolution: true, twoColor: false, cutModes: ["AutoCut"] },
      { name: "QL-700", family: "Ql720", network: false, dpi: 300, highResolution: true, twoColor: false, cutModes: ["AutoCut"] },
    ];
    let created: Record<string, unknown> | null = null;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        if (url.endsWith("/api/printers/models")) return jsonResponse(models);
        if (url.endsWith("/api/printers") && init?.method === "POST") {
          created = JSON.parse(String(init.body));
          return jsonResponse({ ...printerA, id: 3 });
        }
        return jsonResponse([]);
      }),
    );

    renderPage();
    await screen.findByText(/no printers configured yet/i);
    fireEvent.click(screen.getByRole("button", { name: "Add Printer" }));

    const model = screen.getByLabelText("Model");
    await screen.findByRole("option", { name: "QL-700" });
    fireEvent.change(model, { target: { value: "QL-700" } });
    expect(screen.getByText(/QL-700 \(300 dpi\) connects by USB/)).toBeInTheDocument();

    fireEvent.change(model, { target: { value: "__other__" } });
    fireEvent.change(screen.getByLabelText("Model name"), { target: { value: "Brother PT-P750W" } });
    expect(screen.getByText(/PT-P750W \(180 dpi\) prints directly/)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Desk" } });
    fireEvent.click(screen.getByRole("button", { name: "Add Printer" }));
    await waitFor(() => expect(created).toMatchObject({ model: "Brother PT-P750W" }));
  });

  it("shows the print server or USB fields depending on the selected connection type", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([])));

    renderPage();
    await screen.findByText(/no printers configured yet/i);
    fireEvent.click(screen.getByRole("button", { name: "Add Printer" }));

    fireEvent.change(screen.getByLabelText("Connection type"), { target: { value: "PrintServer" } });
    expect(screen.getByLabelText("Print server address")).toBeInTheDocument();
    expect(screen.getByRole("note")).toHaveTextContent("AppSocket/HP JetDirect");
    expect(screen.getByLabelText("Port")).toHaveAccessibleDescription("Raw printing port, usually 9100.");
    expect(screen.getByRole("group", { name: "Test print size (optional)" })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Connection type"), { target: { value: "Usb" } });
    expect(screen.getByLabelText("USB identifier")).toBeInTheDocument();
    expect(screen.queryByLabelText("Port")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Print server address")).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Connection type"), { target: { value: "Hostname" } });
    expect(screen.getByLabelText("Hostname")).toBeInTheDocument();
    expect(screen.getByLabelText("Port")).toBeInTheDocument();
  });

  it("does not delete the printer when the confirmation is declined", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([printerA]));
    vi.stubGlobal("fetch", fetchMock);
    vi.stubGlobal("confirm", vi.fn().mockReturnValue(false));

    renderPage();
    await screen.findByText("Front Desk");

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));

    expect(screen.getByText("Front Desk")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({ method: "DELETE" }),
    );
  });

  it("sets a printer as the default", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();
      const nonDefault = { ...printerA, isDefault: false };

      if (url.endsWith("/api/printers") && (!init || init.method === undefined)) {
        return jsonResponse([nonDefault]);
      }

      if (url.endsWith("/api/printers/1/default") && init?.method === "PUT") {
        return { ok: true, status: 204, json: async () => null } as Response;
      }

      throw new Error(`Unexpected fetch: ${url} ${init?.method}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    renderPage();
    await screen.findByText("Front Desk");

    fireEvent.click(screen.getByRole("button", { name: "Set Default" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining("/api/printers/1/default"),
        expect.objectContaining({ method: "PUT" }),
      ),
    );
  });

  it("shows a failure result when a test print fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === "string" ? input : input.toString();

        if (url.endsWith("/api/printers") && (!init || init.method === undefined)) {
          return jsonResponse([printerA]);
        }

        if (url.endsWith("/api/printers/1/test-print")) {
          return jsonResponse({ isSuccess: false, errorMessage: "Connection refused." });
        }

        throw new Error(`Unexpected fetch: ${url}`);
      }),
    );

    renderPage();
    await screen.findByText("Front Desk");

    fireEvent.click(screen.getByRole("button", { name: "Test Print" }));

    expect(await screen.findByText("Connection refused.")).toBeInTheDocument();
  });

  it("tests a printer connection and shows the result", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();

      if (url.endsWith("/api/printers") && (!init || init.method === undefined)) {
        return jsonResponse([printerA]);
      }

      if (url.endsWith("/api/printers/1/test-connection")) {
        return jsonResponse({ isSuccess: true, errorMessage: null });
      }

      throw new Error(`Unexpected fetch: ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    renderPage();
    await screen.findByText("Front Desk");

    fireEvent.click(screen.getByRole("button", { name: "Test Connection" }));

    expect(await screen.findByText(/connection succeeded/i)).toBeInTheDocument();
  });

  it("deletes a printer after confirmation", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();

      if (url.endsWith("/api/printers") && (!init || init.method === undefined)) {
        return jsonResponse(fetchMock.mock.calls.length > 2 ? [] : [printerA]);
      }

      if (url.endsWith("/api/printers/1") && init?.method === "DELETE") {
        return { ok: true, status: 204, json: async () => null } as Response;
      }

      throw new Error(`Unexpected fetch: ${url} ${init?.method}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    renderPage();
    await screen.findByText("Front Desk");

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));

    await waitFor(() => expect(screen.getByText(/no printers configured yet/i)).toBeInTheDocument());
  });

  it("populates the edit form with the printer's current values", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([printerA])));

    renderPage();
    await screen.findByText("Front Desk");

    fireEvent.click(screen.getByRole("button", { name: "Edit" }));

    expect(await screen.findByDisplayValue("Front Desk")).toBeInTheDocument();
    expect(screen.getByDisplayValue("192.168.1.50")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save Changes" })).toBeInTheDocument();
  });
  it("marks a USB printer on another computer and offers no tests for it", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse([{ ...printerA, connectionType: "Usb", computerName: "OFFICE-PC", onThisComputer: false }]),
      ),
    );

    renderPage();

    expect(await screen.findByText("on OFFICE-PC")).toBeInTheDocument();
    expect(screen.getByText(/Connected to OFFICE-PC/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Test Print" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Test Connection" })).not.toBeInTheDocument();
  });
});
