import { afterEach, describe, expect, it, vi } from "vitest";
import {
  createPrinter,
  deletePrinter,
  getPrinter,
  listPrinters,
  setDefaultPrinter,
  testPrinterConnection,
  testPrinterPrint,
  updatePrinter,
} from "./printers";

function jsonResponse(body: unknown, ok = true, status = ok ? 200 : 400) {
  return { ok, status, json: async () => body } as Response;
}

const request = {
  name: "Front Desk",
  connectionType: "IpAddress" as const,
  address: "192.168.1.50",
  port: 9100,
  enabled: true,
};

describe("printers api client", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("listPrinters fetches the collection", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]));
    vi.stubGlobal("fetch", fetchMock);

    await listPrinters();

    expect(fetchMock).toHaveBeenCalledWith("/api/printers");
  });

  it("getPrinter fetches by id", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 3 }));
    vi.stubGlobal("fetch", fetchMock);

    await getPrinter(3);

    expect(fetchMock).toHaveBeenCalledWith("/api/printers/3");
  });

  it("createPrinter posts the request body", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 1, ...request }));
    vi.stubGlobal("fetch", fetchMock);

    await createPrinter(request);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/printers");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toEqual(request);
  });

  it("createPrinter surfaces the problem detail on a validation failure", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ detail: "Name is required." }, false, 400)));

    await expect(createPrinter(request)).rejects.toThrow("Name is required.");
  });

  it("updatePrinter puts to the printer's id", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 1, ...request }));
    vi.stubGlobal("fetch", fetchMock);

    await updatePrinter(1, request);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/printers/1");
    expect(init.method).toBe("PUT");
  });

  it("deletePrinter issues a DELETE and resolves on success", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204, json: async () => null } as Response);
    vi.stubGlobal("fetch", fetchMock);

    await expect(deletePrinter(1)).resolves.toBeUndefined();
    expect(fetchMock).toHaveBeenCalledWith("/api/printers/1", { method: "DELETE" });
  });

  it("deletePrinter throws on failure", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ detail: "Not found." }, false, 404)));

    await expect(deletePrinter(999)).rejects.toThrow("Not found.");
  });

  it("setDefaultPrinter puts to the default endpoint", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204, json: async () => null } as Response);
    vi.stubGlobal("fetch", fetchMock);

    await setDefaultPrinter(1);

    expect(fetchMock).toHaveBeenCalledWith("/api/printers/1/default", { method: "PUT" });
  });

  it("testPrinterConnection posts and returns the result", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ isSuccess: true, errorMessage: null }));
    vi.stubGlobal("fetch", fetchMock);

    const result = await testPrinterConnection(1);

    expect(fetchMock).toHaveBeenCalledWith("/api/printers/1/test-connection", { method: "POST" });
    expect(result).toEqual({ isSuccess: true, errorMessage: null });
  });

  it("testPrinterPrint posts and returns the result", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ isSuccess: false, errorMessage: "Timed out." }));
    vi.stubGlobal("fetch", fetchMock);

    const result = await testPrinterPrint(1);

    expect(fetchMock).toHaveBeenCalledWith("/api/printers/1/test-print", { method: "POST" });
    expect(result).toEqual({ isSuccess: false, errorMessage: "Timed out." });
  });
});
