const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

export interface TemplateFieldDto {
  name: string;
  label: string | null;
  defaultValue: string | null;
  required: boolean;
}

export interface TemplateVersionResponse {
  versionNumber: number;
  widthMm: number;
  heightMm: number;
  editorJson: string;
  fields: TemplateFieldDto[];
  previewImageUrl: string | null;
  createdAt: string;
}

export interface TemplateSummaryResponse {
  id: number;
  name: string;
  description: string | null;
  category: string | null;
  tags: string[];
  status: string;
  currentVersionNumber: number;
  widthMm: number;
  heightMm: number;
  previewImageUrl: string | null;
  sourceLbxUrl: string | null;
  createdAt: string;
  updatedAt: string;
  /** Visible to every account; otherwise only its owner and administrators see it. */
  isPublic?: boolean;
  /** Display name of the owner; null for a shared template from before accounts existed. */
  ownerName?: string | null;
  /** Whether the signed-in account may change it (its owner, or an administrator). */
  canEdit?: boolean;
  /** Owned by the signed-in account. */
  isMine?: boolean;
}

export interface TemplateDetailResponse {
  id: number;
  name: string;
  description: string | null;
  category: string | null;
  tags: string[];
  status: string;
  sourceLbxUrl: string | null;
  conversionWarnings: string[];
  createdAt: string;
  updatedAt: string;
  currentVersion: TemplateVersionResponse;
  /** Visible to every account; otherwise only its owner and administrators see it. */
  isPublic?: boolean;
  /** Display name of the owner; null for a shared template from before accounts existed. */
  ownerName?: string | null;
  /** Whether the signed-in account may change it (its owner, or an administrator). */
  canEdit?: boolean;
  /** Owned by the signed-in account. */
  isMine?: boolean;
}

export interface CreateTemplateRequest {
  name: string;
  description?: string | null;
  category?: string | null;
  tags?: string[] | null;
  widthMm: number;
  heightMm: number;
  editorJson: string;
  fields?: TemplateFieldDto[] | null;
}

export interface CreateTemplateVersionRequest {
  widthMm: number;
  heightMm: number;
  editorJson: string;
  fields?: TemplateFieldDto[] | null;
}

export interface UpdateTemplateMetadataRequest {
  name: string;
  description?: string | null;
  category?: string | null;
  tags?: string[] | null;
  status?: string | null;
}

async function parseJsonOrThrow<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const detail = problem?.detail ?? problem?.title ?? `Request failed with status ${response.status}`;
    throw new Error(detail);
  }

  return (await response.json()) as T;
}

/** The version number busts the browser cache whenever the template is re-saved. */
export function templateThumbnailUrl(id: number, versionNumber: number): string {
  return `${API_BASE_URL}/templates/${id}/thumbnail?v=${versionNumber}`;
}

export async function listTemplates(params?: {
  search?: string;
  category?: string;
  status?: string;
}): Promise<TemplateSummaryResponse[]> {
  const query = new URLSearchParams();
  if (params?.search) query.set("search", params.search);
  if (params?.category) query.set("category", params.category);
  if (params?.status) query.set("status", params.status);

  const suffix = query.toString() ? `?${query.toString()}` : "";
  const response = await fetch(`${API_BASE_URL}/templates${suffix}`);
  return parseJsonOrThrow(response);
}

export async function getTemplate(id: number): Promise<TemplateDetailResponse> {
  const response = await fetch(`${API_BASE_URL}/templates/${id}`);
  return parseJsonOrThrow(response);
}

export async function createTemplate(
  request: CreateTemplateRequest,
): Promise<TemplateDetailResponse> {
  const response = await fetch(`${API_BASE_URL}/templates`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return parseJsonOrThrow(response);
}

export async function updateTemplateMetadata(
  id: number,
  request: UpdateTemplateMetadataRequest,
): Promise<TemplateDetailResponse> {
  const response = await fetch(`${API_BASE_URL}/templates/${id}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return parseJsonOrThrow(response);
}

export async function createTemplateVersion(
  id: number,
  request: CreateTemplateVersionRequest,
): Promise<TemplateVersionResponse> {
  const response = await fetch(`${API_BASE_URL}/templates/${id}/versions`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return parseJsonOrThrow(response);
}

/** Removes the template. Print jobs that used it stay in the print history. */
/** Copies the template's current version into a new draft called `name`. */
export async function duplicateTemplate(id: number, name: string): Promise<TemplateSummaryResponse> {
  const response = await fetch(`${API_BASE_URL}/templates/${id}/duplicate`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name }),
  });

  return parseJsonOrThrow<TemplateSummaryResponse>(response);
}

/** Makes a template public (every account sees it) or private (only its owner and administrators). */
export async function setTemplateVisibility(id: number, isPublic: boolean): Promise<TemplateDetailResponse> {
  const response = await fetch(`${API_BASE_URL}/templates/${id}/visibility`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ isPublic }),
  });
  return parseJsonOrThrow(response);
}

export async function deleteTemplate(id: number): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/templates/${id}`, { method: "DELETE" });

  if (!response.ok) {
    await parseJsonOrThrow(response);
  }
}

/** Moves every template in `from` to `to`; an empty `to` ungroups them. */
export async function renameTemplateGroup(from: string, to: string): Promise<{ updatedCount: number }> {
  const response = await fetch(`${API_BASE_URL}/templates/groups/rename`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ from, to }),
  });
  return parseJsonOrThrow(response);
}

export async function setTemplatePreviewImage(id: number, uploadedFileId: number): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/templates/${id}/preview-image`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ uploadedFileId }),
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail ?? `Request failed with status ${response.status}`);
  }
}

export interface UploadedImageResponse {
  id: number;
  fileName: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  url: string;
  createdAt: string;
}

export async function uploadImage(file: File): Promise<UploadedImageResponse> {
  const formData = new FormData();
  formData.append("file", file);

  const response = await fetch(`${API_BASE_URL}/uploads/images`, {
    method: "POST",
    body: formData,
  });

  return parseJsonOrThrow(response);
}

/** Where a template downloads from as a ".tapeory" file: the whole template, images included. */
export function templateFileUrl(id: number): string {
  return `${API_BASE_URL}/templates/${id}/export`;
}

/** Creates a template from a ".tapeory" file (or an older ".tapeory.json"). */
export async function importTemplateFile(file: File): Promise<TemplateDetailResponse> {
  const response = await fetch(`${API_BASE_URL}/templates/import`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: await file.text(),
  });

  return parseJsonOrThrow(response);
}

export async function importLbxTemplate(file: File): Promise<TemplateDetailResponse> {
  const formData = new FormData();
  formData.append("file", file);

  const response = await fetch(`${API_BASE_URL}/templates/import-lbx`, {
    method: "POST",
    body: formData,
  });

  return parseJsonOrThrow(response);
}
