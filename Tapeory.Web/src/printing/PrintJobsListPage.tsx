import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { deleteAllPrintJobs, deletePrintJob, listPrintJobs, type PrintJobResponse } from "../api/printJobs";
import { DataGrid, DataGridRow, useUrlSort } from "../components/DataGrid";
import { EyeIcon, PrinterIcon, TrashIcon } from "../components/icons";
import { useNotifications } from "../notifications/NotificationsContext";
import { formatRelativeTime } from "../relativeTime";
import {
  isPrintJobSortKey,
  labelCount,
  PRINT_JOB_SORT_DESCENDING_FIRST,
  sortPrintJobs,
  type PrintJobSortKey,
} from "./printJobSort";
import "./printing.css";

const COLUMNS: { key: PrintJobSortKey; label: string }[] = [
  { key: "job", label: "printing.printJobsList.columnJob" },
  { key: "template", label: "printing.printJobsList.columnTemplate" },
  { key: "printer", label: "printing.printJobsList.columnPrinter" },
  { key: "labels", label: "printing.printJobsList.columnLabels" },
  { key: "status", label: "printing.printJobsList.columnStatus" },
  { key: "created", label: "printing.printJobsList.columnCreated" },
];

/** The server refuses to delete these: the print processor is about to work on them, or is. */
function isStillPrinting(job: PrintJobResponse) {
  return job.status === "Queued" || job.status === "Processing";
}

