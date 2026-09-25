import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { getSettings, updateSettings, type SettingsPatch } from "../api/settings";
import i18n, { SUPPORTED_LANGUAGES, type SupportedLanguage } from "../i18n";
import { applyTheme, getStoredTheme, setStoredTheme, type ThemePreference } from "../theme/theme";
import {
  formatDimension,
  formatLengthNumber,
  formatSize,
  formatTotalArea,
  formatTotalLength,
  fromDisplayValue,
  inputStep,
  isLengthUnit,
  toDisplayValue,
  unitSymbol,
  type LengthUnit,
} from "../units";

export interface AppSettings {
  language: SupportedLanguage;
  theme: ThemePreference;
  unit: LengthUnit;
}

interface AppSettingsContextValue {
  settings: AppSettings;
  /** Applies immediately, then saves to the server; reverts and rethrows if saving fails. */
  update: (patch: Partial<AppSettings>) => Promise<void>;
}

// The server (database) is the source of truth. These browser keys are only a cache, so the
// right theme and language show on the very first paint instead of flashing the defaults.
const UNIT_CACHE_KEY = "tapeory.unit";

function isLanguage(value: unknown): value is SupportedLanguage {
  return (SUPPORTED_LANGUAGES as readonly unknown[]).includes(value);
}

function isTheme(value: unknown): value is ThemePreference {
  return value === "light" || value === "dark" || value === "system";
}

function readCachedUnit(): LengthUnit {
  try {
    const stored = localStorage.getItem(UNIT_CACHE_KEY);
    return isLengthUnit(stored) ? stored : "mm";
  } catch {
    return "mm";
  }
}

function currentLanguage(): SupportedLanguage {
  const language = i18n.resolvedLanguage ?? i18n.language;
  return isLanguage(language) ? language : "en";
}

function readCache(): AppSettings {
  return { language: currentLanguage(), theme: getStoredTheme(), unit: readCachedUnit() };
}

/** Applies settings to this browser: theme attribute, UI language, and the local cache. */
function applyLocally(settings: AppSettings) {
  setStoredTheme(settings.theme);
  applyTheme(settings.theme);
  if (currentLanguage() !== settings.language) {
    void i18n.changeLanguage(settings.language);
  }
  try {
    localStorage.setItem(UNIT_CACHE_KEY, settings.unit);
  } catch {
    // Cache only; the setting itself lives on the server.
  }
}

const AppSettingsContext = createContext<AppSettingsContextValue>({
  // Outside a provider (isolated component tests) settings still apply locally, just unsaved.
  settings: { language: "en", theme: "system", unit: "mm" },
  update: async (patch) => applyLocally({ ...readCache(), ...patch }),
});

export function AppSettingsProvider({ children }: { children: ReactNode }) {
  const [settings, setSettings] = useState<AppSettings>(readCache);
  const settingsRef = useRef(settings);
  settingsRef.current = settings;

  useEffect(() => {
    let cancelled = false;

    getSettings()
      .then((server) => {
        if (cancelled) return;

        const local = settingsRef.current;
        const merged: AppSettings = {
          language: isLanguage(server.language) ? server.language : local.language,
          theme: isTheme(server.theme) ? server.theme : local.theme,
          unit: isLengthUnit(server.unit) ? server.unit : local.unit,
        };
        applyLocally(merged);
        setSettings(merged);

        // Nothing stored yet (fresh install, or upgrading from browser-only settings): save what
        // this browser uses, so every other device picks up the same configuration.
        const missing: SettingsPatch = {};
        if (server.language === null) missing.language = merged.language;
        if (server.theme === null) missing.theme = merged.theme;
        if (server.unit === null) missing.unit = merged.unit;
        if (Object.keys(missing).length > 0) {
          updateSettings(missing).catch(() => {
            // Retried naturally the next time settings are loaded or changed.
          });
        }
      })
      .catch(() => {
        // Offline or API down: keep the cached settings.
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const update = useCallback(async (patch: Partial<AppSettings>) => {
    const previous = settingsRef.current;
    const next = { ...previous, ...patch };
    applyLocally(next);
    setSettings(next);

    try {
      await updateSettings(patch);
    } catch (err) {
      applyLocally(previous);
      setSettings(previous);
      throw err;
    }
  }, []);

  const value = useMemo(() => ({ settings, update }), [settings, update]);
  return <AppSettingsContext.Provider value={value}>{children}</AppSettingsContext.Provider>;
}

export function useAppSettings(): AppSettingsContextValue {
  return useContext(AppSettingsContext);
}

/** Length helpers bound to the configured unit and the UI language. */
export function useUnits() {
  const { settings } = useAppSettings();
  const { i18n: instance } = useTranslation();
  const unit = settings.unit;
  const locale = instance.language;

  return useMemo(
    () => ({
      unit,
      symbol: unitSymbol(unit),
      step: inputStep(unit),
      toDisplay: (mm: number) => toDisplayValue(mm, unit),
      fromDisplay: (value: number) => fromDisplayValue(value, unit),
      number: (mm: number) => formatLengthNumber(mm, unit, locale),
      dimension: (mm: number) => formatDimension(mm, unit, locale),
      size: (widthMm: number, heightMm: number) => formatSize(widthMm, heightMm, unit, locale),
      totalLength: (mm: number) => formatTotalLength(mm, unit, locale),
      totalArea: (mm2: number) => formatTotalArea(mm2, unit, locale),
    }),
    [unit, locale],
  );
}
