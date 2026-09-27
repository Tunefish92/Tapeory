import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import i18n from "../i18n";
import { SettingsPage } from "./SettingsPage";

function renderPage() {
  return render(
    <MemoryRouter>
      <SettingsPage />
    </MemoryRouter>,
  );
}

function jsonResponse(body: unknown) {
  return { ok: true, json: async () => body } as Response;
}

describe("SettingsPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    document.documentElement.removeAttribute("data-theme");
    localStorage.clear();
    void i18n.changeLanguage("en");
  });

  it("shows the configured default printer once loaded", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === "string" ? input : input.toString();

        if (url.endsWith("/api/printers")) {
          return jsonResponse([
            { id: 1, name: "Front Desk", isDefault: false },
            { id: 2, name: "Back Office", isDefault: true },
          ]);
        }

        return jsonResponse({ status: "ok", storagePath: "/data", databaseConnected: true });
      }),
    );

    renderPage();

    expect(await screen.findByText("Back Office")).toBeInTheDocument();
  });

  it("shows a message when no default printer is configured", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === "string" ? input : input.toString();

        if (url.endsWith("/api/printers")) {
          return jsonResponse([]);
        }

        return jsonResponse({ status: "ok", storagePath: "/data", databaseConnected: true });
      }),
    );

    renderPage();

    expect(await screen.findByText(/no default printer configured/i)).toBeInTheDocument();
  });

  it("shows the storage path and connection status once health resolves", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === "string" ? input : input.toString();

        if (url.endsWith("/api/printers")) {
          return jsonResponse([]);
        }

        return jsonResponse({ status: "ok", storagePath: "/data/tapeory", databaseConnected: true });
      }),
    );

    renderPage();

    expect(await screen.findByText("/data/tapeory")).toBeInTheDocument();
    expect(screen.getByText("Connected")).toBeInTheDocument();
  });

  it("switches the interface language", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([])));

    renderPage();
    await screen.findByRole("heading", { name: "Settings" });

    const languageSelects = screen.getAllByRole("combobox");
    fireEvent.change(languageSelects[0], { target: { value: "de" } });

    await waitFor(() => expect(screen.getByRole("heading", { name: "Einstellungen" })).toBeInTheDocument());
  });

  it("persists the selected theme and applies it to the document", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([])));

    renderPage();
    await screen.findByRole("heading", { name: "Settings" });

    fireEvent.change(screen.getByDisplayValue("Follow system"), { target: { value: "dark" } });

    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(localStorage.getItem("tapeory.theme")).toBe("dark");
  });

  it("shows the application version", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([])));

    renderPage();
    await screen.findByRole("heading", { name: "Settings" });

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
});
