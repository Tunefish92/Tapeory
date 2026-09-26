import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SetupPage } from "./SetupPage";

function jsonResponse(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as Response;
}

function renderPage(onComplete = vi.fn()) {
  render(<SetupPage onComplete={onComplete} />);
  fireEvent.change(screen.getByLabelText("Server"), { target: { value: "db.local" } });
  return onComplete;
}

describe("SetupPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("reports a successful connection test", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ isSuccess: true, errorMessage: null, databaseExists: true })));
    renderPage();

    fireEvent.click(screen.getByRole("button", { name: "Test connection" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Connection successful.");
  });

  it("says when the database will be created", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ isSuccess: true, errorMessage: null, databaseExists: false })));
    renderPage();

    fireEvent.click(screen.getByRole("button", { name: "Test connection" }));

    expect(await screen.findByRole("status")).toHaveTextContent("“tapeory” doesn’t exist yet");
  });

  it("shows the server's reason when the test fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse({ isSuccess: false, errorMessage: "Access denied for user 'tapeory'", databaseExists: false })),
    );
    renderPage();

    fireEvent.click(screen.getByRole("button", { name: "Test connection" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Access denied for user 'tapeory'");
  });

  it("stays on the form and shows the error when saving fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse({ title: "Could not set up the database.", detail: "Unable to connect to any of the specified MySQL hosts." }, 400),
      ),
    );
    const onComplete = renderPage();

    fireEvent.click(screen.getByRole("button", { name: "Save and continue" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Unable to connect to any of the specified MySQL hosts.");
    expect(onComplete).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Save and continue" })).toBeEnabled();
  });

  it("shows validation messages from the server", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse({ title: "One or more validation errors occurred.", errors: { database: ["Enter the database name."] } }, 400),
      ),
    );
    renderPage();

    fireEvent.click(screen.getByRole("button", { name: "Save and continue" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Enter the database name.");
  });
});
