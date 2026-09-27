import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import i18n from "../i18n";
import { Footer } from "./Footer";

describe("Footer", () => {
  afterEach(() => {
    void i18n.changeLanguage("en");
  });

  it("shows only the copyright, linking Tunefish to its GitHub repositories", () => {
    render(<Footer />);

    const year = new Date().getFullYear();
    expect(screen.getByRole("contentinfo")).toHaveTextContent(`© ${year} by Tunefish`);
    expect(screen.getByRole("link", { name: "Tunefish" })).toHaveAttribute(
      "href",
      "https://github.com/Tunefish92?tab=repositories",
    );
  });

  it("translates the copyright but keeps the name", async () => {
    await i18n.changeLanguage("de");
    render(<Footer />);

    expect(screen.getByRole("contentinfo")).toHaveTextContent(`© ${new Date().getFullYear()} von Tunefish`);
  });
});
