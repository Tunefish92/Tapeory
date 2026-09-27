const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

export type UserRole = "Admin" | "User";

export interface CurrentUser {
  id: number;
  userName: string;
  displayName: string;
  role: UserRole;
  /** Signed in with a temporary password: has to choose a new one first. */
  mustChangePassword: boolean;
}

export interface AuthState {
  /** False until the first account is created; until then Tapeory is open to everyone. */
  hasUsers: boolean;
  user: CurrentUser | null;
}

export interface UserResponse {
  id: number;
  userName: string;
  displayName: string;
  role: UserRole;
  disabled: boolean;
  mustChangePassword: boolean;
  createdAt: string;
  lastLoginAt: string | null;
}

/** A new account, or a reset password: the temporary password is only shown this once. */
export interface TemporaryPasswordResponse {
  user: UserResponse;
  temporaryPassword: string;
}

/** The problem's first field error, detail or title. */
async function problemMessage(response: Response): Promise<string | null> {
  const problem = await response.json().catch(() => null);
  const fieldErrors: string[] = problem?.errors ? Object.values<string[]>(problem.errors).flat() : [];
  return fieldErrors[0] ?? problem?.detail ?? problem?.title ?? null;
}

async function send<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method,
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    throw new Error((await problemMessage(response)) ?? `Request failed with status ${response.status}`);
  }

  return (response.status === 204 ? undefined : await response.json()) as T;
}

export function getAuthState(): Promise<AuthState> {
  return send("GET", "/auth/state");
}

export function createFirstUser(request: { userName: string; displayName: string; password: string }): Promise<AuthState> {
  return send("POST", "/auth/first-user", request);
}

export function login(request: { userName: string; password: string; rememberMe: boolean }): Promise<AuthState> {
  return send("POST", "/auth/login", request);
}

export function logout(): Promise<void> {
  return send("POST", "/auth/logout");
}

export function changePassword(request: { currentPassword: string; newPassword: string }): Promise<AuthState> {
  return send("PUT", "/auth/password", request);
}

export function listUsers(): Promise<UserResponse[]> {
  return send("GET", "/users");
}

export function createUser(request: { userName: string; displayName: string; role: UserRole }): Promise<TemporaryPasswordResponse> {
  return send("POST", "/users", request);
}

export function updateUser(
  id: number,
  request: { displayName: string; role: UserRole; disabled: boolean },
): Promise<UserResponse> {
  return send("PUT", `/users/${id}`, request);
}

export function resetUserPassword(id: number): Promise<TemporaryPasswordResponse> {
  return send("POST", `/users/${id}/reset-password`);
}

/** Deletes an account; its templates become yours ("transfer") or are deleted with it. */
export function deleteUser(id: number, templates: "transfer" | "delete" = "transfer"): Promise<void> {
  return send("DELETE", `/users/${id}?templates=${templates}`);
}
