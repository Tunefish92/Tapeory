const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

export type PrinterConnectionType = "IpAddress" | "Hostname" | "PrintServer" | "Usb";

export interface PrinterResponse {
  id: number;
  name: string;
  model: string | null;
  connectionType: PrinterConnectionType;
  address: string | null;
  port: number;
  printServerAddress: string | null;
  usbIdentifier: string | null;
  labelMediaWidthMm: number | null;
  labelMediaHeightMm: number | null;
  isDefault: boolean;
  enabled: boolean;
  lastConnectionStatus: "Unknown" | "Success" | "Failed";
  lastConnectionCheckedAt: string | null;
  lastErrorMessage: string | null;
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
  labelMediaWidthMm?: number | null;
  labelMediaHeightMm?: number | null;
  enabled: boolean;
}

export interface TestConnectionResponse {
  isSuccess: boolean;
  errorMessage: string | null;
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

export async function testPrinterPrint(id: number): Promise<TestPrintResponse> {
  const response = await fetch(`${API_BASE_URL}/printers/${id}/test-print`, { method: "POST" });
  return parseJsonOrThrow(response);
}
