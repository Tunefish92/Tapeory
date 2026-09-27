import { useEffect, useState, type PropsWithChildren } from "react";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { checkForUpdates, fetchHealth, type HealthStatus, type UpdateCheck } from "../api/client";
import { listPrinters, type PrinterResponse } from "../api/printers";
import { getStoredTheme, setStoredTheme, type ThemePreference } from "../theme/theme";
import { SUPPORTED_LANGUAGES, type SupportedLanguage } from "../i18n";
import { StatisticsCard } from "./StatisticsCard";
import { BackupCard } from "./BackupCard";
import { UsersCard } from "./UsersCard";
import { useAuth } from "../auth/AuthContext";
import "./settings.css";

// Each language in its own name, so people can find theirs whatever the UI is set to.
const LANGUAGE_NAMES: Record<SupportedLanguage, string> = {
  en: "English",
  de: "Deutsch",
  it: "Italiano",
  es: "Español",
  fr: "Français",
};

// Alphabetically by the language's own name.
const LANGUAGE_OPTIONS = [...SUPPORTED_LANGUAGES].sort((a, b) =>
  LANGUAGE_NAMES[a].localeCompare(LANGUAGE_NAMES[b], "en"),
);

export function SettingsPage() {
  const { t, i18n } = useTranslation();
  const { user, canAdminister } = useAuth();

  const [theme, setTheme] = useState<ThemePreference>(() => getStoredTheme());
  const [defaultPrinter, setDefaultPrinter] = useState<PrinterResponse | null | undefined>(undefined);
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [update, setUpdate] = useState<UpdateCheck | "checking" | "failed">("checking");

  function runUpdateCheck(refresh: boolean) {
    setUpdate("checking");
    checkForUpdates(refresh)
      .then(setUpdate)
      .catch(() => setUpdate("failed"));
  }

  useEffect(() => {
    listPrinters()
      .then((printers) => setDefaultPrinter(printers.find((p) => p.isDefault) ?? null))
      .catch(() => setDefaultPrinter(null));

    fetchHealth()
      .then(setHealth)
      .catch(() => setHealth(null));

    runUpdateCheck(false);
  }, []);

  function handleThemeChange(value: ThemePreference) {
    setTheme(value);
    setStoredTheme(value);
  }

  return (
    <section className="settings-page page-enter">
      <div className="page-header">
        <h2>{t("settings.title")}</h2>
      </div>

      <div className="settings-grid">
      <div className="card settings-section">
        <div className="settings-section__head">
          <SectionIcon>
            <circle cx="12" cy="12" r="9" />
            <path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18" />
          </SectionIcon>
          <h3>{t("settings.language")}</h3>
        </div>
        <select
          aria-label={t("settings.language")}
          value={i18n.language}
          onChange={(e) => void i18n.changeLanguage(e.target.value)}
        >
          {LANGUAGE_OPTIONS.map((lng) => (
            <option key={lng} value={lng} lang={lng}>
              {LANGUAGE_NAMES[lng]}
            </option>
          ))}
        </select>
      </div>

      <div className="card settings-section">
        <div className="settings-section__head">
          <SectionIcon>
            <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" />
          </SectionIcon>
          <h3>{t("settings.theme")}</h3>
        </div>
        <select
          aria-label={t("settings.theme")}
          value={theme}
          onChange={(e) => handleThemeChange(e.target.value as ThemePreference)}
        >
          <option value="light">{t("settings.themeLight")}</option>
          <option value="dark">{t("settings.themeDark")}</option>
          <option value="system">{t("settings.themeSystem")}</option>
        </select>
      </div>

      <div className="card settings-section">
        <div className="settings-section__head">
          <SectionIcon>
            <path d="M6 9V3h12v6" />
            <rect x="3" y="9" width="18" height="8" rx="2" />
            <path d="M7 14h10v7H7z" />
          </SectionIcon>
          <h3>{t("settings.defaultPrinter")}</h3>
        </div>
        <p>{defaultPrinter ? defaultPrinter.name : t("settings.noDefaultPrinter")}</p>
        {canAdminister && (
          <Link className="btn" to="/printers">
            {t("settings.managePrinters")}
          </Link>
        )}
      </div>

      <StatisticsCard
        icon={
          <SectionIcon>
            <path d="M4 20V10M10 20V4M16 20v-7M22 20H2" />
          </SectionIcon>
        }
      />

      {canAdminister && (
      <>
      <BackupCard
        kind="database"
        icon={
          <SectionIcon>
            <ellipse cx="12" cy="5" rx="8" ry="3" />
            <path d="M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5" />
            <path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3" />
          </SectionIcon>
        }
      />

      <BackupCard
        kind="labels"
        icon={
          <SectionIcon>
            <path d="M20.6 13.4 13.4 20.6a2 2 0 0 1-2.8 0L3 13V3h10l7.6 7.6a2 2 0 0 1 0 2.8z" />
            <path d="M7.5 7.5h.01" />
          </SectionIcon>
        }
      />
      </>
      )}

      {user?.role === "Admin" && (
        <UsersCard
          icon={
            <SectionIcon>
              <circle cx="9" cy="8" r="3.5" />
              <path d="M2.5 20c0-3.6 2.9-6 6.5-6s6.5 2.4 6.5 6" />
              <path d="M16 4.5a3.5 3.5 0 0 1 0 7M18 14c2.2.6 3.5 2.8 3.5 6" />
            </SectionIcon>
          }
        />
      )}

      <div className="card settings-section settings-section--wide">
        <div className="settings-section__head">
          <SectionIcon>
            <circle cx="12" cy="12" r="9" />
            <path d="M12 11v5M12 8h.01" />
          </SectionIcon>
          <h3>{t("settings.about")}</h3>
        </div>
        <div>
          <dl className="settings-row">
            <dt>{t("settings.version")}</dt>
            <dd className="settings-version">
              <span>{__APP_VERSION__}</span>
              <UpdateStatus update={update} onCheck={() => runUpdateCheck(true)} />
            </dd>
          </dl>
          <dl className="settings-row">
            <dt>{t("settings.apiConnection")}</dt>
            <dd>
              <span className={`status-pill ${health ? "status-pill--success status-pill--live" : "status-pill--danger"}`}>
                {health ? t("settings.connected") : t("settings.disconnected")}
              </span>
            </dd>
          </dl>
          {health && (
            <dl className="settings-row">
              <dt>{t("settings.storagePath")}</dt>
              <dd>{health.storagePath}</dd>
            </dl>
          )}
        </div>
      </div>
      </div>
    </section>
  );
}

