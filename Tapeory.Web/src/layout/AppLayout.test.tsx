import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { MemoryRouter } from "react-router-dom";
import type { ReactElement } from "react";
import { AppLayout } from "./AppLayout";

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
});
