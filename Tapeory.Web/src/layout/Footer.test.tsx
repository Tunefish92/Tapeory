import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import i18n from "../i18n";
import { Footer } from "./Footer";

describe("Footer", () => {
  afterEach(() => {
    void i18n.changeLanguage("en");
  });

  it("credits Tunefish as editor and publisher", () => {
    render(<Footer />);

    expect(screen.getByText("Editor & publisher: Tunefish")).toBeInTheDocument();
  });

  it("translates the credit but keeps the name", async () => {
    await i18n.changeLanguage("de");
    render(<Footer />);

    expect(screen.getByText("Herausgeber & Redaktion: Tunefish")).toBeInTheDocument();
  });
});
