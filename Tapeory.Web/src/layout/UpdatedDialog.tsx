import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { getInstalledVersion, type InstalledVersion } from "../api/client";
import { ReleaseNotes } from "./ReleaseNotes";

/** The version whose "what's new" this browser has already shown (or didn't need to). */
export const SEEN_VERSION_KEY = "tapeory.seenVersion";

const RELEASES_URL = "https://github.com/Tunefish92/Tapeory/releases/tag/v";

function readSeen(): string | null {
  try {
    return localStorage.getItem(SEEN_VERSION_KEY);
  } catch {
    return null;
  }
}

function markSeen(version: string) {
  try {
    localStorage.setItem(SEEN_VERSION_KEY, version);
  } catch {
    // Private mode or blocked storage: the dialog may simply come again next time.
  }
}

/**
 * After Tapeory was updated, shows once per browser what the new version brings: the server
 * knows whether this version came as an update, the browser remembers that it has shown it.
 */
export function UpdatedDialog() {
  const { t, i18n } = useTranslation();
  const [installed, setInstalled] = useState<InstalledVersion | null>(null);
  const closeButton = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    let cancelled = false;

    getInstalledVersion()
      .then((answer) => {
        if (cancelled || readSeen() === answer.version) return;
        if (answer.updated) setInstalled(answer);
        else markSeen(answer.version);
      })
      .catch(() => {
        // Nothing to show; the app works without it.
      });

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!installed) return;
    closeButton.current?.focus();

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") close();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [installed]);

  if (!installed) return null;

  function close() {
    markSeen(installed!.version);
    setInstalled(null);
  }

  // The notes come with this build of the web app; they belong to its version.
  const notes = __APP_VERSION__ === installed.version ? __RELEASE_NOTES__ : "";

  return (
    <div className="modal-backdrop" onClick={(event) => event.target === event.currentTarget && close()}>
      <div className="card modal updated-dialog" role="dialog" aria-modal="true" aria-labelledby="updated-dialog-title">
        <div className="updated-dialog__header">
          <img src="/logo.svg" alt="" width={40} height={40} />
          <div>
            <h3 id="updated-dialog-title">{t("updated.title", { version: installed.version })}</h3>
            <p className="updated-dialog__subtitle">
              {installed.previousVersion
                ? t("updated.fromVersion", { previous: installed.previousVersion })
                : t("updated.intro")}
            </p>
          </div>
        </div>

        {notes && (
          <div className="updated-dialog__notes" tabIndex={0} aria-label={t("updated.whatsNew")}>
            {!i18n.language.startsWith("en") && <p className="updated-dialog__hint">{t("updated.englishOnly")}</p>}
            <ReleaseNotes markdown={notes} />
          </div>
        )}

        <div className="updated-dialog__actions">
          <a className="btn" href={`${RELEASES_URL}${installed.version}`} target="_blank" rel="noreferrer">
            {t("updated.allNotes")}
          </a>
          <button type="button" className="btn btn-primary" ref={closeButton} onClick={close}>
            {t("updated.close")}
          </button>
        </div>
      </div>
    </div>
  );
}
