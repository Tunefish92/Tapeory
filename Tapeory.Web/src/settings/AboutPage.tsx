import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { checkForUpdates, fetchHealth, type HealthStatus, type UpdateCheck } from "../api/client";
import { SectionIcon } from "./SettingsPage";
import "./settings.css";

const REPOSITORY_URL = "https://github.com/Tunefish92/Tapeory";

/** About Tapeory: the version with the update check, and where this installation runs. */
export function AboutPage() {
  const { t } = useTranslation();
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [update, setUpdate] = useState<UpdateCheck | "checking" | "failed">("checking");

  function runUpdateCheck(refresh: boolean) {
    setUpdate("checking");
    checkForUpdates(refresh)
      .then(setUpdate)
      .catch(() => setUpdate("failed"));
  }

  useEffect(() => {
    fetchHealth()
      .then(setHealth)
      .catch(() => setHealth(null));

    runUpdateCheck(false);
  }, []);

  return (
    <section className="settings-page page-enter">
      <div className="page-header">
        <h2>{t("nav.about")}</h2>
      </div>

      <div className="about-grid">
      <div className="card settings-section about-card">
        <div className="settings-section__head">
          <SectionIcon>
            <circle cx="12" cy="12" r="9" />
            <path d="M12 11v5M12 8h.01" />
          </SectionIcon>
          <h3>{t("app.name")}</h3>
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

      <div className="card settings-section about-card about-links">
        <div className="settings-section__head">
          <SectionIcon>
            <circle cx="12" cy="12" r="9" />
            <path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18" />
          </SectionIcon>
          <h3>{t("about.links")}</h3>
        </div>
        <ul>
          <li>
            <a className="btn" href={REPOSITORY_URL} target="_blank" rel="noreferrer">
              {t("about.source")}
            </a>
          </li>
          <li>
            <a className="btn" href={`${REPOSITORY_URL}/issues`} target="_blank" rel="noreferrer">
              {t("about.issues")}
            </a>
          </li>
          <li>
            <a className="btn btn-primary" href={`${REPOSITORY_URL}/issues/new`} target="_blank" rel="noreferrer">
              {t("about.newIssue")}
            </a>
          </li>
        </ul>
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
