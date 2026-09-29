import { useEffect, useRef, useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { getTemplate, type TemplateDetailResponse } from "../api/templates";
import { createPrintJob, CUT_MODES, previewTemplate, type CutMode } from "../api/printJobs";
import {
  getPrinterStatus,
  listPrinters,
  tapeForLabelHeight,
  type PrinterResponse,
  type PrintQuality,
} from "../api/printers";
import "./printing.css";

const MANUAL_PRINTER_OPTION = "manual";

/** How long typing must pause before the preview re-renders. */
const PREVIEW_DELAY_MS = 300;

export function PrintTemplatePage() {
  const { t } = useTranslation();
  const params = useParams<{ id: string }>();
  const templateId = Number(params.id);
  const navigate = useNavigate();

  const [template, setTemplate] = useState<TemplateDetailResponse | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [fieldValues, setFieldValues] = useState<Record<string, string>>({});
  const [quantity, setQuantity] = useState(1);

  const [printers, setPrinters] = useState<PrinterResponse[]>([]);
  const [printerSelection, setPrinterSelection] = useState<string>(MANUAL_PRINTER_OPTION);
  const [manualPrinterName, setManualPrinterName] = useState("");
  const [quality, setQuality] = useState<PrintQuality>("Standard");
  const [cutMode, setCutMode] = useState<CutMode>("AutoCut");

  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);

  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    getTemplate(templateId)
      .then((detail) => {
        if (cancelled) return;
        setTemplate(detail);
        const defaults: Record<string, string> = {};
        for (const field of detail.currentVersion.fields) {
          defaults[field.name] = field.defaultValue ?? "";
        }
        setFieldValues(defaults);
      })
      .catch((err: unknown) => {
        if (!cancelled) setLoadError(err instanceof Error ? err.message : t("printing.printTemplate.loadErrorFallback"));
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [templateId]);

  useEffect(() => {
    let cancelled = false;

    listPrinters()
      .then((results) => {
        if (cancelled) return;
        setPrinters(results);
        // Another computer's USB printer can't be printed to from here.
        const defaultPrinter = results.find((p) => p.isDefault && p.onThisComputer !== false);
        if (defaultPrinter) {
          setPrinterSelection(String(defaultPrinter.id));
        }
      })
      .catch(() => {
        // Printer list is a convenience here; the manual name field still works if this fails.
      });

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    return () => {
      if (previewUrl) {
        URL.revokeObjectURL(previewUrl);
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Live preview: re-render on the server whenever the field values change, once typing pauses.
  // The request counter drops responses that arrive after a newer request was started.
  const previewRequest = useRef(0);

  useEffect(() => {
    if (!template) return;

    const request = ++previewRequest.current;
    const timer = setTimeout(async () => {
      setPreviewLoading(true);

      try {
        const blob = await previewTemplate(templateId, fieldValues, "png");
        if (request !== previewRequest.current) return;
        setPreviewError(null);
        setPreviewUrl((old) => {
          if (old) URL.revokeObjectURL(old);
          return URL.createObjectURL(blob);
        });
      } catch (err) {
        if (request !== previewRequest.current) return;
        setPreviewError(err instanceof Error ? err.message : t("printing.printTemplate.previewErrorFallback"));
      } finally {
        if (request === previewRequest.current) setPreviewLoading(false);
      }
    }, PREVIEW_DELAY_MS);

    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [template, templateId, fieldValues]);

  // The resolutions the selected printer's model supports; a choice the newly selected printer
  // can't do falls back to Standard.
  const selectedPrinter = printers.find((printer) => String(printer.id) === printerSelection);

  // Ask the selected printer which tape is loaded, to warn before a label goes onto the wrong one.
  const [loadedTape, setLoadedTape] = useState<{ printerId: number; mm: number | null } | null>(null);

  useEffect(() => {
    if (!selectedPrinter) return;

    let cancelled = false;
    const printerId = selectedPrinter.id;

    getPrinterStatus(printerId)
      .then((status) => {
        if (!cancelled) setLoadedTape({ printerId, mm: status.loadedTapeMm });
      })
      .catch(() => {
        // The warning is a convenience; without the status, printing still checks the tape.
      });

    return () => {
      cancelled = true;
    };
  }, [selectedPrinter]);

  const neededTapeMm = template ? tapeForLabelHeight(template.currentVersion.heightMm) : null;
  const loadedTapeMm = loadedTape?.printerId === selectedPrinter?.id ? loadedTape?.mm ?? null : null;
  const tapeMismatch = loadedTapeMm !== null && neededTapeMm !== null && loadedTapeMm !== neededTapeMm;
  const resolutions = selectedPrinter?.resolutions ?? [];
  const effectiveQuality = resolutions.some((resolution) => resolution.quality === quality) ? quality : "Standard";
  // The printer's model decides the cutting options (no half cut on QL printers, only cut marks
  // without a cutter); a choice it doesn't offer falls back to its first.
  const cutModes: CutMode[] = selectedPrinter?.cutModes?.length ? selectedPrinter.cutModes : CUT_MODES;
  const effectiveCutMode = cutModes.includes(cutMode) ? cutMode : cutModes[0];

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setSubmitError(null);

    try {
      const printerId = printerSelection === MANUAL_PRINTER_OPTION ? null : Number(printerSelection);
      const job = await createPrintJob({
        templateId,
        printerId,
        printerName: printerId === null ? manualPrinterName.trim() || null : null,
        items: [{ fieldValues, quantity }],
        quality: printerId === null ? undefined : effectiveQuality,
        cutMode: printerId === null ? undefined : effectiveCutMode,
      });
      navigate(`/print-jobs/${job.id}`);
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : t("printing.printTemplate.submitErrorFallback"));
    } finally {
      setSubmitting(false);
    }
  }

  if (loadError) {
    return <p role="alert">{loadError}</p>;
  }

  if (!template) {
    return <p>{t("printing.printTemplate.loading")}</p>;
  }

  return (
    <section className="print-page page-enter">
      <h2>{t("printing.printTemplate.title", { name: template.name })}</h2>

      <form className="print-form" onSubmit={handleSubmit}>
        <div className="card print-form__fields">
          {template.currentVersion.fields.length === 0 && <p>{t("printing.printTemplate.noFields")}</p>}

          {template.currentVersion.fields.map((field) => (
            <label key={field.name} className="properties-field">
              {field.label ?? field.name}
              {field.required ? " *" : ""}
              <input
                value={fieldValues[field.name] ?? ""}
                onChange={(e) => setFieldValues((prev) => ({ ...prev, [field.name]: e.target.value }))}
              />
            </label>
          ))}

          <label className="properties-field">
            {t("printing.printTemplate.quantity")}
            <input
              type="number"
              min={1}
              value={quantity}
              onChange={(e) => setQuantity(Math.max(1, Number(e.target.value)))}
            />
          </label>

          <label className="properties-field">
            {t("printing.printTemplate.printer")}
            <select value={printerSelection} onChange={(e) => setPrinterSelection(e.target.value)}>
              {printers.map((printer) => (
                <option key={printer.id} value={printer.id} disabled={printer.onThisComputer === false}>
                  {printer.name}
                  {printer.onThisComputer === false
                    ? t("printing.printTemplate.optionElsewhereSuffix", { name: printer.computerName ?? "?" })
                    : ""}
                  {printer.isDefault ? t("printing.printTemplate.optionDefaultSuffix") : ""}
                  {!printer.enabled ? t("printing.printTemplate.optionDisabledSuffix") : ""}
                </option>
              ))}
              <option value={MANUAL_PRINTER_OPTION}>{t("printing.printTemplate.manualOption")}</option>
            </select>
          </label>

          {resolutions.length > 0 && (
            <label className="properties-field">
              {t("printing.quality.label")}
              <select
                value={effectiveQuality}
                onChange={(e) => setQuality(e.target.value as PrintQuality)}
                disabled={resolutions.length === 1}
              >
                {resolutions.map((resolution) => (
                  <option key={resolution.quality} value={resolution.quality}>
                    {t("printing.quality.option", {
                      name: t(`printing.quality.${resolution.quality.toLowerCase()}`),
                      horizontal: resolution.horizontalDpi,
                      vertical: resolution.verticalDpi,
                    })}
                  </option>
                ))}
              </select>
            </label>
          )}

          {tapeMismatch && (
            <p className="print-form__warning" role="alert">
              {t("printing.printTemplate.tapeMismatch", { loaded: loadedTapeMm, needed: neededTapeMm })}
            </p>
          )}

          {selectedPrinter && (
            <div className="print-form__field">
              <label className="properties-field">
                {t("printing.cutMode.label")}
                <select
                  value={effectiveCutMode}
                  onChange={(e) => setCutMode(e.target.value as CutMode)}
                  aria-describedby="print-cut-mode-hint"
                >
                  {cutModes.map((mode) => (
                    <option key={mode} value={mode}>
                      {t(`printing.cutMode.${mode}`)}
                    </option>
                  ))}
                </select>
              </label>
              <span id="print-cut-mode-hint" className="print-form__hint">
                {t(`printing.cutMode.${effectiveCutMode}Hint`)}
              </span>
            </div>
          )}

          {printerSelection === MANUAL_PRINTER_OPTION && (
            <label className="properties-field">
              {t("printing.printTemplate.printerNameOptional")}
              <input value={manualPrinterName} onChange={(e) => setManualPrinterName(e.target.value)} />
            </label>
          )}

          <div className="print-form__actions">
            <button type="submit" className="btn btn-primary" disabled={submitting}>
              {submitting ? t("printing.printTemplate.submitting") : t("printing.printTemplate.submit")}
            </button>
          </div>

          {previewError && <p role="alert">{previewError}</p>}
          {submitError && <p role="alert">{submitError}</p>}
        </div>

        <div className="print-preview" aria-busy={previewLoading}>
          {previewUrl ? (
            <img src={previewUrl} alt={t("printing.printTemplate.previewImageAlt")} />
          ) : (
            !previewError && <p>{t("printing.printTemplate.rendering")}</p>
          )}
          {previewUrl && previewLoading && (
            <span className="print-preview__status">{t("printing.printTemplate.rendering")}</span>
          )}
        </div>
      </form>
    </section>
  );
}
