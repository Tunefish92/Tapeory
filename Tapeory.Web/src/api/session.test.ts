import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { installApiFetch, SESSION_ENDED_EVENT } from "./session";

describe("installApiFetch", () => {
  const original = window.fetch;
  let inner: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    inner = vi.fn(async () => ({ status: 200 }) as Response);
    window.fetch = inner as unknown as typeof fetch;
    installApiFetch();
  });

  afterEach(() => {
    window.fetch = original;
  });

  function headersOf(call: number): Headers {
    return new Headers((inner.mock.calls[call][1] as RequestInit | undefined)?.headers);
  }

  it("marks API changes as coming from the web UI, keeping their own headers", async () => {
    await window.fetch("/api/templates", { method: "POST", headers: { "Content-Type": "application/json" } });

    expect(headersOf(0).get("X-Requested-With")).toBe("Tapeory");
    expect(headersOf(0).get("Content-Type")).toBe("application/json");
  });

  it("leaves reads and other sites alone", async () => {
    await window.fetch("/api/templates");
    await window.fetch("https://example.com/api/x", { method: "POST" });

    expect(headersOf(0).has("X-Requested-With")).toBe(false);
    expect(headersOf(1).has("X-Requested-With")).toBe(false);
  });

  it("reports an ended session when the API answers 401", async () => {
    const listener = vi.fn();
    window.addEventListener(SESSION_ENDED_EVENT, listener);
    inner.mockResolvedValueOnce({ status: 401 } as Response);

    await window.fetch("/api/templates");

    expect(listener).toHaveBeenCalledOnce();
    window.removeEventListener(SESSION_ENDED_EVENT, listener);
  });
});
