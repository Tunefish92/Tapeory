import { fireEvent, render, screen, waitFor } from "@testing-library/react";
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
});
