import { useEffect, useRef, useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { getTemplate, type TemplateDetailResponse } from "../api/templates";
import { createPrintJob, previewTemplate } from "../api/printJobs";
import { PrinterIcon } from "../components/icons";
import { PrintModeSwitch } from "./PrintModeSwitch";
import { PrintOptionsFields, usePrintOptions } from "./PrintOptions";
import "./printing.css";

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

  const options = usePrintOptions(template ? template.currentVersion.heightMm : null);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setSubmitError(null);

    try {
      const job = await createPrintJob({ templateId, ...options.target, items: [{ fieldValues, quantity }] });
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
      <PrintModeSwitch templateId={templateId} mode="single" bulkAvailable={template.currentVersion.fields.length > 0} />

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

          <PrintOptionsFields options={options} />

          <div className="print-form__actions">
            <button type="submit" className="btn btn-primary btn-print" disabled={submitting}>
              <PrinterIcon />
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
