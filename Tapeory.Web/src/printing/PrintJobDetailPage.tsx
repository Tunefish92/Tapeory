import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { cancelPrintJob, getPrintJob, reprintUnprinted, type PrintJobResponse } from "../api/printJobs";
import { Pager } from "../components/Pager";
import { ProgressBar } from "../components/ProgressBar";
import "./printing.css";

const POLL_INTERVAL_MS = 1500;
/** How many rows of a job are shown at a time. */
const PAGE_SIZE = 60;
const TERMINAL_STATUSES = new Set(["Completed", "Failed", "Cancelled"]);
/** Rows that didn't come out of the printer and can be printed again. */
const UNPRINTED_STATUSES = new Set(["Failed", "Cancelled"]);

export function PrintJobDetailPage() {
  const { t } = useTranslation();
  const params = useParams<{ id: string }>();
  const jobId = Number(params.id);

  const [job, setJob] = useState<PrintJobResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(0);
  const [stopping, setStopping] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const navigate = useNavigate();

  async function stop() {
    setStopping(true);
    setActionError(null);

    try {
      // A printing job answers still in progress; the polling picks up the end.
      setJob(await cancelPrintJob(jobId));
    } catch (err) {
      setStopping(false);
      setActionError(err instanceof Error ? err.message : t("printing.printJobDetail.stopErrorFallback"));
    }
  }

  async function reprint() {
    setActionError(null);

    try {
      const created = await reprintUnprinted(jobId);
      setStopping(false);
      navigate(`/print-jobs/${created.id}`);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : t("printing.printJobDetail.reprintErrorFallback"));
    }
  }

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

  // Rows of a bulk job that are through, printed or failed.
  const finishedItems = job.items.filter((item) => TERMINAL_STATUSES.has(item.status)).length;
  const finished = TERMINAL_STATUSES.has(job.status);
  const unprintedItems = job.items.filter((item) => UNPRINTED_STATUSES.has(item.status)).length;

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

      {!finished && (
        <div className="card print-job-progress">
          {job.items.length > 1 && (
            <ProgressBar
              fraction={finishedItems / job.items.length}
              status={t("printing.printJobDetail.progress", { done: finishedItems, total: job.items.length })}
            />
          )}
          <div className="print-form__actions">
            <button type="button" className="btn btn-danger" disabled={stopping} onClick={() => void stop()}>
              {stopping ? t("printing.printJobDetail.stopping") : t("printing.printJobDetail.stop")}
            </button>
            <span className="print-form__hint">{t("printing.printJobDetail.stopHint")}</span>
          </div>
        </div>
      )}

      {finished && unprintedItems > 0 && !job.templateDeleted && (
        <div className="card print-job-progress">
          <div className="print-form__actions">
            <button type="button" className="btn btn-primary" onClick={() => void reprint()}>
              {t("printing.printJobDetail.reprintUnprinted", { count: unprintedItems })}
            </button>
            <span className="print-form__hint">{t("printing.printJobDetail.reprintHint")}</span>
          </div>
        </div>
      )}

      {actionError && <p role="alert">{actionError}</p>}

      {job.errorMessage && <p role="alert">{job.errorMessage}</p>}

      <div className="print-job-items">
        {job.items.slice(page * PAGE_SIZE, (page + 1) * PAGE_SIZE).map((item) => (
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
              <img loading="lazy" src={item.previewUrl} alt={t("printing.printJobDetail.itemPreviewAlt", { id: item.id })} />
            )}
          </div>
        ))}
      </div>
      <Pager page={page} pageSize={PAGE_SIZE} total={job.items.length} onPage={setPage} infoKey="printing.printJobDetail.pageInfo" />
    </section>
  );
}
