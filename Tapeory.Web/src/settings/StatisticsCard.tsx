import { useEffect, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { fetchStats, resetStats, restoreAllTimeStats, type DashboardStats } from "../api/client";
import { formatCount, formatLength } from "../stats";

type Busy = "reset" | "restore" | null;

/**
 * Resets the print statistics shown on the dashboard. Nothing is deleted — the server only
 * remembers when counting (re)started — so the reset can be undone right here.
 */
export function StatisticsCard({ icon }: { icon: ReactNode }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.language;

  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState<Busy>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchStats()
      .then(setStats)
      .catch(() => setStats(null));
  }, []);

  async function run(action: Exclude<Busy, null>) {
    setBusy(action);
    setError(null);
    setMessage(null);

    try {
      const updated = action === "reset" ? await resetStats() : await restoreAllTimeStats();
      setStats(updated);
      setConfirming(false);
      setMessage(action === "reset" ? t("settings.statsResetDone") : t("settings.statsRestoreDone"));
    } catch {
      setError(t("settings.statsResetError"));
    } finally {
      setBusy(null);
    }
  }

  const since = stats?.statsSince ? new Date(stats.statsSince) : null;

  return (
    <div className="card settings-section settings-statistics">
      <div className="settings-section__head">
        {icon}
        <h3>{t("settings.statistics")}</h3>
      </div>

      <div>
        <dl className="settings-row">
          <dt>{t("settings.statsLabelsPrinted")}</dt>
          <dd>{stats ? formatCount(stats.labelsPrinted ?? 0, locale) : "…"}</dd>
        </dl>
        <dl className="settings-row">
          <dt>{t("settings.statsTapeUsed")}</dt>
          <dd>{stats ? formatLength(stats.totalPrintedLengthMm ?? 0, locale) : "…"}</dd>
        </dl>
        <dl className="settings-row">
          <dt>{t("settings.statsCountingSince")}</dt>
          <dd>
            {since
              ? since.toLocaleString(locale, { dateStyle: "medium", timeStyle: "short" })
              : t("settings.statsAllTime")}
          </dd>
        </dl>
      </div>

      {confirming ? (
        <div className="settings-confirm" role="group" aria-label={t("settings.statsReset")}>
          <p>{t("settings.statsResetConfirmText")}</p>
          <div className="settings-confirm__actions">
            <button type="button" className="btn btn-danger btn-sm settings-confirm__go" disabled={busy !== null} onClick={() => void run("reset")}>
              {busy === "reset" ? t("settings.statsResetting") : t("settings.statsResetConfirm")}
            </button>
            <button type="button" className="btn btn-sm" disabled={busy !== null} onClick={() => setConfirming(false)}>
              {t("common.cancel")}
            </button>
          </div>
        </div>
      ) : (
        <div className="settings-statistics__actions">
          <button
            type="button"
            className="btn btn-danger"
            disabled={busy !== null}
            onClick={() => {
              setMessage(null);
              setError(null);
              setConfirming(true);
            }}
          >
            {t("settings.statsReset")}
          </button>
          {since && (
            <button type="button" className="btn btn-sm" disabled={busy !== null} onClick={() => void run("restore")}>
              {busy === "restore" ? t("settings.statsRestoring") : t("settings.statsRestore")}
            </button>
          )}
        </div>
      )}

      {message && (
        <p className="settings-statistics__message" role="status">
          {message}
        </p>
      )}
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
