import { useCallback, useEffect, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import {
  backupDownloadUrl,
  BackupRequestError,
  createBackup,
  deleteBackup,
  listBackups,
  restoreBackup,
  type BackupInfo,
  type BackupKind,
} from "../api/backups";

type Pending = { action: "restore" | "delete"; backup: BackupInfo } | null;

function formatSize(bytes: number, locale: string): string {
  const units = ["B", "KB", "MB", "GB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${new Intl.NumberFormat(locale, { maximumFractionDigits: unit === 0 ? 0 : 1 }).format(value)} ${units[unit]}`;
}

/**
 * Backups of one kind (the whole database, or all label templates with their files), stored on
 * the server: create, download, restore and delete. Restoring backs up the current state first,
 * so a restore can itself be undone from the list.
 */
export function BackupCard({ kind, icon }: { kind: BackupKind; icon: ReactNode }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.language;
  const title = kind === "database" ? t("settings.backupDatabaseTitle") : t("settings.backupLabelsTitle");

  const [backups, setBackups] = useState<BackupInfo[] | null>(null);
  const [pending, setPending] = useState<Pending>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(
    () =>
      listBackups(kind)
        .then(setBackups)
        .catch(() => setBackups([])),
    [kind],
  );

  useEffect(() => {
    void reload();
  }, [reload]);

  const formatDate = (iso: string) => new Date(iso).toLocaleString(locale, { dateStyle: "medium", timeStyle: "short" });

  async function run(action: () => Promise<string>) {
    setBusy(true);
    setError(null);
    setMessage(null);

    try {
      setMessage(await action());
      setPending(null);
    } catch (e) {
      setError(e instanceof BackupRequestError ? e.message : t("settings.backupError"));
    } finally {
      setBusy(false);
      await reload();
    }
  }

  const create = () =>
    run(async () => {
      await createBackup(kind);
      return t("settings.backupCreated");
    });

  const confirm = (current: NonNullable<Pending>) =>
    run(async () => {
      if (current.action === "delete") {
        await deleteBackup(kind, current.backup.fileName);
        return t("settings.backupDeleted");
      }

      const result = await restoreBackup(kind, current.backup.fileName);
      return kind === "labels" && result.restoredTemplates !== null
        ? t("settings.backupRestoredLabels", { templates: result.restoredTemplates })
        : t("settings.backupRestored");
    });

  function confirmText(current: NonNullable<Pending>): string {
    const date = formatDate(current.backup.createdAt);
    if (current.action === "delete") return t("settings.backupDeleteConfirmText", { date });
    return kind === "database"
      ? t("settings.backupRestoreDatabaseConfirmText", { date })
      : t("settings.backupRestoreLabelsConfirmText", { date });
  }

  return (
    <div className="card settings-section settings-backup">
      <div className="settings-section__head">
        {icon}
        <h3>{title}</h3>
      </div>

      <p className="settings-backup__intro">
        {kind === "database" ? t("settings.backupDatabaseText") : t("settings.backupLabelsText")}
      </p>

      <div>
        <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void create()}>
          {busy && pending === null ? t("settings.backupCreating") : t("settings.backupCreate")}
        </button>
      </div>

      {backups === null ? (
        <p className="settings-backup__empty">{t("common.loading")}</p>
      ) : backups.length === 0 ? (
        <p className="settings-backup__empty">{t("settings.backupNone")}</p>
      ) : (
        <ul className="settings-backup__list" aria-label={title}>
          {backups.map((backup) => (
            <li key={backup.fileName} className="settings-backup__item">
              <div className="settings-backup__info">
                <span className="settings-backup__date">
                  {formatDate(backup.createdAt)}{" "}
                  <span className="settings-backup__size">({formatSize(backup.sizeBytes, locale)})</span>
                </span>
                {backup.beforeRestore && <span className="settings-backup__badge">{t("settings.backupBeforeRestore")}</span>}
              </div>
              <div className="settings-backup__actions">
                <a className="btn btn-sm" href={backupDownloadUrl(kind, backup.fileName)} download={backup.fileName}>
                  {t("settings.backupDownload")}
                </a>
                <button
                  type="button"
                  className="btn btn-sm"
                  disabled={busy}
                  onClick={() => {
                    setMessage(null);
                    setError(null);
                    setPending({ action: "restore", backup });
                  }}
                >
                  {t("settings.backupRestore")}
                </button>
                <button
                  type="button"
                  className="btn btn-sm btn-danger"
                  disabled={busy}
                  aria-label={`${t("common.delete")} ${formatDate(backup.createdAt)}`}
                  onClick={() => {
                    setMessage(null);
                    setError(null);
                    setPending({ action: "delete", backup });
                  }}
                >
                  {t("common.delete")}
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}

      {pending && (
        <div className="settings-confirm" role="group" aria-label={title}>
          <p>{confirmText(pending)}</p>
          <div className="settings-confirm__actions">
            <button type="button" className="btn btn-danger btn-sm settings-confirm__go" disabled={busy} onClick={() => void confirm(pending)}>
              {pending.action === "delete"
                ? busy
                  ? t("common.deleting")
                  : t("settings.backupDeleteConfirm")
                : busy
                  ? t("settings.backupRestoring")
                  : t("settings.backupRestoreConfirm")}
            </button>
            <button type="button" className="btn btn-sm" disabled={busy} onClick={() => setPending(null)}>
              {t("common.cancel")}
            </button>
          </div>
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
