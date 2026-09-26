import { useEffect, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { fetchHealth, fetchStats, type DashboardStats, type HealthStatus } from "./api/client";
import { formatArea, formatCount, formatFactor, formatLength, pickComparison } from "./stats";
import "./DashboardPage.css";

const COUNT_UP_MS = 900;

/** Animates from 0 to `target` once it's known. Jumps straight to the value when motion is
 * reduced or matchMedia is unavailable (e.g. jsdom), so tests see the final number at once. */
function useCountUp(target: number | null): number | null {
  const [value, setValue] = useState<number | null>(null);

  useEffect(() => {
    if (target === null) return;

    const animate =
      typeof window.matchMedia === "function" &&
      !window.matchMedia("(prefers-reduced-motion: reduce)").matches &&
      target > 0;

    if (!animate) {
      setValue(target);
      return;
    }

    let frame = 0;
    const start = performance.now();

    const tick = (now: number) => {
      const progress = Math.min(1, (now - start) / COUNT_UP_MS);
      const eased = 1 - Math.pow(1 - progress, 3);
      setValue(Math.round(target * eased));
      if (progress < 1) frame = requestAnimationFrame(tick);
    };

    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [target]);

  return value;
}

function Icon({ children }: { children: ReactNode }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      {children}
    </svg>
  );
}

const ICONS = {
  templates: (
    <Icon>
      <rect x="4" y="3" width="16" height="18" rx="2" />
      <path d="M8 8h8M8 12h8M8 16h5" />
    </Icon>
  ),
  printers: (
    <Icon>
      <path d="M6 9V3h12v6" />
      <rect x="3" y="9" width="18" height="8" rx="2" />
      <path d="M7 14h10v7H7z" />
    </Icon>
  ),
  labels: (
    <Icon>
      <path d="M3 5h11l7 7-7 7H3z" />
      <circle cx="8" cy="9" r="1.5" />
    </Icon>
  ),
  jobs: (
    <Icon>
      <path d="M4 12l5 5L20 6" />
    </Icon>
  ),
};

interface StatCardProps {
  to: string;
  icon: ReactNode;
  label: string;
  value: number | null;
  sub?: string;
  highlight?: boolean;
  index: number;
}

function StatCard({ to, icon, label, value, sub, highlight, index }: StatCardProps) {
  const { i18n } = useTranslation();
  const animated = useCountUp(value);

  return (
    <Link
      to={to}
      className={`stat-card${highlight ? " stat-card--highlight" : ""}`}
      style={{ animationDelay: `${index * 70}ms` }}
    >
      <span className="stat-card__glow" aria-hidden="true" />
      <span className="stat-card__icon" aria-hidden="true">
        {icon}
      </span>
      <span className="stat-card__label">{label}</span>
      <span className="stat-card__value">
        {animated === null ? <span className="stat-card__skeleton" /> : formatCount(animated, i18n.language)}
      </span>
      {sub && <span className="stat-card__sub">{sub}</span>}
    </Link>
  );
}

type PillTone = "success" | "danger" | "neutral";

function StatusRow({
  name,
  hint,
  wide,
  children,
}: {
  name: string;
  hint: string;
  /** Puts the value on its own full-width line — for long values like a folder path. */
  wide?: boolean;
  children: ReactNode;
}) {
  return (
    <div className={`dashboard-stats__row dashboard-status__row${wide ? " dashboard-status__row--wide" : ""}`}>
      <dt>
        <span className="dashboard-status__name">{name}</span>
        <span className="dashboard-status__hint">{hint}</span>
      </dt>
      <dd>{children}</dd>
    </div>
  );
}

function Pill({ tone, live, children }: { tone: PillTone; live?: boolean; children: ReactNode }) {
  return <span className={`status-pill status-pill--${tone}${live ? " status-pill--live" : ""}`}>{children}</span>;
}

/** Each part of the system in plain words: what it is, and whether it's working. When the
 * health check itself fails the server is unreachable, so the other two are unknown. */
function SystemStatus({ health, error }: { health: HealthStatus | null; error: string | null }) {
  const { t } = useTranslation();
  const online = !error && health !== null;
  const databaseOk = online && health.databaseConnected;

  const summary = !online
    ? t("dashboard.system.summaryOffline")
    : databaseOk
      ? t("dashboard.system.summaryOk")
      : t("dashboard.system.summaryNoDatabase");

  return (
    <>
      <p
        className={`dashboard-status__summary${databaseOk ? "" : " dashboard-status__summary--problem"}`}
        role={databaseOk ? undefined : "alert"}
      >
        {summary}
      </p>

      <dl className="dashboard-stats">
        <StatusRow name={t("dashboard.system.server")} hint={t("dashboard.system.serverHint")}>
          {online ? (
            <Pill tone="success" live>
              {t("dashboard.system.online")}
            </Pill>
          ) : (
            <Pill tone="danger">{t("dashboard.system.offline")}</Pill>
          )}
        </StatusRow>

        <StatusRow name={t("dashboard.system.database")} hint={t("dashboard.system.databaseHint")}>
          {!online ? (
            <Pill tone="neutral">{t("dashboard.system.unknown")}</Pill>
          ) : health.databaseConnected ? (
            <Pill tone="success">{t("dashboard.system.connected")}</Pill>
          ) : (
            <Pill tone="danger">{t("dashboard.system.notConnected")}</Pill>
          )}
        </StatusRow>

        <StatusRow name={t("dashboard.system.storage")} hint={t("dashboard.system.storageHint")} wide={online}>
          {online ? (
            <code className="dashboard-status__path">{health.storagePath}</code>
          ) : (
            <Pill tone="neutral">{t("dashboard.system.unknown")}</Pill>
          )}
        </StatusRow>
      </dl>

      {error && <p className="dashboard-status__detail">{t("dashboard.system.errorDetail", { message: error })}</p>}
    </>
  );
}

export function DashboardPage() {
  const { t, i18n } = useTranslation();
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [statsError, setStatsError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    fetchHealth()
      .then((status) => {
        if (!cancelled) setHealth(status);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : t("dashboard.errorFallback"));
      });

    fetchStats()
      .then((result) => {
        if (!cancelled) setStats(result);
      })
      .catch((err: unknown) => {
        if (!cancelled) setStatsError(err instanceof Error ? err.message : t("dashboard.statsErrorFallback"));
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const locale = i18n.language;
  const finishedJobs = stats ? stats.completedPrintJobCount + stats.failedPrintJobCount : 0;
  const successRate = finishedJobs > 0 ? Math.round((stats!.completedPrintJobCount / finishedJobs) * 100) : null;
  const comparison = stats ? pickComparison(stats.totalPrintedLengthMm) : null;

  return (
    <section className="page-enter">
      <div className="page-header">
        <h2>{t("nav.dashboard")}</h2>
      </div>

      {statsError && <p role="alert">{statsError}</p>}
      {stats?.statsSince && (
        <p className="dashboard-since">
          {t("dashboard.statsSince", {
            date: new Date(stats.statsSince).toLocaleDateString(locale, { dateStyle: "medium" }),
          })}{" "}
          <Link to="/settings">{t("dashboard.statsSinceChange")}</Link>
        </p>
      )}

      <div className="stat-grid">
        <StatCard
          index={0}
          to="/templates"
          icon={ICONS.templates}
          label={t("dashboard.statTemplates")}
          value={stats?.templateCount ?? null}
        />
        <StatCard
          index={1}
          to="/printers"
          icon={ICONS.printers}
          label={t("dashboard.statPrinters")}
          value={stats?.printerCount ?? null}
        />
        <StatCard
          index={2}
          to="/print-jobs"
          icon={ICONS.labels}
          label={t("dashboard.statLabelsPrinted")}
          value={stats?.labelsPrinted ?? null}
          highlight
        />
        <StatCard
          index={3}
          to="/print-jobs"
          icon={ICONS.jobs}
          label={t("dashboard.statPrintJobs")}
          value={stats?.printJobCount ?? null}
          sub={
            stats
              ? successRate === null
                ? t("dashboard.noPrintsYet")
                : t("dashboard.successRate", { rate: successRate })
              : undefined
          }
        />
      </div>

      <div className="dashboard-row">
        <div className="card dashboard-card dashboard-facts">
          <h3>{t("dashboard.funFactsTitle")}</h3>

          {stats && (
            <>
              <dl className="dashboard-stats">
                <div className="dashboard-stats__row">
                  <dt>{t("dashboard.totalLength")}</dt>
                  <dd>{formatLength(stats.totalPrintedLengthMm, locale)}</dd>
                </div>
                <div className="dashboard-stats__row">
                  <dt>{t("dashboard.totalArea")}</dt>
                  <dd>{formatArea(stats.totalPrintedAreaMm2, locale)}</dd>
                </div>
                <div className="dashboard-stats__row">
                  <dt>{t("dashboard.mostPrinted")}</dt>
                  <dd>
                    {stats.mostPrintedTemplateName
                      ? t("dashboard.mostPrintedValue", {
                          name: stats.mostPrintedTemplateName,
                          count: stats.mostPrintedTemplateCount,
                        })
                      : "—"}
                  </dd>
                </div>
                <div className="dashboard-stats__row">
                  <dt>{t("dashboard.lastPrinted")}</dt>
                  <dd>
                    {stats.lastPrintedAt ? new Date(stats.lastPrintedAt).toLocaleString(locale) : t("dashboard.never")}
                  </dd>
                </div>
              </dl>

              <p className="dashboard-comparison">
                <span className="dashboard-comparison__icon" aria-hidden="true">
                  <Icon>
                    <rect x="2" y="8" width="20" height="8" rx="1.5" />
                    <path d="M6 8v3M10 8v4M14 8v3M18 8v4" />
                  </Icon>
                </span>
                {comparison
                  ? t("dashboard.comparison", {
                      factor: formatFactor(comparison.factor, locale),
                      thing: t(`dashboard.things.${comparison.key}`),
                    })
                  : t("dashboard.comparisonEmpty")}
              </p>
            </>
          )}
        </div>

        <div className="card dashboard-card">
          <h3>{t("dashboard.title")}</h3>

          {!error && !health ? (
            <p className="dashboard-status__summary">{t("dashboard.checking")}</p>
          ) : (
            <SystemStatus health={health} error={error} />
          )}
        </div>
      </div>
    </section>
  );
}
