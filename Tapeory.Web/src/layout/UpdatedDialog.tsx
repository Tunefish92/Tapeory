import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { getInstalledVersion, type InstalledVersion } from "../api/client";
import { compareVersions } from "../versions";
import { ReleaseNotes } from "./ReleaseNotes";
import { RELEASES } from "./releases";

/** The version whose "what's new" this browser has already shown (or didn't need to). */
export const SEEN_VERSION_KEY = "tapeory.seenVersion";

const RELEASES_URL = "https://github.com/Tunefish92/Tapeory/releases";

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
 * After a jump over several versions, it shows the changes of each of them.
 */
export function UpdatedDialog() {
  const { t, i18n } = useTranslation();
  const [installed, setInstalled] = useState<InstalledVersion | null>(null);
  // The changes of every version after this one are shown.
  const [since, setSince] = useState<string | null>(null);
  const closeButton = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    let cancelled = false;

    getInstalledVersion()
      .then((answer) => {
        const seen = readSeen();
        if (cancelled || seen === answer.version) return;
        if (!answer.updated) {
          markSeen(answer.version);
          return;
        }

        // A browser that last saw an older version than the one before this update (it wasn't
        // opened in between) gets the changes since then as well.
        const serverSince = answer.changesSince ?? answer.previousVersion;
        const older =
          seen !== null && compareVersions(seen, answer.version) < 0 && (serverSince === null || compareVersions(seen, serverSince) < 0);
        setSince(older ? seen : serverSince);
        setInstalled(answer);
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

  // The notes come with this build of the web app, newest first.
  const releases = RELEASES.filter(
    (release) =>
      compareVersions(release.version, installed.version) <= 0 &&
      (since === null ? release.version === installed.version : compareVersions(release.version, since) > 0),
  );
  const several = releases.length > 1;

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

        {releases.length > 0 && (
          <div className="updated-dialog__notes" tabIndex={0} aria-label={t("updated.whatsNew")}>
            {!i18n.language.startsWith("en") && <p className="updated-dialog__hint">{t("updated.englishOnly")}</p>}
            {several && <p>{t("updated.severalVersions", { count: releases.length })}</p>}
            {releases.map((release) => (
              <section key={release.version} className="updated-dialog__release">
                {several && (
                  <h4 className="updated-dialog__version">
                    {release.version}
                    {release.date && <span> · {release.date}</span>}
                  </h4>
                )}
                <ReleaseNotes markdown={release.notes} />
              </section>
            ))}
          </div>
        )}

        <div className="updated-dialog__actions">
          <a className="btn" href={several ? RELEASES_URL : `${RELEASES_URL}/tag/v${installed.version}`} target="_blank" rel="noreferrer">
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
