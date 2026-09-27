import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SESSION_ENDED_EVENT } from "../api/session";
import { AuthGate } from "./AuthGate";
import { useAuth } from "./AuthContext";

function jsonResponse(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as Response;
}

const admin = { id: 1, userName: "ada", displayName: "Ada", role: "Admin", mustChangePassword: false };

function stubFetch(handler: (url: string, init?: RequestInit) => Response) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => handler(String(input), init));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function WhoAmI() {
  const { user, canAdminister, startCreatingAccount } = useAuth();
  return (
    <div>
      <p>app for {user?.displayName ?? "everyone"}</p>
      <p>{canAdminister ? "can administer" : "can't administer"}</p>
      <button type="button" onClick={startCreatingAccount}>
        start
      </button>
    </div>
  );
}

function fillIn(label: string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

describe("AuthGate", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows the app to everyone while there are no accounts", async () => {
    stubFetch(() => jsonResponse({ hasUsers: false, user: null }));

    render(
      <AuthGate>
        <WhoAmI />
      </AuthGate>,
    );

    expect(await screen.findByText("app for everyone")).toBeInTheDocument();
    expect(screen.getByText("can administer")).toBeInTheDocument();
  });

  it("asks to sign in once accounts exist, then shows the app", async () => {
    const fetchMock = stubFetch((url) => {
      if (url.endsWith("/auth/state")) return jsonResponse({ hasUsers: true, user: null });
      if (url.endsWith("/auth/login")) return jsonResponse({ hasUsers: true, user: { ...admin, role: "User" } });
      throw new Error(`unexpected ${url}`);
    });

    render(
      <AuthGate>
        <WhoAmI />
      </AuthGate>,
    );

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
    fillIn("User name", " ada ");
    fillIn("Password", "secret password");
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("app for Ada")).toBeInTheDocument();
    expect(screen.getByText("can't administer")).toBeInTheDocument();
    const loginCall = fetchMock.mock.calls.find(([url]) => String(url).endsWith("/auth/login"));
    expect(JSON.parse(String(loginCall?.[1]?.body))).toEqual({ userName: "ada", password: "secret password", rememberMe: true });
  });

  it("shows the server's message when signing in fails", async () => {
    stubFetch((url) => {
      if (url.endsWith("/auth/state")) return jsonResponse({ hasUsers: true, user: null });
      return jsonResponse({ title: "Wrong user name or password." }, 400);
    });

    render(
      <AuthGate>
        <WhoAmI />
      </AuthGate>,
    );

    await screen.findByRole("heading", { name: "Sign in" });
    fillIn("User name", "ada");
    fillIn("Password", "nope nope");
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Wrong user name or password.");
  });

  it("creates the first account from the banner, checking the password first", async () => {
    const fetchMock = stubFetch((url) => {
      if (url.endsWith("/auth/state")) return jsonResponse({ hasUsers: false, user: null });
      if (url.endsWith("/auth/first-user")) return jsonResponse({ hasUsers: true, user: admin });
      throw new Error(`unexpected ${url}`);
    });

    render(
      <AuthGate>
        <WhoAmI />
      </AuthGate>,
    );

    fireEvent.click(await screen.findByRole("button", { name: "start" }));
    expect(screen.getByRole("heading", { name: "Create your account" })).toBeInTheDocument();

    fillIn("User name", "ada");
    fillIn("Display name (optional)", "Ada");
    fillIn("Password", "long enough");
    fillIn("Repeat password", "long enougH");
    fireEvent.click(screen.getByRole("button", { name: "Create account" }));
    expect(screen.getByRole("alert")).toHaveTextContent("The passwords don't match.");
    expect(fetchMock).not.toHaveBeenCalledWith(expect.stringContaining("first-user"), expect.anything());

    fillIn("Repeat password", "long enough");
    fireEvent.click(screen.getByRole("button", { name: "Create account" }));

    expect(await screen.findByText("app for Ada")).toBeInTheDocument();
  });

  it("can back out of creating an account on an existing install", async () => {
    stubFetch(() => jsonResponse({ hasUsers: false, user: null }));

    render(
      <AuthGate>
        <WhoAmI />
      </AuthGate>,
    );

    fireEvent.click(await screen.findByRole("button", { name: "start" }));
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(screen.getByText("app for everyone")).toBeInTheDocument();
  });

  it("makes an account on a temporary password choose its own first", async () => {
    stubFetch((url) => {
      if (url.endsWith("/auth/state")) return jsonResponse({ hasUsers: true, user: { ...admin, mustChangePassword: true } });
      if (url.endsWith("/auth/password")) return jsonResponse({ hasUsers: true, user: admin });
      throw new Error(`unexpected ${url}`);
    });

    render(
      <AuthGate>
        <WhoAmI />
      </AuthGate>,
    );

    expect(await screen.findByRole("heading", { name: "Choose a new password" })).toBeInTheDocument();
    fillIn("Current password", "Temp2345abcd");
    fillIn("New password", "my own password");
    fillIn("Repeat password", "my own password");
    fireEvent.click(screen.getByRole("button", { name: "Change password" }));

    expect(await screen.findByText("app for Ada")).toBeInTheDocument();
  });

  it("goes back to signing in when a call reports the session ended", async () => {
    let signedIn = true;
    stubFetch(() => jsonResponse({ hasUsers: true, user: signedIn ? admin : null }));

    render(
      <AuthGate>
        <WhoAmI />
      </AuthGate>,
    );

    await screen.findByText("app for Ada");
    signedIn = false;
    window.dispatchEvent(new Event(SESSION_ENDED_EVENT));

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
  });

  it("shows the app when the server can't say (an older version, or unreachable)", async () => {
    stubFetch(() => jsonResponse(null, 503));

    render(
      <AuthGate>
        <WhoAmI />
      </AuthGate>,
    );

    await waitFor(() => expect(screen.getByText("app for everyone")).toBeInTheDocument());
  });
});
