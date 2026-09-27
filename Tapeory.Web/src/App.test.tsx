import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

describe("App routing", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    window.history.pushState({}, "", "/");
  });

  it("renders the dashboard at the root route", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ status: "ok", storagePath: "/data", databaseConnected: true }),
      }),
    );

    render(<App />);

    // The app shows once the server has said whether anyone needs to sign in.
    expect(await screen.findByText(/system status/i)).toBeInTheDocument();
  });

  it("renders the templates list at /templates", async () => {
    window.history.pushState({}, "", "/templates");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, json: async () => [] }));

    render(<App />);

    expect(await screen.findByRole("heading", { name: "Templates" })).toBeInTheDocument();
  });

  it("renders the settings page at /settings", async () => {
    window.history.pushState({}, "", "/settings");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === "string" ? input : input.toString();

        if (url.endsWith("/api/printers")) {
          return { ok: true, json: async () => [] } as Response;
        }

        return {
          ok: true,
          json: async () => ({ status: "ok", storagePath: "/data", databaseConnected: true }),
        } as Response;
      }),
    );

    render(<App />);

    expect(await screen.findByRole("heading", { name: "Settings" })).toBeInTheDocument();
  });

  it("renders the printers page at /printers", async () => {
    window.history.pushState({}, "", "/printers");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, json: async () => [] }));

    render(<App />);

    expect(await screen.findByText(/no printers configured yet/i)).toBeInTheDocument();
  });
});
