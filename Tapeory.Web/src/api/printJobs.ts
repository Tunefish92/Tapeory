const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

export interface PrintJobItemRequest {
  fieldValues: Record<string, string>;
  quantity: number;
}

export interface CreatePrintJobRequest {
  templateId: number;
  printerId: number | null;
  printerName: string | null;
  items: PrintJobItemRequest[];
}

export interface PrintJobItemResponse {
  id: number;
  fieldValues: Record<string, string>;
  quantity: number;
  status: string;
  errorMessage: string | null;
  previewUrl: string | null;
}

export interface PrintJobResponse {
  id: number;
  templateId: number;
  templateName: string;
  /** The template has since been deleted: show its name, but it can't be opened or printed. */
  templateDeleted?: boolean;
  templateVersionNumber: number;
  printerId: number | null;
  printerName: string | null;
  status: string;
  errorMessage: string | null;
  createdAt: string;
  completedAt: string | null;
  items: PrintJobItemResponse[];
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

export async function createPrintJob(request: CreatePrintJobRequest): Promise<PrintJobResponse> {
  const response = await fetch(`${API_BASE_URL}/print-jobs`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return parseJsonOrThrow(response);
}

export async function getPrintJob(id: number): Promise<PrintJobResponse> {
  const response = await fetch(`${API_BASE_URL}/print-jobs/${id}`);
  return parseJsonOrThrow(response);
}

export async function listPrintJobs(templateId?: number): Promise<PrintJobResponse[]> {
  const suffix = templateId ? `?templateId=${templateId}` : "";
  const response = await fetch(`${API_BASE_URL}/print-jobs${suffix}`);
  return parseJsonOrThrow(response);
}

/** Removes a job from the print history; it keeps counting in the statistics. */
export async function deletePrintJob(id: number): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/print-jobs/${id}`, { method: "DELETE" });

  if (!response.ok) {
    await parseJsonOrThrow(response);
  }
}

export interface DeleteAllPrintJobsResult {
  deleted: number;
  /** Jobs still queued or printing, which are left in the history. */
  skippedInProgress: number;
}

export async function deleteAllPrintJobs(): Promise<DeleteAllPrintJobsResult> {
  const response = await fetch(`${API_BASE_URL}/print-jobs`, { method: "DELETE" });
  return parseJsonOrThrow(response);
}

export async function previewTemplate(
  id: number,
  fieldValues: Record<string, string>,
  format: "png" | "pdf" = "png",
): Promise<Blob> {
  const response = await fetch(`${API_BASE_URL}/templates/${id}/preview`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ fieldValues, format }),
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail ?? `Request failed with status ${response.status}`);
  }

  return response.blob();
}
