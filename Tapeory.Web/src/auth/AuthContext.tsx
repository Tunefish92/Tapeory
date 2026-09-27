import { createContext, useContext } from "react";
import type { CurrentUser } from "../api/auth";

export interface AuthContextValue {
  /** False until the first account exists: Tapeory is open to everyone. */
  hasUsers: boolean;
  user: CurrentUser | null;
  /** No account exists yet, as reported by the server: shows the "create an account" banner. */
  openAccess: boolean;
  /** Admin, or anyone while there are no accounts (the server applies the same rule). */
  canAdminister: boolean;
  signOut: () => Promise<void>;
  /** Opens "create your account" while Tapeory has no accounts yet. */
  startCreatingAccount: () => void;
  /** Re-reads who is signed in, e.g. after changing the password. */
  refresh: () => Promise<void>;
}

// Outside the gate (component tests, or before it loads) everything behaves as it did without
// accounts: open, nobody signed in.
const OPEN: AuthContextValue = {
  hasUsers: false,
  user: null,
  openAccess: false,
  canAdminister: true,
  signOut: async () => {},
  startCreatingAccount: () => {},
  refresh: async () => {},
};

export const AuthContext = createContext<AuthContextValue>(OPEN);

export function useAuth(): AuthContextValue {
  return useContext(AuthContext);
}
