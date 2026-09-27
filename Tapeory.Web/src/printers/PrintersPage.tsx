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
  queueName: "",
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
    queueName: printer.queueName ?? "",
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
    queueName: form.queueName?.trim() || null,
  };
}

/** Where the printer is reached: address and port, print server, or USB identifier. */
function connectionTarget(printer: PrinterResponse): string {
  switch (printer.connectionType) {
    case "PrintServer":
      return printer.queueName
        ? `${printer.printServerAddress ?? "—"}:${printer.port}/printers/${printer.queueName}`
        : `${printer.printServerAddress ?? "—"}:${printer.port}`;
    case "Usb":
      return printer.usbIdentifier ?? "—";
    default:
      return `${printer.address ?? "—"}:${printer.port}`;
  }
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
            ? result.loadedTapeMm
              ? t("printers.connectionSucceededWithTape", { tape: result.loadedTapeMm })
              : t("printers.connectionSucceeded")
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
  const usesQueue = isPrintServer && Boolean(form.queueName?.trim());

  // A queue is reached over IPP (port 631) rather than the raw port (9100); switch the port
  // along with it unless the user has set a custom one.
  function handleQueueNameChange(queueName: string) {
    setForm((prev) => {
      const hadQueue = Boolean(prev.queueName?.trim());
      const hasQueue = Boolean(queueName.trim());
      let port = prev.port;

      if (!hadQueue && hasQueue && port === 9100) port = 631;
      if (hadQueue && !hasQueue && port === 631) port = 9100;

      return { ...prev, queueName, port };
    });
  }

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

          <div className="printer-form__sections">
            <fieldset className="printer-form__section">
              <legend>{t("printers.sectionPrinter")}</legend>
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

              <label className="properties-field properties-field--inline">
                <input
                  type="checkbox"
                  checked={form.enabled}
                  onChange={(e) => setForm((prev) => ({ ...prev, enabled: e.target.checked }))}
                />
                {t("printers.enabled")}
              </label>
            </fieldset>

            <fieldset className="printer-form__section">
              <legend>{t("printers.sectionConnection")}</legend>
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

              {isPrintServer && (
                <p className="printer-form__note" role="note">
                  {t("printers.printServerNote")}
                </p>
              )}

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

              {isPrintServer && (
                <div className="printer-form__field">
                  <label className="properties-field">
                    {t("printers.queueName")}
                    <input
                      value={form.queueName ?? ""}
                      onChange={(e) => handleQueueNameChange(e.target.value)}
                      placeholder="Brother_PT-P750W"
                      aria-describedby="printer-queue-hint"
                    />
                  </label>
                  <span id="printer-queue-hint" className="printer-form__hint">
                    {t("printers.queueNameHint")}
                  </span>
                </div>
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
                <div className="printer-form__field">
                  <label className="properties-field">
                    {t("printers.port")}
                    <input
                      type="number"
                      min={1}
                      max={65535}
                      value={form.port ?? 9100}
                      onChange={(e) => setForm((prev) => ({ ...prev, port: Number(e.target.value) }))}
                      aria-describedby="printer-port-hint"
                    />
                  </label>
                  <span id="printer-port-hint" className="printer-form__hint">
                    {usesQueue ? t("printers.portHintIpp") : t("printers.portHint")}
                  </span>
                </div>
              )}
            </fieldset>

            <fieldset className="printer-form__section">
              <legend>{t("printers.testPrintSize")}</legend>
              <span className="printer-form__hint">{t("printers.testPrintSizeHint")}</span>
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
            </fieldset>
          </div>

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
                    <span className="printer-card__name" title={printer.name}>
                      {printer.name}
                    </span>
                    <span className="printer-card__badges">
                      {printer.isDefault && <span className="badge badge--default">{t("printers.default")}</span>}
                      {!printer.enabled && <span className="badge badge--disabled">{t("printers.disabled")}</span>}
                      <span className={`badge badge--${printer.lastConnectionStatus.toLowerCase()}`}>
                        {STATUS_LABELS[printer.lastConnectionStatus]}
                      </span>
                    </span>
                  </div>
                </div>

                <dl className="printer-card__details">
                  <div>
                    <dt>{t("printers.model")}</dt>
                    <dd title={printer.model ?? undefined}>{printer.model ?? t("printers.unknownModel")}</dd>
                  </div>
                  <div>
                    <dt>{CONNECTION_TYPE_LABELS[printer.connectionType]}</dt>
                    <dd title={connectionTarget(printer)}>{connectionTarget(printer)}</dd>
                  </div>
                </dl>

                {printer.lastErrorMessage && (
                  <p className="printer-card__error">
                    {t("printers.lastError", { message: printer.lastErrorMessage })}
                  </p>
                )}

                {result && (
                  <p className="printer-card__result" role={result.success ? "status" : "alert"}>
                    {result.message}
                  </p>
                )}

                <div className="printer-card__actions">
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
                  <div className="printer-card__secondary-actions">
                    <button type="button" onClick={() => openEditForm(printer)} disabled={rowBusy}>
                      {t("common.edit")}
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
                </div>
              </div>
            );
          })}
        </div>
      )}
    </section>
  );
}
