import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import {
  createPrinter,
  deletePrinter,
  listPrinters,
  setDefaultPrinter,
  testPrinterConnection,
  testPrinterPrint,
  updatePrinter,
  type PrinterConnectionType,
  type PrinterRequest,
  type PrinterResponse,
} from "../api/printers";
import { useNotifications } from "../notifications/NotificationsContext";
import "./printers.css";

const EMPTY_FORM: PrinterRequest = {
  name: "",
  model: "",
  connectionType: "IpAddress",
  address: "",
  port: 9100,
  printServerAddress: "",
  usbIdentifier: "",
  labelMediaWidthMm: null,
  labelMediaHeightMm: null,
  enabled: true,
};

function toFormState(printer: PrinterResponse): PrinterRequest {
  return {
    name: printer.name,
    model: printer.model ?? "",
    connectionType: printer.connectionType,
    address: printer.address ?? "",
    port: printer.port,
    printServerAddress: printer.printServerAddress ?? "",
    usbIdentifier: printer.usbIdentifier ?? "",
    labelMediaWidthMm: printer.labelMediaWidthMm,
    labelMediaHeightMm: printer.labelMediaHeightMm,
    enabled: printer.enabled,
  };
}

function buildRequestPayload(form: PrinterRequest): PrinterRequest {
  return {
    ...form,
    model: form.model?.trim() || null,
    address: form.address?.trim() || null,
    printServerAddress: form.printServerAddress?.trim() || null,
    usbIdentifier: form.usbIdentifier?.trim() || null,
  };
}

