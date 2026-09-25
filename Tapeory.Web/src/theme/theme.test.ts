import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { applyTheme, getStoredTheme, initializeTheme, setStoredTheme } from "./theme";

describe("theme", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute("data-theme");
  });

  afterEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute("data-theme");
  });

  describe("getStoredTheme", () => {
    it("defaults to 'system' when nothing is stored", () => {
      expect(getStoredTheme()).toBe("system");
    });

    it("returns a validly stored preference", () => {
      localStorage.setItem("tapeory.theme", "dark");
      expect(getStoredTheme()).toBe("dark");
    });

    it("falls back to 'system' for a garbage stored value", () => {
      localStorage.setItem("tapeory.theme", "not-a-theme");
      expect(getStoredTheme()).toBe("system");
    });
  });

  describe("applyTheme", () => {
    it("sets data-theme for 'light' and 'dark'", () => {
      applyTheme("light");
      expect(document.documentElement.getAttribute("data-theme")).toBe("light");

      applyTheme("dark");
      expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    });

    it("removes data-theme for 'system'", () => {
      document.documentElement.setAttribute("data-theme", "dark");

      applyTheme("system");

      expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
    });
  });

  describe("setStoredTheme", () => {
    it("persists the preference and applies it", () => {
      setStoredTheme("dark");

      expect(localStorage.getItem("tapeory.theme")).toBe("dark");
      expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    });

    it("clears data-theme and persists 'system'", () => {
      setStoredTheme("dark");
      setStoredTheme("system");

      expect(localStorage.getItem("tapeory.theme")).toBe("system");
      expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
    });
  });

  describe("initializeTheme", () => {
    it("applies whatever preference is already stored", () => {
      localStorage.setItem("tapeory.theme", "light");

      initializeTheme();

      expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    });

    it("applies 'system' (no attribute) when nothing is stored", () => {
      initializeTheme();

      expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
    });
  });
});
