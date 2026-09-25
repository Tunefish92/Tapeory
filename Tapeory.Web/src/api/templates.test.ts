import { afterEach, describe, expect, it, vi } from "vitest";
import {
  createTemplate,
  createTemplateVersion,
  getTemplate,
  importLbxTemplate,
  listTemplates,
  setTemplatePreviewImage,
  uploadImage,
} from "./templates";

function jsonResponse(body: unknown, ok = true, status = ok ? 200 : 400) {
  return {
    ok,
    status,
    json: async () => body,
  } as Response;
}

describe("templates api client", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("listTemplates builds a query string only for provided filters", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]));
    vi.stubGlobal("fetch", fetchMock);

    await listTemplates({ search: "box", category: "Shipping" });

    const [url] = fetchMock.mock.calls[0] as [string];
    expect(url).toContain("/api/templates?");
    expect(url).toContain("search=box");
    expect(url).toContain("category=Shipping");
    expect(url).not.toContain("status=");
  });

  it("listTemplates omits the query string entirely when no filters are given", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]));
    vi.stubGlobal("fetch", fetchMock);

    await listTemplates();

    expect(fetchMock).toHaveBeenCalledWith("/api/templates");
  });

  it("getTemplate returns the parsed detail on success", async () => {
    const detail = { id: 1, name: "Box Label" };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(detail)));

    const result = await getTemplate(1);

    expect(result).toEqual(detail);
  });

  it("throws the problem-details message when a request fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse({ detail: "Name is required." }, false, 400)),
    );

    await expect(createTemplate({ name: "", widthMm: 0, heightMm: 0, editorJson: "{}" })).rejects.toThrow(
      "Name is required.",
    );
  });

  it("falls back to a generic message when the failure body isn't parseable JSON", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: false,
        status: 500,
        json: async () => {
          throw new Error("not json");
        },
      } as unknown as Response),
    );

    await expect(getTemplate(1)).rejects.toThrow("Request failed with status 500");
  });

  it("createTemplateVersion posts to the versions endpoint", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ versionNumber: 2 }));
    vi.stubGlobal("fetch", fetchMock);

    await createTemplateVersion(7, { widthMm: 50, heightMm: 25, editorJson: "{}" });

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/templates/7/versions");
    expect(init.method).toBe("POST");
  });

  it("setTemplatePreviewImage resolves without a body on success", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, status: 204 } as Response));

    await expect(setTemplatePreviewImage(1, 2)).resolves.toBeUndefined();
  });

  it("setTemplatePreviewImage throws the problem detail on failure", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse({ detail: "Uploaded file not found." }, false, 400)),
    );

    await expect(setTemplatePreviewImage(1, 999)).rejects.toThrow("Uploaded file not found.");
  });

  it("uploadImage sends the file as multipart form data", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 1, url: "/api/uploads/images/1" }));
    vi.stubGlobal("fetch", fetchMock);

    const file = new File(["bytes"], "logo.png", { type: "image/png" });
    await uploadImage(file);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/uploads/images");
    expect(init.method).toBe("POST");
    expect(init.body).toBeInstanceOf(FormData);
  });

  it("importLbxTemplate sends the file as multipart form data to the import-lbx endpoint", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 1, name: "Imported" }));
    vi.stubGlobal("fetch", fetchMock);

    const file = new File(["zip bytes"], "label.lbx", { type: "application/octet-stream" });
    await importLbxTemplate(file);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/templates/import-lbx");
    expect(init.method).toBe("POST");
    expect(init.body).toBeInstanceOf(FormData);
  });

  it("importLbxTemplate throws the problem detail on failure", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse({ detail: "Only .lbx files are supported." }, false, 400)),
    );

    const file = new File(["not a label"], "notes.txt");
    await expect(importLbxTemplate(file)).rejects.toThrow("Only .lbx files are supported.");
  });
});
