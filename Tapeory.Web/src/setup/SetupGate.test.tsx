import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SetupGate } from "./SetupGate";

function jsonResponse(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as Response;
}

type Handler = (url: string, init?: RequestInit) => Response;

function stubFetch(handler: Handler) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => handler(input.toString(), init));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function fillIn(label: string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

describe("SetupGate", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows the app when the database is already set up", async () => {
    const fetchMock = stubFetch(() => jsonResponse({ configured: true }));

    render(<SetupGate>app content</SetupGate>);

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    expect(screen.getByText("app content")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Welcome to Tapeory" })).not.toBeInTheDocument();
  });

  it("shows the app when the setup status can't be checked", async () => {
    const fetchMock = stubFetch(() => {
      throw new TypeError("Failed to fetch");
    });

    render(<SetupGate>app content</SetupGate>);

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    expect(screen.getByText("app content")).toBeInTheDocument();
  });

  it("asks for the database on first start, then shows the app once it's saved", async () => {
    const fetchMock = stubFetch((url) => {
      if (url.endsWith("/setup/status")) return jsonResponse({ configured: false });
      if (url.endsWith("/setup/database")) return jsonResponse({ configured: true });
      throw new Error(`unexpected ${url}`);
    });

    render(<SetupGate>app content</SetupGate>);

    expect(await screen.findByRole("heading", { name: "Welcome to Tapeory" })).toBeInTheDocument();
    expect(screen.queryByText("app content")).not.toBeInTheDocument();

    fillIn("Server", " db.local ");
    fillIn("Password", "secret");
    fireEvent.click(screen.getByRole("button", { name: "Save and continue" }));

    expect(await screen.findByText("app content")).toBeInTheDocument();

    const [, init] = fetchMock.mock.calls.find(([url]) => url.toString().endsWith("/setup/database"))!;
    expect(init?.method).toBe("POST");
    expect(JSON.parse(init?.body as string)).toEqual({
      host: "db.local",
      port: 3306,
      database: "tapeory",
      user: "tapeory",
      password: "secret",
    });
  });
});
