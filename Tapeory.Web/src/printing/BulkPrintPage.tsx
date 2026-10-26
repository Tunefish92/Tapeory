import { useEffect, useMemo, useRef, useState, type DragEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { getTemplate, type TemplateDetailResponse } from "../api/templates";
import { createPrintJob, previewTemplate, type CutMode } from "../api/printJobs";
import {
  CHECK_CHUNK_SIZE,
  MAX_FILE_MB,
  checkRows,
  fetchPrintData,
  parsePrintData,
  type DataUrl, type CheckedRow, type PrintData } from "../api/printData";
import {
  deleteBulkPrintProfile,
  listBulkPrintProfiles,
  ProfileNameTakenError,
  saveBulkPrintProfile,
  type BulkPrintProfile,
} from "../api/bulkPrintProfiles";
import { PrinterIcon, TrashIcon } from "../components/icons";
import { Pager } from "../components/Pager";
import { ProgressBar } from "../components/ProgressBar";
import { PrintModeSwitch } from "./PrintModeSwitch";
import { PrintOptionsFields, usePrintOptions } from "./PrintOptions";
import { canKeepFiles, droppedFile, keepFile, pickDataFile, reopenFile, type KeptFile } from "./fileHandles";
import {
  applyProfile,
  buildRows,
  clampCopies,
  columnLetter,
  fieldNeedsColumn,
  initialMapping,
  MAX_COPIES,
  missingFields,
  profileSettings,
  saveMapping,
  type BulkRow,
  type ColumnMapping,
} from "./bulkPrint";
import "./printing.css";
import "./bulkPrint.css";

type Step = "file" | "match" | "check" | "print";
const STEPS: Step[] = ["file", "match", "check", "print"];

/** The separators offered when the file's own couldn't be told. */
const SEPARATORS = [",", ";", "tab", "|", "space", "none"] as const;
const SEPARATOR_KEYS: Record<string, string> = {
  ",": "comma",
  ";": "semicolon",
  tab: "tab",
  "|": "pipe",
  space: "space",
  none: "none",
};

/** How many rows the check's table shows at a time. */
const PAGE_SIZE = 100;

/** How many rows of the file the matching step shows. */
const SAMPLE_ROWS = 5;

/** The ways to import data, one tab each; more can be added here. */
const IMPORT_TABS = ["file", "api"] as const;
type ImportTab = (typeof IMPORT_TABS)[number];

/** Where the data comes from: a file the user chose, or a web address the server asks. */
type DataSource = { kind: "file"; file: File } | ({ kind: "url" } & DataUrl);

type Busy =
  | { kind: "upload"; fraction: number }
  | { kind: "reading" }
  | { kind: "checking"; done: number; total: number };

export function BulkPrintPage() {
  const { t } = useTranslation();
  const params = useParams<{ id: string }>();
  const templateId = Number(params.id);
  const navigate = useNavigate();

  const [template, setTemplate] = useState<TemplateDetailResponse | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [step, setStep] = useState<Step>("file");
  const [busy, setBusy] = useState<Busy | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [source, setSource] = useState<DataSource | null>(null);
  // Which way of importing the first step shows.
  const [importTab, setImportTab] = useState<ImportTab>("file");
  // The web address form on the first step.
  const [dataUrl, setDataUrl] = useState<DataUrl>({ url: "", headerName: "", headerValue: "" });
  const [data, setData] = useState<PrintData | null>(null);
  const [hasHeader, setHasHeader] = useState(false);
  const [mapping, setMapping] = useState<ColumnMapping>({});
  const [quantityColumn, setQuantityColumn] = useState<number | null>(null);
  // The user picked the separator (or confirmed the guess), so the question is settled.
  // How many times each label is printed, on top of a row's own copies.
  const [copies, setCopies] = useState(1);
  // A file is being dragged over the page.
  const [dragging, setDragging] = useState(false);
  const [separatorChosen, setSeparatorChosen] = useState(false);
  const [otherSeparator, setOtherSeparator] = useState("");

  const [rows, setRows] = useState<BulkRow[]>([]);
  const [checked, setChecked] = useState<CheckedRow[]>([]);
  const [ticked, setTicked] = useState<boolean[]>([]);
  const [current, setCurrent] = useState(0);
  const [onlyProblems, setOnlyProblems] = useState(false);
  const checkAbort = useRef<AbortController | null>(null);
  const [page, setPage] = useState(0);

  // The rows the table lists (all, or only those with problems), a page at a time.
  const listed = useMemo(
    () =>
      rows
        .map((_, index) => index)
        .filter((index) => !onlyProblems || (checked[index]?.errors.length ?? 0) + (checked[index]?.warnings.length ?? 0) > 0),
    [rows, checked, onlyProblems],
  );

  // The table follows the label being looked at.
  useEffect(() => {
    const position = listed.indexOf(current);
    if (position >= 0) setPage(Math.floor(position / PAGE_SIZE));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [current, onlyProblems]);

  // Saved profiles: the list, the one in use (its name is offered when saving again), and one
  // that is waiting for its file to be chosen.
  const [profiles, setProfiles] = useState<BulkPrintProfile[]>([]);
  const [activeProfile, setActiveProfile] = useState<BulkPrintProfile | null>(null);
  const [pendingProfile, setPendingProfile] = useState<BulkPrintProfile | null>(null);
  const [keptFile, setKeptFile] = useState<KeptFile | null>(null);
  const [profileName, setProfileName] = useState("");
  const [savingProfile, setSavingProfile] = useState(false);
  const [profileNotice, setProfileNotice] = useState<string | null>(null);

  const [submitting, setSubmitting] = useState(false);
  const options = usePrintOptions(template ? template.currentVersion.heightMm : null);

  const fields = useMemo(() => template?.currentVersion.fields ?? [], [template]);

  useEffect(() => {
    let cancelled = false;

    getTemplate(templateId)
      .then((detail) => {
        if (!cancelled) setTemplate(detail);
      })
      .then(() => listBulkPrintProfiles(templateId))
      .then((saved) => {
        if (!cancelled) setProfiles(saved);
      })
      .catch((err: unknown) => {
        if (!cancelled) setLoadError(err instanceof Error ? err.message : t("printing.printTemplate.loadErrorFallback"));
      });

    return () => {
      cancelled = true;
      checkAbort.current?.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [templateId]);

  // --- Step 1 and 2: reading the file ---

  async function readSource(
    chosen: DataSource,
    choice: { separator?: string; sheet?: number } = {},
    profile: BulkPrintProfile | null = null,
  ) {
    if (chosen.kind === "file" && chosen.file.size > MAX_FILE_MB * 1024 * 1024) {
      setError(t("printing.bulk.file.tooLarge", { size: MAX_FILE_MB }));
      return;
    }

    // A profile says how its file is read; the user's own choice on this page comes first.
    const read = profile
      ? { separator: profile.settings.separator ?? undefined, sheet: profile.settings.sheet ?? undefined, ...choice }
      : choice;

    setError(null);
    setSource(chosen);
    setBusy(chosen.kind === "file" ? { kind: "upload", fraction: 0 } : { kind: "reading" });

    try {
      const parsed =
        chosen.kind === "file"
          ? await parsePrintData(templateId, chosen.file, {
              ...read,
              onUploadProgress: (fraction) => setBusy(fraction < 1 ? { kind: "upload", fraction } : { kind: "reading" }),
            })
          : await fetchPrintData(templateId, chosen, read);
      const matched = profile
        ? applyProfile(profile.settings, parsed, fields)
        : { hasHeader: parsed.hasHeader, ...initialMapping(templateId, parsed, fields) };
      const settled = parsed.separatorDetected || read.separator !== undefined;

      setData(parsed);
      setHasHeader(matched.hasHeader);
      setMapping(matched.mapping);
      setQuantityColumn(matched.quantityColumn);
      setSeparatorChosen(read.separator !== undefined);
      setBusy(null);

      if (profile) {
        setActiveProfile(profile);
        setPendingProfile(null);
        setProfileName(profile.name);
        setCopies(clampCopies(profile.settings.copies));
        setProfileNotice(null);
        options.apply({
          printerId: profile.settings.printerId,
          printerName: profile.settings.printerName,
          quality: profile.settings.quality ?? undefined,
          cutMode: (profile.settings.cutMode as CutMode | null) ?? undefined,
        });
      }

      // Nothing to ask: a profile that still fits the file, or a clear separator and a header
      // that names every field that needs a column.
      const complete = missingFields(fields, matched.mapping).length === 0;
      const nothingToAsk = profile
        ? complete && parsed.rows.length > (matched.hasHeader ? 1 : 0)
        : choice.separator === undefined && choice.sheet === undefined && settled && parsed.hasHeader && complete;

      if (nothingToAsk) {
        await check(parsed, matched.hasHeader, matched.mapping, matched.quantityColumn);
      } else {
        setStep("match");
      }
    } catch (err) {
      setBusy(null);
      setError(err instanceof Error ? err.message : t("printing.bulk.file.errorFallback"));
    }
  }

  /** A file the user chose: for the profile that is waiting for one, or a fresh start. */
  function fileChosen(chosen: File, kept: KeptFile | null) {
    setKeptFile(kept);
    if (!pendingProfile) {
      setActiveProfile(null);
      setProfileNotice(null);
    }
    void readSource({ kind: "file", file: chosen }, {}, pendingProfile);
  }

  /** The data of a web address: the server gets it. */
  function loadUrl() {
    setKeptFile(null);
    setActiveProfile(null);
    setPendingProfile(null);
    setProfileNotice(null);
    void readSource({ kind: "url", ...dataUrl, url: dataUrl.url.trim() });
  }

  /** Loads a saved profile: its file if the browser kept it, otherwise after the user picks it. */
  async function loadProfile(profile: BulkPrintProfile) {
    setError(null);

    // A profile for a web address needs nothing from the user.
    if (profile.settings.url) {
      const address = {
        url: profile.settings.url,
        headerName: profile.settings.urlHeaderName ?? "",
        headerValue: profile.settings.urlHeaderValue ?? "",
      };
      setKeptFile(null);
      setDataUrl(address);
      setImportTab("api");
      await readSource({ kind: "url", ...address }, {}, profile);
      return;
    }

    const reopened = await reopenFile(profile.id);

    if (reopened) {
      setKeptFile(reopened.kept);
      await readSource({ kind: "file", file: reopened.file }, {}, profile);
    } else {
      setPendingProfile(profile);
      setImportTab("file");
    }
  }

  async function removeProfile(profile: BulkPrintProfile) {
    if (!window.confirm(t("printing.bulk.profiles.confirmDelete", { name: profile.name }))) return;

    try {
      await deleteBulkPrintProfile(profile.id);
      void keepFile(profile.id, null);
      setProfiles((previous) => previous.filter((other) => other.id !== profile.id));
      if (pendingProfile?.id === profile.id) setPendingProfile(null);
      if (activeProfile?.id === profile.id) setActiveProfile(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("printing.bulk.profiles.loadErrorFallback"));
    }
  }

  async function saveProfile(replace = false) {
    if (!data || !source) return;
    setSavingProfile(true);
    setError(null);

    try {
      const name = profileName.trim();
      const saved = await saveBulkPrintProfile(
        templateId,
        name,
        {
          ...profileSettings(sourceName, data, hasHeader, mapping, quantityColumn, options.target, copies),
          ...(source.kind === "url"
            ? { fileName: null, url: source.url, urlHeaderName: source.headerName || null, urlHeaderValue: source.headerValue || null }
            : {}),
        },
        replace,
      );
      await keepFile(saved.id, keptFile);
      setProfiles(await listBulkPrintProfiles(templateId));
      setActiveProfile(saved);
      setProfileNotice(
        t(keptFile || source.kind === "url" ? "printing.bulk.profiles.saved" : "printing.bulk.profiles.savedNoFile", {
          name: saved.name,
        }),
      );
    } catch (err) {
      // The name is taken: ask before overwriting that profile.
      if (err instanceof ProfileNameTakenError) {
        if (window.confirm(t("printing.bulk.profiles.confirmReplace", { name: profileName.trim() }))) {
          await saveProfile(true);
        }
        return;
      }
      setError(err instanceof Error ? err.message : t("printing.bulk.profiles.saveErrorFallback"));
    } finally {
      setSavingProfile(false);
    }
  }

  // A file can be dropped anywhere on the page while the first step waits for one.
  const acceptsDrop = step === "file" && busy === null;

  function handleDragOver(event: DragEvent) {
    if (!Array.from(event.dataTransfer.types).includes("Files")) return;
    // Without this the browser would open the file instead.
    event.preventDefault();
    event.dataTransfer.dropEffect = acceptsDrop ? "copy" : "none";
    setDragging(acceptsDrop);
  }

  function handleDrop(event: DragEvent) {
    event.preventDefault();
    setDragging(false);
    if (!acceptsDrop) return;
    setImportTab("file");
    void droppedFile(event.dataTransfer).then((dropped) => dropped && fileChosen(dropped.file, dropped.kept));
  }

  // --- Step 3: checking and rendering every label ---

  async function check(
    source: PrintData,
    withHeader: boolean,
    columns: ColumnMapping,
    quantity: number | null,
  ) {
    const labels = buildRows(source, withHeader, fields, columns, quantity);
    saveMapping(templateId, source, withHeader, columns, quantity);

    const abort = new AbortController();
    checkAbort.current = abort;
    setError(null);
    setRows(labels);
    setChecked([]);
    setCurrent(0);
    setOnlyProblems(false);
    setStep("check");
    setBusy({ kind: "checking", done: 0, total: labels.length });

    const results: CheckedRow[] = [];

    try {
      for (let start = 0; start < labels.length; start += CHECK_CHUNK_SIZE) {
        const chunk = labels.slice(start, start + CHECK_CHUNK_SIZE);
        const answers = await checkRows(
          templateId,
          chunk.map((row) => row.values),
          abort.signal,
        );

        answers.forEach((answer, index) => {
          const errors = chunk[index].quantityError
            ? [...answer.errors, t("printing.bulk.check.quantityInvalid")]
            : answer.errors;
          results.push({ errors, warnings: answer.warnings });
        });

        setChecked([...results]);
        setBusy({ kind: "checking", done: results.length, total: labels.length });
      }

      setTicked(results.map((result) => result.errors.length === 0));
      setBusy(null);
    } catch (err) {
      setBusy(null);
      if (abort.signal.aborted) {
        setStep("match");
      } else {
        setError(err instanceof Error ? err.message : t("printing.bulk.check.errorFallback"));
      }
    }
  }

  // The label viewer: each label is rendered when it is looked at, and its neighbours right
  // after, so stepping through feels instant. Images are kept for this check.
  const previews = useRef(new Map<number, string>());
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);

  useEffect(() => {
    const cache = previews.current;
    return () => {
      cache.forEach((url) => URL.revokeObjectURL(url));
      cache.clear();
    };
  }, [rows]);

  useEffect(() => {
    if (step !== "check" || rows.length === 0) return;

    let cancelled = false;
    const cache = previews.current;

    async function load(index: number): Promise<string | null> {
      if (index < 0 || index >= rows.length) return null;
      const known = cache.get(index);
      if (known) return known;

      const blob = await previewTemplate(templateId, rows[index].values, "png");
      // The rows changed while this was rendering: the cache belongs to the new ones.
      if (cancelled && cache !== previews.current) return null;
      const url = URL.createObjectURL(blob);
      cache.set(index, url);
      return url;
    }

    setPreviewUrl(cache.get(current) ?? null);

    load(current)
      .then((url) => {
        if (!cancelled) setPreviewUrl(url);
        return Promise.all([load(current + 1), load(current - 1)]);
      })
      .catch(() => {
        // A row that can't be rendered is reported by the check; the viewer just stays empty.
      });

    return () => {
      cancelled = true;
    };
  }, [step, rows, current, templateId]);

  function go(index: number) {
    setCurrent(Math.min(rows.length - 1, Math.max(0, index)));
  }

  // Arrow keys step through the labels, unless the user is typing or choosing somewhere.
  useEffect(() => {
    if (step !== "check") return;

    function onKey(event: KeyboardEvent) {
      const tag = (event.target as HTMLElement | null)?.tagName;
      if (tag === "INPUT" || tag === "SELECT" || tag === "TEXTAREA") return;
      if (event.key === "ArrowRight") setCurrent((index) => Math.min(rows.length - 1, index + 1));
      if (event.key === "ArrowLeft") setCurrent((index) => Math.max(0, index - 1));
    }

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [step, rows.length]);

  // --- Step 4: printing ---

  const printRows = rows.filter((_, index) => ticked[index]);
  const labelCount = printRows.reduce((sum, row) => sum + row.quantity * copies, 0);

  async function print() {
    setSubmitting(true);
    setError(null);

    try {
      const job = await createPrintJob({
        templateId,
        ...options.target,
        items: printRows.map((row) => ({ fieldValues: row.values, quantity: row.quantity * copies })),
      });
      navigate(`/print-jobs/${job.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("printing.printTemplate.submitErrorFallback"));
      setSubmitting(false);
    }
  }

  if (loadError) {
    return <p role="alert">{loadError}</p>;
  }

  if (!template) {
    return <p>{t("printing.printTemplate.loading")}</p>;
  }

  const sourceName = source?.kind === "file" ? source.file.name : (source?.url ?? "");
  const fieldName = (name: string) => fields.find((field) => field.name === name)?.label ?? name;
  const width = data?.rows[0]?.length ?? 0;
  const header = data && hasHeader ? data.rows[0] : null;
  const columnName = (index: number) =>
    header?.[index]
      ? t("printing.bulk.match.columnWithHeader", { header: header[index], letter: columnLetter(index) })
      : t("printing.bulk.match.column", { letter: columnLetter(index) });
  const missing = missingFields(fields, mapping);
  const askSeparator = data?.kind === "text" && !data.separatorDetected && !separatorChosen;
  const problemCount = checked.filter((row) => row.errors.length > 0 || row.warnings.length > 0).length;
  const errorCount = checked.filter((row) => row.errors.length > 0).length;
  const checking = busy?.kind === "checking";
  const tapeLengthMm = labelCount * template.currentVersion.widthMm;
  // The profiles card lists the profiles of the import tab that is open.
  const tabProfiles = profiles.filter((profile) => (profile.settings.url ? "api" : "file") === importTab);
  const shownPage = Math.min(page, Math.max(0, Math.ceil(listed.length / PAGE_SIZE) - 1));

  const saveProfileBlock = data && source && (
    <div className="bulk-save-profile">
      <label className="properties-field">
        {t("printing.bulk.profiles.saveHeading")}
        <input
          value={profileName}
          placeholder={t("printing.bulk.profiles.name")}
          maxLength={100}
          onChange={(event) => setProfileName(event.target.value)}
        />
      </label>
      <button
        type="button"
        className="btn"
        disabled={savingProfile || profileName.trim() === ""}
        onClick={() => void saveProfile()}
      >
        {savingProfile ? t("printing.bulk.profiles.saving") : t("printing.bulk.profiles.save")}
      </button>
      <span className="print-form__hint">{profileNotice ?? t("printing.bulk.profiles.saveHint")}</span>
    </div>
  );

  return (
    <section
      className="print-page bulk-print page-enter"
      onDragOver={handleDragOver}
      onDragLeave={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragging(false);
      }}
      onDrop={handleDrop}
    >
      <h2>{t("printing.bulk.title", { name: template.name })}</h2>
      <PrintModeSwitch templateId={templateId} mode="bulk" bulkAvailable />

      <ol className="bulk-steps" aria-label={t("printing.bulk.stepsLabel")}>
        {STEPS.map((name, index) => (
          <li
            key={name}
            className={`bulk-steps__step${name === step ? " bulk-steps__step--current" : ""}${
              STEPS.indexOf(step) > index ? " bulk-steps__step--done" : ""
            }`}
            aria-current={name === step ? "step" : undefined}
          >
            <span className="bulk-steps__number">{index + 1}</span>
            {t(`printing.bulk.steps.${name}`)}
          </li>
        ))}
      </ol>

      {error && (
        <p className="print-form__warning" role="alert">
          {error}
        </p>
      )}

      {step === "file" && (
        <div className="bulk-start">
          <div className="card bulk-card">
            <h3>{t("printing.bulk.file.heading")}</h3>
            <p className="bulk-card__intro">
              {t("printing.bulk.file.intro", { fields: fields.map((field) => field.label ?? field.name).join(", ") })}
            </p>

            {busy ? (
              <ProgressBar
                fraction={busy.kind === "upload" ? busy.fraction : null}
                status={
                  busy.kind === "upload"
                    ? t("printing.bulk.file.uploading", { percent: Math.round(busy.fraction * 100) })
                    : t("printing.bulk.file.reading")
                }
              />
            ) : (
              <>
                <div className="bulk-tabs" role="tablist" aria-label={t("printing.bulk.tabs.label")}>
                  {IMPORT_TABS.map((tab) => (
                    <button
                      key={tab}
                      type="button"
                      role="tab"
                      id={`bulk-tab-${tab}`}
                      aria-selected={importTab === tab}
                      aria-controls="bulk-tab-panel"
                      className={`bulk-tabs__tab${importTab === tab ? " bulk-tabs__tab--selected" : ""}`}
                      onClick={() => setImportTab(tab)}
                    >
                      {t(`printing.bulk.tabs.${tab}`)}
                    </button>
                  ))}
                </div>

                <div id="bulk-tab-panel" role="tabpanel" aria-labelledby={`bulk-tab-${importTab}`} className="bulk-tab-panel">
                  {importTab === "file" && (
                    <>
                      {pendingProfile && (
                        <div className="bulk-question bulk-question--open" role="status">
                          {t("printing.bulk.profiles.chooseFile", {
                            name: pendingProfile.name,
                            file: pendingProfile.settings.fileName ?? "?",
                          })}
                          <div>
                            <button type="button" className="btn btn-sm" onClick={() => setPendingProfile(null)}>
                              {t("printing.bulk.profiles.cancel")}
                            </button>
                          </div>
                        </div>
                      )}

                      <label className={`bulk-drop${dragging ? " bulk-drop--over" : ""}`}>
                        <strong>{dragging ? t("printing.bulk.file.dropNow") : t("printing.bulk.file.drop")}</strong>
                        <span className="btn btn-primary">{t("printing.bulk.file.choose")}</span>
                        <span className="print-form__hint">{t("printing.bulk.file.formats")}</span>
                        <input
                          type="file"
                          accept=".xlsx,.xls,.csv,.txt,.tsv,.json"
                          data-testid="bulk-file-input"
                          onClick={(event) => {
                            // Where the browser can keep the file for a profile, its own dialog is used.
                            if (!canKeepFiles()) return;
                            event.preventDefault();
                            void pickDataFile().then((picked) => picked && fileChosen(picked.file, picked.kept));
                          }}
                          onChange={(event) => {
                            const chosen = event.target.files?.[0];
                            event.target.value = "";
                            if (chosen) fileChosen(chosen, null);
                          }}
                        />
                      </label>
                    </>
                  )}

                  {importTab === "api" && (
                    <form
                      className="bulk-url"
                      onSubmit={(event) => {
                        event.preventDefault();
                        loadUrl();
                      }}
                    >
                      <p className="print-form__hint">{t("printing.bulk.url.hint")}</p>
                      <div className="bulk-url__row">
                        <input
                          type="url"
                          aria-label={t("printing.bulk.url.address")}
                          placeholder="https://server/api/items"
                          value={dataUrl.url}
                          onChange={(event) => setDataUrl((previous) => ({ ...previous, url: event.target.value }))}
                        />
                        <button type="submit" className="btn" disabled={dataUrl.url.trim() === ""}>
                          {t("printing.bulk.url.load")}
                        </button>
                      </div>
                      <details open={dataUrl.headerName !== "" ? true : undefined}>
                        <summary>{t("printing.bulk.url.headerToggle")}</summary>
                        <div className="bulk-url__row">
                          <input
                            aria-label={t("printing.bulk.url.headerName")}
                            placeholder={t("printing.bulk.url.headerName")}
                            value={dataUrl.headerName}
                            onChange={(event) => setDataUrl((previous) => ({ ...previous, headerName: event.target.value }))}
                          />
                          <input
                            aria-label={t("printing.bulk.url.headerValue")}
                            placeholder={t("printing.bulk.url.headerValue")}
                            value={dataUrl.headerValue}
                            onChange={(event) => setDataUrl((previous) => ({ ...previous, headerValue: event.target.value }))}
                          />
                        </div>
                      </details>
                    </form>
                  )}
                </div>
              </>
            )}
          </div>

          <div className="card bulk-card bulk-profiles">
            <h3>{t("printing.bulk.profiles.headingFor", { tab: t(`printing.bulk.tabs.${importTab}`) })}</h3>
            <p className="bulk-card__intro">{t("printing.bulk.profiles.intro")}</p>
            {tabProfiles.length === 0 ? (
              <p className="print-form__hint">{t("printing.bulk.profiles.empty")}</p>
            ) : (
              <ul>
                {tabProfiles.map((profile) => (
                  <li key={profile.id}>
                    <button
                      type="button"
                      className="bulk-profile"
                      title={t("printing.bulk.profiles.load", { name: profile.name })}
                      onClick={() => void loadProfile(profile)}
                    >
                      <strong>{profile.name}</strong>
                      <span>{profile.settings.url ?? profile.settings.filePath ?? profile.settings.fileName ?? ""}</span>
                    </button>
                    <button
                      type="button"
                      className="icon-link icon-link--danger"
                      aria-label={t("printing.bulk.profiles.delete", { name: profile.name })}
                      title={t("printing.bulk.profiles.delete", { name: profile.name })}
                      onClick={() => void removeProfile(profile)}
                    >
                      <TrashIcon />
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}

      {step === "match" && data && source && (
        <div className="card bulk-card">
          <h3>{t("printing.bulk.match.heading")}</h3>
          <p className="bulk-card__intro">
            {t("printing.bulk.match.file", { name: sourceName, count: data.rows.length - (hasHeader ? 1 : 0) })}
          </p>

          {busy && <ProgressBar fraction={null} status={t("printing.bulk.file.reading")} />}

          {data.kind === "text" && (
            <fieldset className={`bulk-question${askSeparator ? " bulk-question--open" : ""}`}>
              <legend>
                {askSeparator ? t("printing.bulk.match.separatorQuestion") : t("printing.bulk.match.separatorLabel")}
              </legend>
              {askSeparator && <p className="print-form__hint">{t("printing.bulk.match.separatorHelp")}</p>}
              <div className="bulk-choices">
                {SEPARATORS.map((separator) => (
                  <label key={separator} className="bulk-choice">
                    <input
                      type="radio"
                      name="bulk-separator"
                      checked={!askSeparator && data.separator === separator}
                      disabled={busy !== null}
                      onChange={() => void readSource(source, { separator })}
                    />
                    {t(`printing.bulk.separator.${SEPARATOR_KEYS[separator]}`)}
                  </label>
                ))}
                <label className="bulk-choice">
                  {t("printing.bulk.separator.other")}
                  <input
                    className="bulk-choice__other"
                    maxLength={1}
                    value={otherSeparator}
                    aria-label={t("printing.bulk.separator.other")}
                    disabled={busy !== null}
                    onChange={(event) => {
                      setOtherSeparator(event.target.value);
                      if (event.target.value.length === 1) void readSource(source, { separator: event.target.value });
                    }}
                  />
                </label>
              </div>
            </fieldset>
          )}

          {data.sheets.length > 1 && (
            <label className="properties-field bulk-field">
              {t("printing.bulk.match.sheet")}
              <select
                value={data.sheet}
                disabled={busy !== null}
                onChange={(event) => void readSource(source, { sheet: Number(event.target.value) })}
              >
                {data.sheets.map((name, index) => (
                  <option key={index} value={index}>
                    {name}
                  </option>
                ))}
              </select>
            </label>
          )}

          <div className="table-scroll bulk-sample">
            <table>
              <caption>{t("printing.bulk.match.sampleHeading")}</caption>
              <thead>
                <tr>
                  {Array.from({ length: width }, (_, index) => (
                    <th key={index}>{columnName(index)}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data.rows.slice(hasHeader ? 1 : 0, (hasHeader ? 1 : 0) + SAMPLE_ROWS).map((row, index) => (
                  <tr key={index}>
                    {row.map((cell, column) => (
                      <td key={column}>{cell}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {!askSeparator && (
            <>
              <fieldset className={`bulk-question${!hasHeader || missing.length > 0 ? " bulk-question--open" : ""}`}>
                <legend>
                  {hasHeader ? t("printing.bulk.match.headerFound") : t("printing.bulk.match.columnQuestion")}
                </legend>

                <label className="bulk-choice">
                  <input
                    type="checkbox"
                    checked={hasHeader}
                    disabled={data.rows.length < 2 || data.kind === "json"}
                    onChange={(event) => setHasHeader(event.target.checked)}
                  />
                  {t("printing.bulk.match.headerSwitch")}
                </label>

                <div className="bulk-mapping">
                  {fields.map((field) => (
                    <label key={field.name} className="properties-field">
                      <span>
                        {field.label ?? field.name}
                        {fieldNeedsColumn(field) ? " *" : ""}
                      </span>
                      <select
                        value={mapping[field.name] ?? ""}
                        aria-invalid={fieldNeedsColumn(field) && mapping[field.name] == null}
                        onChange={(event) =>
                          setMapping((previous) => ({
                            ...previous,
                            [field.name]: event.target.value === "" ? null : Number(event.target.value),
                          }))
                        }
                      >
                        <option value="">
                          {fieldNeedsColumn(field)
                            ? t("printing.bulk.match.chooseColumn")
                            : field.defaultValue
                              ? t("printing.bulk.match.useDefault", { value: field.defaultValue })
                              : t("printing.bulk.match.leaveEmpty")}
                        </option>
                        {Array.from({ length: width }, (_, index) => (
                          <option key={index} value={index}>
                            {columnName(index)}
                          </option>
                        ))}
                      </select>
                    </label>
                  ))}

                  <label className="properties-field">
                    <span>{t("printing.bulk.match.quantity")}</span>
                    <select
                      value={quantityColumn ?? ""}
                      onChange={(event) => setQuantityColumn(event.target.value === "" ? null : Number(event.target.value))}
                    >
                      <option value="">{t("printing.bulk.match.quantityNone")}</option>
                      {Array.from({ length: width }, (_, index) => (
                        <option key={index} value={index}>
                          {columnName(index)}
                        </option>
                      ))}
                    </select>
                  </label>
                </div>

                {missing.length > 0 && (
                  <p className="print-form__hint">
                    {t("printing.bulk.match.missing", { fields: missing.map((field) => fieldName(field.name)).join(", ") })}
                  </p>
                )}
              </fieldset>
            </>
          )}

          <div className="print-form__actions">
            <button type="button" className="btn" onClick={() => setStep("file")}>
              {t("common.back")}
            </button>
            <button
              type="button"
              className="btn btn-primary"
              disabled={busy !== null || askSeparator || missing.length > 0 || data.rows.length - (hasHeader ? 1 : 0) < 1}
              onClick={() => void check(data, hasHeader, mapping, quantityColumn)}
            >
              {t("printing.bulk.match.continue")}
            </button>
          </div>
        </div>
      )}

      {step === "check" && (
        <>
          {checking && busy.kind === "checking" && (
            <div className="card bulk-card">
              <h3>{t("printing.bulk.check.readingRows", { count: busy.total })}</h3>
              <ProgressBar
                fraction={busy.total === 0 ? 1 : busy.done / busy.total}
                status={t("printing.bulk.check.checking", { done: busy.done, total: busy.total })}
              />
              <div className="print-form__actions">
                <button type="button" className="btn" onClick={() => checkAbort.current?.abort()}>
                  {t("common.cancel")}
                </button>
              </div>
            </div>
          )}

          {rows.length > 0 && (
            <div className="bulk-check">
              <div className="bulk-viewer">
                <div className="print-preview" aria-busy={previewUrl === null}>
                  {previewUrl ? (
                    <img src={previewUrl} alt={t("printing.bulk.check.labelOf", { current: current + 1, total: rows.length })} />
                  ) : (
                    <p>{t("printing.printTemplate.rendering")}</p>
                  )}
                </div>
                <div className="bulk-viewer__nav">
                  <button type="button" className="btn" disabled={current === 0} onClick={() => go(current - 1)}>
                    <span aria-hidden="true">←</span> {t("printing.bulk.check.previous")}
                  </button>
                  <label className="bulk-viewer__position">
                    {t("printing.bulk.check.label")}
                    <input
                      type="number"
                      min={1}
                      max={rows.length}
                      value={current + 1}
                      onChange={(event) => go(Number(event.target.value) - 1)}
                    />
                    {t("printing.bulk.check.ofTotal", { total: rows.length })}
                  </label>
                  <button type="button" className="btn" disabled={current >= rows.length - 1} onClick={() => go(current + 1)}>
                    {t("printing.bulk.check.next")} <span aria-hidden="true">→</span>
                  </button>
                </div>
                <RowProblems row={checked[current]} />
              </div>

              <div className="card bulk-rows">
                <div className="bulk-rows__toolbar">
                  <label className="bulk-choice">
                    <input
                      type="checkbox"
                      checked={onlyProblems}
                      disabled={problemCount === 0}
                      onChange={(event) => setOnlyProblems(event.target.checked)}
                    />
                    {t("printing.bulk.check.onlyProblems", { count: problemCount })}
                  </label>
                </div>
                <div className="table-scroll bulk-rows__table">
                  <table>
                    <thead>
                      <tr>
                        <th>{t("printing.bulk.check.columnPrint")}</th>
                        <th>{t("printing.bulk.check.columnRow")}</th>
                        <th>{t("printing.bulk.check.columnProblem")}</th>
                        {fields.map((field) => (
                          <th key={field.name}>{field.label ?? field.name}</th>
                        ))}
                        <th>{t("printing.bulk.check.columnCopies")}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {listed.slice(shownPage * PAGE_SIZE, (shownPage + 1) * PAGE_SIZE).map((index) => {
                        const row = rows[index];
                        const result = checked[index];
                        const problems = result ? [...result.errors, ...result.warnings] : [];

                        return (
                          <tr
                            key={index}
                            className={`${index === current ? "bulk-row--current " : ""}${
                              result?.errors.length ? "bulk-row--error" : result?.warnings.length ? "bulk-row--warning" : ""
                            }`}
                            aria-selected={index === current}
                            onClick={() => go(index)}
                          >
                            <td>
                              <input
                                type="checkbox"
                                aria-label={t("printing.bulk.check.printRow", { row: index + 1 })}
                                checked={ticked[index] ?? false}
                                disabled={!result || result.errors.length > 0 || checking}
                                onClick={(event) => event.stopPropagation()}
                                onChange={(event) =>
                                  setTicked((previous) => previous.map((value, i) => (i === index ? event.target.checked : value)))
                                }
                              />
                            </td>
                            <td>{index + 1}</td>
                            <td className="bulk-rows__check">
                              {!result ? "…" : problems.length === 0 ? t("printing.bulk.check.ok") : problems.join(" ")}
                            </td>
                            {fields.map((field) => (
                              <td key={field.name}>{row.values[field.name] ?? field.defaultValue ?? ""}</td>
                            ))}
                            <td>{row.quantity * copies}</td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
                <Pager page={shownPage} pageSize={PAGE_SIZE} total={listed.length} onPage={setPage} infoKey="printing.bulk.check.pageInfo" />
              </div>
            </div>
          )}

          {!checking && checked.length === rows.length && (
            <div className="card bulk-card bulk-summary">
              <p className="bulk-summary__main" role="status">
                {labelCount > 0
                  ? t("printing.bulk.check.summary", { count: labelCount })
                  : t("printing.bulk.check.nothingToPrint")}
                {labelCount > 0 &&
                  ` · ${t("printing.bulk.check.tape", {
                    length: formatLength(tapeLengthMm),
                    width: options.neededTapeMm ?? template.currentVersion.heightMm,
                  })}`}
              </p>
              {errorCount > 0 && <p className="print-form__hint">{t("printing.bulk.check.summaryErrors", { count: errorCount })}</p>}
              <label className="properties-field bulk-copies">
                {t("printing.bulk.check.copies")}
                <input
                  type="number"
                  min={1}
                  max={MAX_COPIES}
                  value={copies}
                  onChange={(event) => setCopies(clampCopies(Number(event.target.value)))}
                />
                {quantityColumn !== null && <span className="print-form__hint">{t("printing.bulk.check.copiesHint")}</span>}
              </label>
              {saveProfileBlock}
              <div className="print-form__actions">
                <button type="button" className="btn" onClick={() => setStep(data ? "match" : "file")}>
                  {t("common.back")}
                </button>
                <button type="button" className="btn btn-primary" disabled={labelCount === 0} onClick={() => setStep("print")}>
                  {t("printing.bulk.check.continue")}
                </button>
              </div>
            </div>
          )}
        </>
      )}

      {step === "print" && (
        <div className="card bulk-card print-form__fields bulk-print__options">
          <h3>{t("printing.bulk.print.heading", { count: labelCount })}</h3>
          <p className="bulk-card__intro">{t("printing.bulk.print.note")}</p>

          <PrintOptionsFields options={options} />

          {saveProfileBlock}

          <div className="print-form__actions">
            <button type="button" className="btn" disabled={submitting} onClick={() => setStep("check")}>
              {t("common.back")}
            </button>
            <button type="button" className="btn btn-primary btn-print" disabled={submitting} onClick={() => void print()}>
              <PrinterIcon />
              {submitting ? t("printing.printTemplate.submitting") : t("printing.bulk.print.submit", { count: labelCount })}
            </button>
          </div>
        </div>
      )}
    </section>
  );
}

function RowProblems({ row }: { row: CheckedRow | undefined }) {
  if (!row || (row.errors.length === 0 && row.warnings.length === 0)) return null;

  return (
    <ul className="bulk-problems">
      {row.errors.map((message, index) => (
        <li key={`e${index}`} className="bulk-problems__error">
          {message}
        </li>
      ))}
      {row.warnings.map((message, index) => (
        <li key={`w${index}`} className="bulk-problems__warning">
          {message}
        </li>
      ))}
    </ul>
  );
}

/** Tape length for people: millimetres up to a metre, then metres. */
function formatLength(mm: number): string {
  return mm < 1000
    ? `${Math.round(mm)} mm`
    : `${(mm / 1000).toLocaleString(undefined, { maximumFractionDigits: 1 })} m`;
}
