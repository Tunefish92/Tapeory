import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AuthContext, type AuthContextValue } from "../auth/AuthContext";
import { UsersCard } from "./UsersCard";

function jsonResponse(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as Response;
}

const me = { id: 1, userName: "ada", displayName: "Ada", role: "Admin" as const, mustChangePassword: false };

const users = [
  { ...me, disabled: false, createdAt: "2026-09-01T00:00:00Z", lastLoginAt: "2026-09-27T10:00:00Z" },
  {
    id: 2,
    userName: "grace",
    displayName: "Grace",
    role: "User",
    disabled: false,
    mustChangePassword: true,
    createdAt: "2026-09-02T00:00:00Z",
    lastLoginAt: null,
  },
];

const auth: AuthContextValue = {
  hasUsers: true,
  user: me,
  openAccess: false,
  canAdminister: true,
  signOut: async () => {},
  startCreatingAccount: () => {},
  refresh: async () => {},
};

function renderCard() {
  return render(
    <AuthContext.Provider value={auth}>
      <UsersCard icon={null} />
    </AuthContext.Provider>,
  );
}

describe("UsersCard", () => {
  beforeEach(() => {
    vi.stubGlobal("confirm", vi.fn().mockReturnValue(true));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("lists the accounts, without reset or delete for your own", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => jsonResponse(users)));

    renderCard();

    const ada = (await screen.findByText("Ada")).closest("li") as HTMLElement;
    const grace = screen.getByText("Grace").closest("li") as HTMLElement;
    expect(within(ada).getByText("(you)")).toBeInTheDocument();
    expect(within(ada).queryByRole("button", { name: "Delete" })).not.toBeInTheDocument();
    expect(within(grace).getByText("Temporary password")).toBeInTheDocument();
    expect(within(grace).getByText(/never signed in/)).toBeInTheDocument();
    expect(within(grace).getByRole("button", { name: "Reset password" })).toBeInTheDocument();
  });

  it("adds an account and shows its temporary password once", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === "POST") {
        return jsonResponse({ user: { ...users[1], id: 3, userName: "linus" }, temporaryPassword: "Temp2345abcd" });
      }
      return jsonResponse(users);
    });
    vi.stubGlobal("fetch", fetchMock);

    renderCard();
    await screen.findByText("Grace");

    const form = screen.getByRole("form", { name: "Add user" });
    fireEvent.change(within(form).getByLabelText("User name"), { target: { value: " linus " } });
    fireEvent.change(within(form).getByLabelText("Role"), { target: { value: "Admin" } });
    fireEvent.click(within(form).getByRole("button", { name: "Add user" }));

    expect(await screen.findByText("Temp2345abcd")).toBeInTheDocument();
    expect(screen.getByText("Temporary password for linus:")).toBeInTheDocument();
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === "POST");
    expect(JSON.parse(String(post?.[1]?.body))).toEqual({ userName: "linus", displayName: "", role: "Admin" });

    fireEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(screen.queryByText("Temp2345abcd")).not.toBeInTheDocument();
  });

  it("changes an account's role and disables it", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) =>
      init?.method === "PUT" ? jsonResponse(users[1]) : jsonResponse(users),
    );
    vi.stubGlobal("fetch", fetchMock);

    renderCard();
    const grace = (await screen.findByText("Grace")).closest("li") as HTMLElement;
    fireEvent.click(within(grace).getByRole("button", { name: "Edit" }));

    const editRow = screen.getByRole("button", { name: "Save" }).closest("li") as HTMLElement;
    fireEvent.change(within(editRow).getByLabelText("Role"), { target: { value: "Admin" } });
    fireEvent.click(within(editRow).getByLabelText("Disabled"));
    fireEvent.click(within(editRow).getByRole("button", { name: "Save" }));

    await waitFor(() => {
      const put = fetchMock.mock.calls.find(([, init]) => init?.method === "PUT");
      expect(String(put?.[0])).toContain("/users/2");
      expect(JSON.parse(String(put?.[1]?.body))).toEqual({ displayName: "Grace", role: "Admin", disabled: true });
    });
  });

  it("asks what happens to the account's templates, and shows the server's reason when refused", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) =>
      init?.method === "DELETE"
        ? jsonResponse({ title: "Tapeory needs at least one active administrator." }, 400)
        : jsonResponse(users),
    );
    vi.stubGlobal("fetch", fetchMock);

    renderCard();
    const grace = (await screen.findByText("Grace")).closest("li") as HTMLElement;
    fireEvent.click(within(grace).getByRole("button", { name: "Delete" }));

    const panel = within(grace).getByRole("group", { name: "Delete Grace" });
    expect(within(panel).getByLabelText("Transfer them to me (they stay private)")).toBeChecked();
    fireEvent.click(within(panel).getByLabelText("Delete them too"));
    fireEvent.click(within(panel).getByRole("button", { name: "Delete account" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Tapeory needs at least one active administrator.");
    const call = fetchMock.mock.calls.find(([, init]) => init?.method === "DELETE");
    expect(String(call?.[0])).toContain("/users/2?templates=delete");
  });
});