function UpdateStatus({
  update,
  onCheck,
}: {
  update: UpdateCheck | "checking" | "failed";
  onCheck: () => void;
}) {
  const { t } = useTranslation();

  if (update === "checking") {
    return <span className="settings-version__status">{t("settings.updateChecking")}</span>;
  }

  const checkAgain = (
    <button type="button" className="btn btn-sm" onClick={onCheck}>
      {t("settings.updateCheck")}
    </button>
  );

  if (update === "failed" || update.errorMessage || !update.latestVersion) {
    return (
      <>
        <span className="settings-version__status">{t("settings.updateFailed")}</span>
        {checkAgain}
      </>
    );
  }

  if (update.updateAvailable) {
    return (
      <>
        <span className="status-pill status-pill--info">
          {t("settings.updateAvailable", { version: update.latestVersion })}
        </span>
        {update.releaseUrl && (
          <a className="btn btn-sm" href={update.releaseUrl} target="_blank" rel="noreferrer">
            {t("settings.updateReleaseNotes")}
          </a>
        )}
      </>
    );
  }

  return (
    <>
      <span className="status-pill status-pill--success">{t("settings.updateUpToDate")}</span>
      {checkAgain}
    </>
  );
}

function SectionIcon({ children }: PropsWithChildren) {
  return (
    <span className="settings-section__icon" aria-hidden="true">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        {children}
      </svg>
    </span>
  );
}