export function PrintersPage() {
  const { t } = useTranslation();
  const { notify } = useNotifications();

  const CONNECTION_TYPE_LABELS: Record<PrinterConnectionType, string> = {
    IpAddress: t("printers.connectionTypeIpAddress"),
    Hostname: t("printers.connectionTypeHostname"),
    PrintServer: t("printers.connectionTypePrintServer"),
    Usb: t("printers.connectionTypeUsb"),
  };

  const STATUS_LABELS: Record<PrinterResponse["lastConnectionStatus"], string> = {
    Unknown: t("printers.statusUnknown"),
    Success: t("printers.statusSuccess"),
    Failed: t("printers.statusFailed"),
  };

  const [printers, setPrinters] = useState<PrinterResponse[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [formOpen, setFormOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<PrinterRequest>(EMPTY_FORM);
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const [busyId, setBusyId] = useState<number | null>(null);
  const [rowResults, setRowResults] = useState<Record<number, { success: boolean; message: string }>>({});

  async function refresh() {
    try {
      const results = await listPrinters();
      setPrinters(results);
      setLoadError(null);
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : t("printers.loadErrorFallback"));
    }
  }

  useEffect(() => {
    void refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function openCreateForm() {
    setEditingId(null);
    setForm(EMPTY_FORM);
    setFormError(null);
    setFormOpen(true);
  }

  function openEditForm(printer: PrinterResponse) {
    setEditingId(printer.id);
    setForm(toFormState(printer));
    setFormError(null);
    setFormOpen(true);
  }

  function closeForm() {
    setFormOpen(false);
    setEditingId(null);
    setFormError(null);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setFormError(null);

    try {
      const payload = buildRequestPayload(form);

      if (editingId === null) {
        await createPrinter(payload);
      } else {
        await updatePrinter(editingId, payload);
      }

      closeForm();
      await refresh();
      notify(editingId === null ? t("printers.printerAdded") : t("printers.printerUpdated"), "success");
    } catch (err) {
      setFormError(err instanceof Error ? err.message : t("printers.saveErrorFallback"));
    } finally {
      setSubmitting(false);
    }
  }

  async function handleDelete(printer: PrinterResponse) {
    if (!confirm(t("printers.confirmDelete", { name: printer.name }))) {
      return;
    }

    setBusyId(printer.id);

    try {
      await deletePrinter(printer.id);
      await refresh();
      notify(t("printers.printerDeleted"), "success");
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : t("printers.deleteErrorFallback"));
    } finally {
      setBusyId(null);
    }
  }

  async function handleSetDefault(printer: PrinterResponse) {
    setBusyId(printer.id);

    try {
      await setDefaultPrinter(printer.id);
      await refresh();
      notify(t("printers.defaultPrinterUpdated", { name: printer.name }), "success");
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : t("printers.setDefaultErrorFallback"));
    } finally {
      setBusyId(null);
    }
  }

  async function handleTestConnection(printer: PrinterResponse) {
    setBusyId(printer.id);
    setRowResults((prev) => ({ ...prev, [printer.id]: { success: false, message: t("printers.testingConnection") } }));

    try {
      const result = await testPrinterConnection(printer.id);
      setRowResults((prev) => ({
        ...prev,
        [printer.id]: {
          success: result.isSuccess,
          message: result.isSuccess
            ? t("printers.connectionSucceeded")
            : result.errorMessage ?? t("printers.connectionFailedFallback"),
        },
      }));
      await refresh();
    } catch (err) {
      setRowResults((prev) => ({
        ...prev,
        [printer.id]: { success: false, message: err instanceof Error ? err.message : t("printers.connectionTestErrorFallback") },
      }));
    } finally {
      setBusyId(null);
    }
  }

  async function handleTestPrint(printer: PrinterResponse) {
    setBusyId(printer.id);
    setRowResults((prev) => ({ ...prev, [printer.id]: { success: false, message: t("printers.sendingTestPrint") } }));

    try {
      const result = await testPrinterPrint(printer.id);
      setRowResults((prev) => ({
        ...prev,
        [printer.id]: {
          success: result.isSuccess,
          message: result.isSuccess
            ? t("printers.testPrintSucceeded")
            : result.errorMessage ?? t("printers.testPrintFailedFallback"),
        },
      }));
      await refresh();
    } catch (err) {
      setRowResults((prev) => ({
        ...prev,
        [printer.id]: { success: false, message: err instanceof Error ? err.message : t("printers.testPrintErrorFallback") },
      }));
    } finally {
      setBusyId(null);
    }
  }

  const isIpOrHostname = form.connectionType === "IpAddress" || form.connectionType === "Hostname";
  const isPrintServer = form.connectionType === "PrintServer";
  const isUsb = form.connectionType === "Usb";

  return (
    <section className="page-enter">
      <div className="page-header">
        <h2>{t("printers.title")}</h2>
        <div className="page-header__actions">
          {!formOpen && (
            <button type="button" className="btn btn-primary" onClick={openCreateForm}>
              {t("printers.addPrinter")}
            </button>
          )}
        </div>
      </div>

      {formOpen && (
        <form className="card printer-form" onSubmit={handleSubmit}>
          <h3>{editingId === null ? t("printers.addPrinter") : t("printers.editPrinter")}</h3>

          <label className="properties-field">
            {t("printers.name")}
            <input
              value={form.name}
              onChange={(e) => setForm((prev) => ({ ...prev, name: e.target.value }))}
              required
            />
          </label>

          <label className="properties-field">
            {t("printers.model")}
            <input
              value={form.model ?? ""}
              onChange={(e) => setForm((prev) => ({ ...prev, model: e.target.value }))}
              placeholder={t("printers.modelPlaceholder")}
            />
          </label>

          <label className="properties-field">
            {t("printers.connectionType")}
            <select
              value={form.connectionType}
              onChange={(e) =>
                setForm((prev) => ({ ...prev, connectionType: e.target.value as PrinterConnectionType }))
              }
            >
              {(Object.keys(CONNECTION_TYPE_LABELS) as PrinterConnectionType[]).map((type) => (
                <option key={type} value={type}>
                  {CONNECTION_TYPE_LABELS[type]}
                </option>
              ))}
            </select>
          </label>

          {isIpOrHostname && (
            <label className="properties-field">
              {form.connectionType === "IpAddress"
                ? t("printers.connectionTypeIpAddress")
                : t("printers.connectionTypeHostname")}
              <input
                value={form.address ?? ""}
                onChange={(e) => setForm((prev) => ({ ...prev, address: e.target.value }))}
                placeholder={form.connectionType === "IpAddress" ? "192.168.1.50" : "printer.local"}
              />
            </label>
          )}

          {isPrintServer && (
            <label className="properties-field">
              {t("printers.printServerAddress")}
              <input
                value={form.printServerAddress ?? ""}
                onChange={(e) => setForm((prev) => ({ ...prev, printServerAddress: e.target.value }))}
              />
            </label>
          )}

          {isUsb && (
            <label className="properties-field">
              {t("printers.usbIdentifier")}
              <input
                value={form.usbIdentifier ?? ""}
                onChange={(e) => setForm((prev) => ({ ...prev, usbIdentifier: e.target.value }))}
                placeholder={t("printers.usbIdentifierPlaceholder")}
              />
            </label>
          )}

          {!isUsb && (
            <label className="properties-field">
              {t("printers.port")}
              <input
                type="number"
                min={1}
                max={65535}
                value={form.port ?? 9100}
                onChange={(e) => setForm((prev) => ({ ...prev, port: Number(e.target.value) }))}
              />
            </label>
          )}

          <div className="printer-form__row">
            <label className="properties-field">
              {t("printers.labelWidth")}
              <input
                type="number"
                min={0}
                step="0.1"
                value={form.labelMediaWidthMm ?? ""}
                onChange={(e) =>
                  setForm((prev) => ({
                    ...prev,
                    labelMediaWidthMm: e.target.value === "" ? null : Number(e.target.value),
                  }))
                }
              />
            </label>
            <label className="properties-field">
              {t("printers.labelHeight")}
              <input
                type="number"
                min={0}
                step="0.1"
                value={form.labelMediaHeightMm ?? ""}
                onChange={(e) =>
                  setForm((prev) => ({
                    ...prev,
                    labelMediaHeightMm: e.target.value === "" ? null : Number(e.target.value),
                  }))
                }
              />
            </label>
          </div>

          <label className="properties-field properties-field--inline">
            <input
              type="checkbox"
              checked={form.enabled}
              onChange={(e) => setForm((prev) => ({ ...prev, enabled: e.target.checked }))}
            />
            {t("printers.enabled")}
          </label>

          {formError && <p role="alert">{formError}</p>}

          <div className="printer-form__actions">
            <button type="submit" className="btn btn-primary" disabled={submitting}>
              {submitting ? t("common.saving") : editingId === null ? t("printers.addPrinter") : t("printers.saveChanges")}
            </button>
            <button type="button" onClick={closeForm} disabled={submitting}>
              {t("common.cancel")}
            </button>
          </div>
        </form>
      )}

      {loadError && <p role="alert">{loadError}</p>}
      {!loadError && !printers && <p>{t("printers.loading")}</p>}
      {printers && printers.length === 0 && <p>{t("printers.empty")}</p>}

      {printers && printers.length > 0 && (
        <div className="printer-list">
          {printers.map((printer) => {
            const result = rowResults[printer.id];
            const rowBusy = busyId === printer.id;

            return (
              <div
                key={printer.id}
                className={`printer-card${printer.enabled ? "" : " printer-card--disabled"}`}
              >
                <div className="printer-card__header">
                  <span className="printer-card__icon" aria-hidden="true">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M6 9V3h12v6" />
                      <rect x="3" y="9" width="18" height="8" rx="2" />
                      <path d="M7 14h10v7H7z" />
                    </svg>
                  </span>
                  <div className="printer-card__info">
                    <span className="printer-card__name">
                      {printer.name}
                      {printer.isDefault && <span className="badge badge--default">{t("printers.default")}</span>}
                      {!printer.enabled && <span className="badge badge--disabled">{t("printers.disabled")}</span>}
                      <span className={`badge badge--${printer.lastConnectionStatus.toLowerCase()}`}>
                        {STATUS_LABELS[printer.lastConnectionStatus]}
                      </span>
                    </span>
                    <span className="printer-card__meta">
                      {printer.model ?? t("printers.unknownModel")} — {CONNECTION_TYPE_LABELS[printer.connectionType]}
                      {printer.connectionType === "PrintServer"
                        ? `: ${printer.printServerAddress ?? "—"}`
                        : printer.connectionType === "Usb"
                          ? printer.usbIdentifier
                            ? `: ${printer.usbIdentifier}`
                            : ""
                          : `: ${printer.address ?? "—"}:${printer.port}`}
                    </span>
                    {printer.lastErrorMessage && (
                      <span className="printer-card__meta">
                        {t("printers.lastError", { message: printer.lastErrorMessage })}
                      </span>
                    )}
                  </div>
                </div>

                <div className="printer-card__actions">
                  <button type="button" onClick={() => openEditForm(printer)} disabled={rowBusy}>
                    {t("common.edit")}
                  </button>
                  <button
                    type="button"
                    onClick={() => handleTestConnection(printer)}
                    disabled={rowBusy}
                  >
                    {t("printers.testConnection")}
                  </button>
                  <button type="button" onClick={() => handleTestPrint(printer)} disabled={rowBusy}>
                    {t("printers.testPrint")}
                  </button>
                  {!printer.isDefault && (
                    <button type="button" onClick={() => handleSetDefault(printer)} disabled={rowBusy}>
                      {t("printers.setDefault")}
                    </button>
                  )}
                  <button
                    type="button"
                    className="btn btn-danger"
                    onClick={() => handleDelete(printer)}
                    disabled={rowBusy}
                  >
                    {t("common.delete")}
                  </button>
                </div>

                {result && (
                  <p className="printer-card__result" role={result.success ? "status" : "alert"}>
                    {result.message}
                  </p>
                )}
              </div>
            );
          })}
        </div>
      )}
    </section>
  );
}
