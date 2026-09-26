import { afterEach, describe, expect, it, vi } from "vitest";
import { createPrintJob, getPrintJob, listPrintJobs, previewTemplate } from "./printJobs";

function jsonResponse(body: unknown, ok = true, status = ok ? 200 : 400) {
  return { ok, status, json: async () => body } as Response;
}

describe("printJobs api client", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("createPrintJob posts the request body and returns the parsed job", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 1, status: "Queued" }));
    vi.stubGlobal("fetch", fetchMock);

    const result = await createPrintJob({
      templateId: 5,
      printerId: null,
      printerName: "Front Desk",
      items: [{ fieldValues: { name: "Alice" }, quantity: 2 }],
    });

    expect(result).toEqual({ id: 1, status: "Queued" });
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/print-jobs");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toEqual({
      templateId: 5,
      printerId: null,
      printerName: "Front Desk",
      items: [{ fieldValues: { name: "Alice" }, quantity: 2 }],
    });
  });

  it("createPrintJob surfaces field-level validation errors from a 400 response", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse({ errors: { items: ["Item 1: A value is required for field 'name'."] } }, false, 400),
      ),
    );

    await expect(
      createPrintJob({ templateId: 1, printerId: null, printerName: null, items: [{ fieldValues: {}, quantity: 1 }] }),
    ).rejects.toThrow("A value is required for field 'name'.");
  });

  it("getPrintJob fetches by id", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 7 }));
    vi.stubGlobal("fetch", fetchMock);

    await getPrintJob(7);

    expect(fetchMock).toHaveBeenCalledWith("/api/print-jobs/7");
  });

  it("listPrintJobs omits the query string when no templateId is given", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]));
    vi.stubGlobal("fetch", fetchMock);

    await listPrintJobs();

    expect(fetchMock).toHaveBeenCalledWith("/api/print-jobs");
  });

  it("listPrintJobs includes the templateId filter when given", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]));
    vi.stubGlobal("fetch", fetchMock);

    await listPrintJobs(42);

    expect(fetchMock).toHaveBeenCalledWith("/api/print-jobs?templateId=42");
  });

  it("previewTemplate returns a blob on success", async () => {
    const blob = new Blob(["fake-png-bytes"], { type: "image/png" });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, blob: async () => blob } as Response));

    const result = await previewTemplate(1, { name: "Alice" });

    expect(result).toBe(blob);
  });

  it("previewTemplate throws the problem detail on failure", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ detail: "Template not found." }, false, 404)));

    await expect(previewTemplate(999, {})).rejects.toThrow("Template not found.");
  });
});
