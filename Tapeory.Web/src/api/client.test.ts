import { afterEach, describe, expect, it, vi } from "vitest";
import { fetchHealth } from "./client";

describe("fetchHealth", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("returns the parsed health status on success", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({
          status: "ok",
          storagePath: "/data",
          databaseConnected: true,
        }),
      }),
    );

    const result = await fetchHealth();

    expect(result).toEqual({
      status: "ok",
      storagePath: "/data",
      databaseConnected: true,
    });
  });

  it("throws a descriptive error when the response is not ok", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({ ok: false, status: 503 }),
    );

    await expect(fetchHealth()).rejects.toThrow("503");
  });
});
