import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { AboutPage } from "./AboutPage";

function renderPage() {
  return render(
    <MemoryRouter>
      <AboutPage />
    </MemoryRouter>,
  );
}

function jsonResponse(body: unknown) {
  return { ok: true, json: async () => body } as Response;
}

describe("AboutPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows the application version", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([])));

    renderPage();
    await screen.findByRole("heading", { name: "About" });

    expect(screen.getByText(__APP_VERSION__)).toBeInTheDocument();
  });
  it("shows the update check next to the version, with a link to a newer release", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);

      if (url.endsWith("/api/printers")) return jsonResponse([]);
      if (url.includes("/api/updates")) {
        const refreshed = url.includes("refresh=true");
        return jsonResponse({
          currentVersion: "0.2.1",
          latestVersion: refreshed ? "0.3.0" : "0.2.1",
          updateAvailable: refreshed,
          releaseUrl: "https://github.com/Tunefish92/Tapeory/releases/tag/v0.3.0",
          checkedAt: "2026-09-27T10:00:00Z",
          errorMessage: null,
        });
      }

      return jsonResponse({ status: "ok", storagePath: "/data", databaseConnected: true });
    });
    vi.stubGlobal("fetch", fetchMock);

    renderPage();

    const row = screen.getByText("Version").closest("dl") as HTMLElement;
    expect(await within(row).findByText("Up to date")).toBeInTheDocument();

    fireEvent.click(within(row).getByRole("button", { name: "Check for updates" }));

    expect(await within(row).findByText("Version 0.3.0 available")).toBeInTheDocument();
    expect(within(row).getByRole("link", { name: "What’s new" })).toHaveAttribute(
      "href",
      "https://github.com/Tunefish92/Tapeory/releases/tag/v0.3.0",
    );
  });

  it("says so when the update check fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        if (url.endsWith("/api/printers")) return jsonResponse([]);
        if (url.includes("/api/updates")) {
          return jsonResponse({
            currentVersion: "0.2.1",
            latestVersion: null,
            updateAvailable: false,
            releaseUrl: null,
            checkedAt: null,
            errorMessage: "GitHub couldn't be reached.",
          });
        }
        return jsonResponse({ status: "ok", storagePath: "/data", databaseConnected: true });
      }),
    );

    renderPage();

    expect(await screen.findByText("Couldn’t check for updates")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Check for updates" })).toBeInTheDocument();
  });

  it("shows where this installation keeps its files", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ status: "ok", storagePath: "/data", databaseConnected: true })));

    renderPage();

    expect(await screen.findByText("/data")).toBeInTheDocument();
    expect(screen.getByText("Connected")).toBeInTheDocument();
  });

  it("links to the project on GitHub, its issues, and the form for a new one", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([])));

    renderPage();

    const repository = "https://github.com/Tunefish92/Tapeory";
    expect(screen.getByRole("link", { name: "Source code on GitHub" })).toHaveAttribute("href", repository);
    expect(screen.getByRole("link", { name: "Known problems and requests" })).toHaveAttribute("href", `${repository}/issues`);
    expect(screen.getByRole("link", { name: "Report a problem" })).toHaveAttribute("href", `${repository}/issues/new`);
  });
});
