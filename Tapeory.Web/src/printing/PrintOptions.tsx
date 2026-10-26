import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { CUT_MODES, type CutMode } from "../api/printJobs";
import {
  getPrinterStatus,
  listPrinters,
  tapeForLabelHeight,
  type PrinterResponse,
  type PrintQuality,
} from "../api/printers";

const MANUAL_PRINTER_OPTION = "manual";

/** The printer part of a print job request. */
export interface PrintTarget {
  printerId: number | null;
  printerName: string | null;
  quality?: string;
  cutMode?: CutMode;
}

/**
 * Where and how to print: the printer (or none, for a label to print by hand), the quality and
 * the cutting option, with a warning when the printer holds a different tape than the label
 * needs. Shared by the single print and the bulk print.
 */
export function usePrintOptions(labelHeightMm: number | null) {
  const [printers, setPrinters] = useState<PrinterResponse[]>([]);
  const [printerSelection, setPrinterSelection] = useState<string>(MANUAL_PRINTER_OPTION);
  const [manualPrinterName, setManualPrinterName] = useState("");
  const [quality, setQuality] = useState<PrintQuality>("Standard");
  const [cutMode, setCutMode] = useState<CutMode>("AutoCut");

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

  // Settings to take over (from a saved profile), once the printers are known.
  const [wanted, setWanted] = useState<PrintTarget | null>(null);

  useEffect(() => {
    if (!wanted) return;

    if (wanted.printerId === null) {
      setPrinterSelection(MANUAL_PRINTER_OPTION);
      setManualPrinterName(wanted.printerName ?? "");
    } else if (printers.some((printer) => printer.id === wanted.printerId && printer.onThisComputer !== false)) {
      setPrinterSelection(String(wanted.printerId));
    } else {
      // The printer isn't known (yet, or any more): the current choice stays.
      return;
    }

    if (wanted.quality) setQuality(wanted.quality as PrintQuality);
    if (wanted.cutMode) setCutMode(wanted.cutMode);
    setWanted(null);
  }, [wanted, printers]);

  const neededTapeMm = labelHeightMm === null ? null : tapeForLabelHeight(labelHeightMm);
  const loadedTapeMm = loadedTape?.printerId === selectedPrinter?.id ? loadedTape?.mm ?? null : null;
  const tapeMismatch = loadedTapeMm !== null && neededTapeMm !== null && loadedTapeMm !== neededTapeMm;
  const resolutions = selectedPrinter?.resolutions ?? [];
  const effectiveQuality = resolutions.some((resolution) => resolution.quality === quality) ? quality : "Standard";
  // The printer's model decides the cutting options (no half cut on QL printers, only cut marks
  // without a cutter); a choice it doesn't offer falls back to its first.
  const cutModes: CutMode[] = selectedPrinter?.cutModes?.length ? selectedPrinter.cutModes : CUT_MODES;
  const effectiveCutMode = cutModes.includes(cutMode) ? cutMode : cutModes[0];

  const printerId = printerSelection === MANUAL_PRINTER_OPTION ? null : Number(printerSelection);
  const target: PrintTarget = {
    printerId,
    printerName: printerId === null ? manualPrinterName.trim() || null : null,
    quality: printerId === null ? undefined : effectiveQuality,
    cutMode: printerId === null ? undefined : effectiveCutMode,
  };

  return {
    target,
    /** Takes over a printer choice, e.g. from a saved profile. */
    apply: setWanted,
    printers,
    printerSelection,
    setPrinterSelection,
    manualPrinterName,
    setManualPrinterName,
    selectedPrinter,
    resolutions,
    effectiveQuality,
    setQuality,
    cutModes,
    effectiveCutMode,
    setCutMode,
    tapeMismatch,
    loadedTapeMm,
    neededTapeMm,
  };
}

export function PrintOptionsFields({ options }: { options: ReturnType<typeof usePrintOptions> }) {
  const { t } = useTranslation();
  const {
    printers,
    printerSelection,
    setPrinterSelection,
    manualPrinterName,
    setManualPrinterName,
    selectedPrinter,
    resolutions,
    effectiveQuality,
    setQuality,
    cutModes,
    effectiveCutMode,
    setCutMode,
    tapeMismatch,
    loadedTapeMm,
    neededTapeMm,
  } = options;

  return (
    <>
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
    </>
  );
}
