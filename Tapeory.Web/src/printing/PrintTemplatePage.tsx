import { useEffect, useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { getTemplate, type TemplateDetailResponse } from "../api/templates";
import { createPrintJob, previewTemplate } from "../api/printJobs";
import { listPrinters, type PrinterResponse } from "../api/printers";
import "./printing.css";

const MANUAL_PRINTER_OPTION = "manual";

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
        const defaultPrinter = results.find((p) => p.isDefault);
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

  async function handleRefreshPreview() {
    setPreviewLoading(true);
    setPreviewError(null);

    try {
      const blob = await previewTemplate(templateId, fieldValues, "png");
      setPreviewUrl((old) => {
        if (old) URL.revokeObjectURL(old);
        return URL.createObjectURL(blob);
      });
    } catch (err) {
      setPreviewError(err instanceof Error ? err.message : t("printing.printTemplate.previewErrorFallback"));
    } finally {
      setPreviewLoading(false);
    }
  }

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
                <option key={printer.id} value={printer.id}>
                  {printer.name}
                  {printer.isDefault ? t("printing.printTemplate.optionDefaultSuffix") : ""}
                  {!printer.enabled ? t("printing.printTemplate.optionDisabledSuffix") : ""}
                </option>
              ))}
              <option value={MANUAL_PRINTER_OPTION}>{t("printing.printTemplate.manualOption")}</option>
            </select>
          </label>

          {printerSelection === MANUAL_PRINTER_OPTION && (
            <label className="properties-field">
              {t("printing.printTemplate.printerNameOptional")}
              <input value={manualPrinterName} onChange={(e) => setManualPrinterName(e.target.value)} />
            </label>
          )}

          <div className="print-form__actions">
            <button type="button" onClick={handleRefreshPreview} disabled={previewLoading}>
              {previewLoading ? t("printing.printTemplate.rendering") : t("printing.printTemplate.updatePreview")}
            </button>
            <button type="submit" className="btn btn-primary" disabled={submitting}>
              {submitting ? t("printing.printTemplate.submitting") : t("printing.printTemplate.submit")}
            </button>
          </div>

          {previewError && <p role="alert">{previewError}</p>}
          {submitError && <p role="alert">{submitError}</p>}
        </div>

        <div className="print-preview">
          {previewUrl ? (
            <img src={previewUrl} alt={t("printing.printTemplate.previewImageAlt")} />
          ) : (
            <p>{t("printing.printTemplate.previewPlaceholder")}</p>
          )}
        </div>
      </form>
    </section>
  );
}