export function PrintJobsListPage() {
  const { t, i18n } = useTranslation();
  const { notify } = useNotifications();
  const [jobs, setJobs] = useState<PrintJobResponse[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [sort, setSort] = useUrlSort(isPrintJobSortKey);
  const [busy, setBusy] = useState<number | "all" | null>(null);

  useEffect(() => {
    let cancelled = false;

    listPrintJobs()
      .then((results) => {
        if (!cancelled) setJobs(results);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : t("printing.printJobsList.loadErrorFallback"));
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handleDelete(job: PrintJobResponse) {
    if (!confirm(t("printing.printJobsList.confirmDeleteJob", { id: job.id }))) return;

    setBusy(job.id);
    try {
      await deletePrintJob(job.id);
      setJobs((prev) => prev?.filter((item) => item.id !== job.id) ?? prev);
      notify(t("printing.printJobsList.jobDeleted", { id: job.id }), "success");
    } catch (err) {
      notify(err instanceof Error ? err.message : t("printing.printJobsList.deleteErrorFallback"), "error");
    } finally {
      setBusy(null);
    }
  }

  async function handleDeleteAll() {
    if (!jobs || !confirm(t("printing.printJobsList.confirmDeleteAll", { count: jobs.length }))) return;

    setBusy("all");
    try {
      const result = await deleteAllPrintJobs();
      // Reload rather than filter locally: a job that was still printing a moment ago may have
      // finished (and been deleted) meanwhile.
      setJobs(await listPrintJobs());
      const kept = result.skippedInProgress > 0
        ? ` ${t("printing.printJobsList.keptStillPrinting", { count: result.skippedInProgress })}`
        : "";
      notify(t("printing.printJobsList.allDeleted", { count: result.deleted }) + kept, "success");
    } catch (err) {
      notify(err instanceof Error ? err.message : t("printing.printJobsList.deleteErrorFallback"), "error");
    } finally {
      setBusy(null);
    }
  }

  const deletableCount = jobs?.filter((job) => !isStillPrinting(job)).length ?? 0;

  const rows = useMemo(
    () => (jobs && sort ? sortPrintJobs(jobs, sort.key, sort.direction, i18n.language) : jobs),
    [jobs, sort, i18n.language],
  );

  const columns = COLUMNS.map((column) => ({ key: column.key, label: t(column.label) }));
  const numberFormat = new Intl.NumberFormat(i18n.language);
  const dateFormat = new Intl.DateTimeFormat(i18n.language, { dateStyle: "medium", timeStyle: "short" });

  return (
    <section className="page-enter">
      <div className="page-header">
        <h2>{t("printing.printJobsList.title")}</h2>
        {jobs && jobs.length > 0 && (
          <div className="page-header__actions">
            <button
              type="button"
              className="btn btn-danger print-history__delete-all"
              onClick={() => void handleDeleteAll()}
              disabled={busy !== null || deletableCount === 0}
              title={deletableCount === 0 ? t("printing.printJobsList.deleteAllOnlyPrinting") : undefined}
            >
              <TrashIcon />
              {busy === "all" ? t("printing.printJobsList.deletingAll") : t("printing.printJobsList.deleteAll")}
            </button>
          </div>
        )}
      </div>

      {error && <p role="alert">{error}</p>}
      {!error && !rows && <p>{t("printing.printJobsList.loading")}</p>}
      {rows && rows.length === 0 && <p>{t("printing.printJobsList.empty")}</p>}

      {rows && rows.length > 0 && (
        <DataGrid
          columns={columns}
          sort={sort}
          onSortChange={setSort}
          descendingFirst={PRINT_JOB_SORT_DESCENDING_FIRST}
          hasActions
          footer={t("printing.printJobsList.gridCount", { count: rows.length })}
        >
          {rows.map((job) => {
            const detailUrl = `/print-jobs/${job.id}`;
            const status = job.status.toLowerCase();
            return (
              <DataGridRow key={job.id} href={detailUrl}>
                <td className="data-grid__primary data-grid__col--job">
                  <span className="job-cell">
                    <span className={`job-cell__tile job-cell__tile--${status}`} aria-hidden="true">
                      <PrinterIcon />
                    </span>
                    <span className="data-grid__title">
                      <Link to={detailUrl}>#{job.id}</Link>
                      <span className="data-grid__subtitle">
                        {t("printing.printJobsList.itemsCount", { count: job.items.length })}
                      </span>
                    </span>
                  </span>
                </td>
                <td className="data-grid__col--template">
                  <span className="data-grid__title">
                    {job.templateDeleted ? (
                      <span className="data-grid__muted">{job.templateName}</span>
                    ) : (
                      <Link to={`/templates/${job.templateId}/edit`}>{job.templateName}</Link>
                    )}
                    <span className="data-grid__subtitle data-grid__hide-mobile-inline">
                      {job.templateDeleted
                        ? t("printing.printJobsList.templateDeleted")
                        : t("templates.versionShort", { version: job.templateVersionNumber })}
                    </span>
                  </span>
                </td>
                <td className="data-grid__col--printer data-grid__hide-mobile">
                  {job.printerName ? (
                    <span className="data-grid__with-icon">
                      <PrinterIcon />
                      {job.printerName}
                    </span>
                  ) : (
                    <span className="data-grid__empty">—</span>
                  )}
                </td>
                <td className="data-grid__col--labels data-grid__hide-mobile">
                  <span className="data-grid__number">{numberFormat.format(labelCount(job))}</span>
                </td>
                <td className="data-grid__col--status">
                  <span className={`status-badge status-badge--${status}`} title={job.errorMessage ?? undefined}>
                    {t(`printing.status.${status}`, { defaultValue: job.status })}
                  </span>
                </td>
                <td className="data-grid__col--created data-grid__end">
                  <time
                    className="data-grid__muted"
                    dateTime={job.createdAt}
                    title={dateFormat.format(new Date(job.createdAt))}
                  >
                    {formatRelativeTime(job.createdAt, i18n.language)}
                  </time>
                </td>
                <td className="data-grid__actions-cell">
                  <span className="data-grid__actions">
                    <Link
                      to={detailUrl}
                      className="icon-link"
                      aria-label={`${t("printing.printJobsList.viewDetails")}: #${job.id}`}
                      title={t("printing.printJobsList.viewDetails")}
                    >
                      <EyeIcon />
                    </Link>
                    {!job.templateDeleted && (
                      <Link
                        to={`/templates/${job.templateId}/print`}
                        className="icon-link icon-link--primary"
                        aria-label={`${t("printing.printJobsList.printAgain")}: ${job.templateName}`}
                        title={t("printing.printJobsList.printAgain")}
                      >
                        <PrinterIcon />
                      </Link>
                    )}
                    <button
                      type="button"
                      className="icon-link icon-link--danger"
                      onClick={() => void handleDelete(job)}
                      disabled={busy !== null || isStillPrinting(job)}
                      aria-label={`${t("printing.printJobsList.deleteJob")}: #${job.id}`}
                      title={
                        isStillPrinting(job)
                          ? t("printing.printJobsList.deleteJobStillPrinting")
                          : t("printing.printJobsList.deleteJob")
                      }
                    >
                      <TrashIcon />
                    </button>
                  </span>
                </td>
              </DataGridRow>
            );
          })}
        </DataGrid>
      )}
    </section>
  );
}
