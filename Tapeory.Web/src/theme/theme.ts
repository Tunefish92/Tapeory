export type ThemePreference = "light" | "dark" | "system";

const STORAGE_KEY = "tapeory.theme";
const VALID_THEMES: ThemePreference[] = ["light", "dark", "system"];

export function getStoredTheme(): ThemePreference {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return VALID_THEMES.includes(stored as ThemePreference) ? (stored as ThemePreference) : "system";
  } catch {
    return "system";
  }
}

export function applyTheme(theme: ThemePreference): void {
  if (theme === "system") {
    document.documentElement.removeAttribute("data-theme");
  } else {
    document.documentElement.setAttribute("data-theme", theme);
  }
}

export function setStoredTheme(theme: ThemePreference): void {
  try {
    localStorage.setItem(STORAGE_KEY, theme);
  } catch {
    // Best-effort persistence only; the theme still applies for this session.
  }
  applyTheme(theme);
}

export function initializeTheme(): void {
  applyTheme(getStoredTheme());
}
