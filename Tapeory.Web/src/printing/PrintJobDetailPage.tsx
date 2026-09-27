import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { getPrintJob, type PrintJobResponse } from "../api/printJobs";
import "./printing.css";

const POLL_INTERVAL_MS = 1500;
const TERMINAL_STATUSES = new Set(["Completed", "Failed"]);

export function PrintJobDetailPage() {
  const { t } = useTranslation();
  const params = useParams<{ id: string }>();
  const jobId = Number(params.id);

  const [job, setJob] = useState<PrintJobResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    let timeoutId: ReturnType<typeof setTimeout> | undefined;

    async function poll() {
      try {
        const result = await getPrintJob(jobId);
        if (cancelled) return;

        setJob(result);
        setError(null);

        if (!TERMINAL_STATUSES.has(result.status)) {
          timeoutId = setTimeout(poll, POLL_INTERVAL_MS);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : t("printing.printJobDetail.loadErrorFallback"));
        }
      }
    }

    poll();

    return () => {
      cancelled = true;
      if (timeoutId) clearTimeout(timeoutId);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [jobId]);

  if (error) {
    return <p role="alert">{error}</p>;
  }

  if (!job) {
    return <p>{t("printing.printJobDetail.loading")}</p>;
  }

  return (
    <section className="print-page page-enter">
      <div className="page-header">
        <h2>{t("printing.printJobDetail.title", { id: job.id })}</h2>
        <div className="page-header__actions">
          <Link className="btn" to="/templates">
            <span className="back-arrow" aria-hidden="true">←</span> {t("printing.printJobDetail.backToTemplates")}
          </Link>
          <Link className="btn" to="/print-jobs">
            <span className="back-arrow" aria-hidden="true">←</span> {t("printing.printJobDetail.backToHistory")}
          </Link>
        </div>
      </div>

      <dl className="card print-job-summary">
        <dt>{t("printing.printJobDetail.template")}</dt>
        <dd>
          {job.templateName}
          {job.templateDeleted && ` (${t("printing.printJobsList.templateDeleted")})`}
        </dd>
        <dt>{t("printing.printJobDetail.printer")}</dt>
        <dd>{job.printerName ?? "—"}</dd>
        {job.printedBy && (
          <>
            <dt>{t("printing.printJobDetail.printedBy")}</dt>
            <dd>{job.printedBy}</dd>
          </>
        )}
        {job.quality && (
          <>
            <dt>{t("printing.quality.label")}</dt>
            <dd>{t(`printing.quality.${job.quality.toLowerCase()}`, { defaultValue: job.quality })}</dd>
          </>
        )}
        {job.cutMode && (
          <>
            <dt>{t("printing.cutMode.label")}</dt>
            <dd>{t(`printing.cutMode.${job.cutMode}`, { defaultValue: job.cutMode })}</dd>
          </>
        )}
        <dt>{t("printing.printJobDetail.status")}</dt>
        <dd>
          <span className={`status-badge status-badge--${job.status.toLowerCase()}`}>
            {t(`printing.status.${job.status.toLowerCase()}`, { defaultValue: job.status })}
          </span>
          {!TERMINAL_STATUSES.has(job.status) && t("printing.printJobDetail.refreshing")}
        </dd>
        <dt>{t("printing.printJobDetail.created")}</dt>
        <dd>{new Date(job.createdAt).toLocaleString()}</dd>
        {job.completedAt && (
          <>
            <dt>{t("printing.printJobDetail.completed")}</dt>
            <dd>{new Date(job.completedAt).toLocaleString()}</dd>
          </>
        )}
      </dl>

      {job.errorMessage && <p role="alert">{job.errorMessage}</p>}

      <div className="print-job-items">
        {job.items.map((item) => (
          <div key={item.id} className="print-job-item">
            <p>
              {t("printing.printJobDetail.quantity", { count: item.quantity })} —{" "}
              <span className={`status-badge status-badge--${item.status.toLowerCase()}`}>
                {t(`printing.status.${item.status.toLowerCase()}`, { defaultValue: item.status })}
              </span>
            </p>
            {item.errorMessage &&
              (item.status === "Failed" ? (
                <p role="alert">{item.errorMessage}</p>
              ) : (
                <p className="print-job-item__note">{item.errorMessage}</p>
              ))}
            {item.previewUrl && (
              <img src={item.previewUrl} alt={t("printing.printJobDetail.itemPreviewAlt", { id: item.id })} />
            )}
          </div>
        ))}
      </div>
    </section>
  );
}
