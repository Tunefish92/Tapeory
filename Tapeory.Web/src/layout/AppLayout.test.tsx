import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import type { ReactElement } from "react";
import { AppLayout } from "./AppLayout";
import { AuthContext, type AuthContextValue } from "../auth/AuthContext";

const signedInUser: AuthContextValue = {
  hasUsers: true,
  user: { id: 2, userName: "grace", displayName: "Grace", role: "User", mustChangePassword: false },
  openAccess: false,
  canAdminister: false,
  signOut: async () => {},
  startCreatingAccount: () => {},
  refresh: async () => {},
};

function renderWithRouter(ui: ReactElement, initialPath = "/") {
  return render(<MemoryRouter initialEntries={[initialPath]}>{ui}</MemoryRouter>);
}

describe("AppLayout", () => {
  it("renders the app title, primary navigation, and children", () => {
    renderWithRouter(
      <AppLayout>
        <p>content</p>
      </AppLayout>,
    );

    expect(screen.getByRole("heading", { name: "Tapeory" })).toBeInTheDocument();
    expect(
      screen.getByRole("navigation", { name: /primary/i }),
    ).toBeInTheDocument();
    expect(screen.getByText("content")).toBeInTheDocument();
  });

  it("renders every primary navigation item", () => {
    renderWithRouter(<AppLayout />);

    for (const item of ["Dashboard", "Templates", "Print Jobs", "Printers", "Settings"]) {
      expect(screen.getByText(item)).toBeInTheDocument();
    }
  });

  it("marks the link matching the current route as active", () => {
    renderWithRouter(<AppLayout />, "/templates");

    expect(screen.getByRole("link", { name: "Templates" })).toHaveClass("active");
    expect(screen.getByRole("link", { name: "Dashboard" })).not.toHaveClass("active");
  });

  it("toggles the mobile nav open and closed via the menu button", () => {
    renderWithRouter(<AppLayout />);

    const nav = document.getElementById("primary-nav")!;
    const toggle = screen.getByTestId("mobile-nav-toggle");

    expect(nav).not.toHaveClass("is-open");
    expect(toggle).toHaveAttribute("aria-expanded", "false");

    fireEvent.click(toggle);
    expect(nav).toHaveClass("is-open");
    expect(toggle).toHaveAttribute("aria-expanded", "true");

    fireEvent.click(toggle);
    expect(nav).not.toHaveClass("is-open");
    expect(toggle).toHaveAttribute("aria-expanded", "false");
  });
  it("hides Printers from users, and shows their account menu", () => {
    const signOut = vi.fn(async () => {});
    render(
      <AuthContext.Provider value={{ ...signedInUser, signOut }}>
        <MemoryRouter>
          <AppLayout />
        </MemoryRouter>
      </AuthContext.Provider>,
    );

    expect(screen.queryByText("Printers")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Grace" }));
    expect(screen.getByText("grace · User")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("menuitem", { name: "Change password" }));
    expect(screen.getByRole("dialog", { name: "Change password" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Grace" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Sign out" }));
    expect(signOut).toHaveBeenCalled();
  });

  it("asks for an admin account while Tapeory is open to everyone", () => {
    const startCreatingAccount = vi.fn();
    render(
      <AuthContext.Provider
        value={{ ...signedInUser, hasUsers: false, user: null, openAccess: true, canAdminister: true, startCreatingAccount }}
      >
        <MemoryRouter>
          <AppLayout />
        </MemoryRouter>
      </AuthContext.Provider>,
    );

    expect(screen.getByText(/Anyone on your network can use Tapeory/)).toBeInTheDocument();
    expect(screen.getByText("Printers")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Create account" }));
    expect(startCreatingAccount).toHaveBeenCalled();
  });
});
