const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

export type PrinterConnectionType = "IpAddress" | "Hostname" | "PrintServer" | "Usb";

export type PrintQuality = "Standard" | "High";

/** A print resolution the printer's model supports; horizontal is along the tape. */
export interface PrintResolution {
  quality: PrintQuality;
  horizontalDpi: number;
  verticalDpi: number;
}

/** Cutting options, as the server names them (see api/printJobs CutMode). */
export type PrinterCutMode = "AutoCut" | "HalfCut" | "CutAtEnd" | "ChainPrinting" | "CutMarks";

/** A Brother model Tapeory knows. `network` models print directly; others through a CUPS queue. */
export interface PrinterModelResponse {
  name: string;
  family: string;
  network: boolean;
  dpi: number;
  highResolution: boolean;
  twoColor: boolean;
  cutModes: PrinterCutMode[];
}

export interface PrinterResponse {
  id: number;
  name: string;
  model: string | null;
  connectionType: PrinterConnectionType;
  address: string | null;
  port: number;
  printServerAddress: string | null;
  usbIdentifier: string | null;
  /** For a print server: the printer's queue on it (CUPS/IPP); null sends to its raw port. */
  queueName: string | null;
  labelMediaWidthMm: number | null;
  labelMediaHeightMm: number | null;
  isDefault: boolean;
  enabled: boolean;
  lastConnectionStatus: "Unknown" | "Success" | "Failed";
  lastConnectionCheckedAt: string | null;
  lastErrorMessage: string | null;
  resolutions: PrintResolution[];
  /** The cutting options this printer's model offers. */
  cutModes?: PrinterCutMode[];
  /** A USB printer's computer; null for network printers. */
  computerName?: string | null;
  /** False for a USB printer connected to another computer: only Tapeory there can print to it. */
  onThisComputer?: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface PrinterRequest {
  name: string;
  model?: string | null;
  connectionType: PrinterConnectionType;
  address?: string | null;
  port?: number | null;
  printServerAddress?: string | null;
  usbIdentifier?: string | null;
  queueName?: string | null;
  labelMediaWidthMm?: number | null;
  labelMediaHeightMm?: number | null;
  enabled: boolean;
}

export interface TestConnectionResponse {
  isSuccess: boolean;
  errorMessage: string | null;
  /** The tape the printer reports loaded, in mm (directly connected printers with SNMP). */
  loadedTapeMm?: number | null;
}

/** What a directly connected printer reports right now; `available` is false when it can't be asked. */
export interface PrinterStatusResponse {
  available: boolean;
  loadedTapeMm: number | null;
  display: string | null;
  problem: string | null;
}

/** Brother TZe tape widths, in mm. */
export const TAPE_WIDTHS_MM = [3.5, 6, 9, 12, 18, 24];

/** The tape a label of this height prints on: the closest width, as the server picks it. */
export function tapeForLabelHeight(heightMm: number): number {
  return TAPE_WIDTHS_MM.reduce((best, width) => (Math.abs(width - heightMm) < Math.abs(best - heightMm) ? width : best));
}

export interface TestPrintResponse {
  isSuccess: boolean;
  errorMessage: string | null;
}

async function parseJsonOrThrow<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const detail =
      problem?.detail ??
      problem?.title ??
      (problem?.errors ? Object.values(problem.errors).flat().join(" ") : null) ??
      `Request failed with status ${response.status}`;
    throw new Error(detail);
  }

  return (await response.json()) as T;
}

export async function listPrinters(): Promise<PrinterResponse[]> {
  const response = await fetch(`${API_BASE_URL}/printers`);
  return parseJsonOrThrow(response);
}

export async function getPrinter(id: number): Promise<PrinterResponse> {
  const response = await fetch(`${API_BASE_URL}/printers/${id}`);
  return parseJsonOrThrow(response);
}

export async function createPrinter(request: PrinterRequest): Promise<PrinterResponse> {
  const response = await fetch(`${API_BASE_URL}/printers`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return parseJsonOrThrow(response);
}

export async function updatePrinter(id: number, request: PrinterRequest): Promise<PrinterResponse> {
  const response = await fetch(`${API_BASE_URL}/printers/${id}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return parseJsonOrThrow(response);
}

export async function deletePrinter(id: number): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/printers/${id}`, { method: "DELETE" });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail ?? `Request failed with status ${response.status}`);
  }
}

export async function setDefaultPrinter(id: number): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/printers/${id}/default`, { method: "PUT" });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail ?? `Request failed with status ${response.status}`);
  }
}

export async function testPrinterConnection(id: number): Promise<TestConnectionResponse> {
  const response = await fetch(`${API_BASE_URL}/printers/${id}/test-connection`, { method: "POST" });
  return parseJsonOrThrow(response);
}

/** A printer connected to the server's computer by USB (on Windows: installed in Windows). */
export interface UsbPrinterInfo {
  identifier: string;
  name: string;
  model: string | null;
}

export async function listUsbPrinters(): Promise<UsbPrinterInfo[]> {
  const response = await fetch(`${API_BASE_URL}/printers/usb`);
  return parseJsonOrThrow(response);
}

export async function listPrinterModels(): Promise<PrinterModelResponse[]> {
  const response = await fetch(`${API_BASE_URL}/printers/models`);
  return parseJsonOrThrow(response);
}

export async function getPrinterStatus(id: number): Promise<PrinterStatusResponse> {
  const response = await fetch(`${API_BASE_URL}/printers/${id}/status`);
  return parseJsonOrThrow(response);
}

export async function testPrinterPrint(id: number): Promise<TestPrintResponse> {
  const response = await fetch(`${API_BASE_URL}/printers/${id}/test-print`, { method: "POST" });
  return parseJsonOrThrow(response);
}

/** The margins of a label (in mm) that the printer can't print on, and what they are for. */
export interface PrintArea {
  topMm: number;
  rightMm: number;
  bottomMm: number;
  leftMm: number;
  /** Blank tape the printer adds before and after the label; outside the label. */
  feedMarginMm: number;
  mediaId: string;
}

/** Where a label of this size can't be printed: the limits of its tape or roll, whichever printer prints it. */
export async function getPrintArea(widthMm: number, heightMm: number, media?: string | null): Promise<PrintArea> {
  const query = new URLSearchParams({ widthMm: String(widthMm), heightMm: String(heightMm) });
  if (media) query.set("media", media);
  const response = await fetch(`${API_BASE_URL}/printers/print-area?${query}`);
  return parseJsonOrThrow(response);
}
