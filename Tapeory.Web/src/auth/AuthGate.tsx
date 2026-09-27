import { useCallback, useEffect, useMemo, useState, type PropsWithChildren } from "react";
import { getAuthState, logout, type AuthState } from "../api/auth";
import { SESSION_ENDED_EVENT } from "../api/session";
import { useSetupJustCompleted } from "../setup/SetupGate";
import { AuthContext, type AuthContextValue } from "./AuthContext";
import { CreateAccountPage, ForcedPasswordChangePage, LoginPage } from "./AuthPages";

/**
 * Decides what to show: the app, the sign-in page, "create your account", or "choose a new
 * password". Without any account Tapeory stays open, as before accounts existed; a brand-new
 * install goes straight from the database setup to creating the first (admin) account.
 */
export function AuthGate({ children }: PropsWithChildren) {
  const justSetUp = useSetupJustCompleted();
  const [state, setState] = useState<AuthState | null>(null);
  // An older or unreachable server: show the app, whose pages report their own errors.
  const [unavailable, setUnavailable] = useState(false);
  const [creatingAccount, setCreatingAccount] = useState(false);

  const refresh = useCallback(async () => {
    try {
      setState(await getAuthState());
      setUnavailable(false);
    } catch {
      setUnavailable(true);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // A 401 from any call: the session ended, so ask the server what to show now.
  useEffect(() => {
    const onSessionEnded = () => void refresh();
    window.addEventListener(SESSION_ENDED_EVENT, onSessionEnded);
    return () => window.removeEventListener(SESSION_ENDED_EVENT, onSessionEnded);
  }, [refresh]);

  const signOut = useCallback(async () => {
    try {
      await logout();
    } finally {
      await refresh();
    }
  }, [refresh]);

  const value = useMemo<AuthContextValue>(
    () => ({
      hasUsers: state?.hasUsers ?? false,
      user: state?.user ?? null,
      openAccess: state !== null && !state.hasUsers,
      canAdminister: !state?.hasUsers || state.user?.role === "Admin",
      signOut,
      startCreatingAccount: () => setCreatingAccount(true),
      refresh,
    }),
    [state, signOut, refresh],
  );

  const signedIn = (next: AuthState) => {
    setCreatingAccount(false);
    setState(next);
  };

  if (!state && !unavailable) {
    return null;
  }

  if (state && !state.hasUsers && (creatingAccount || justSetUp)) {
    return <CreateAccountPage onCreated={signedIn} onCancel={justSetUp ? undefined : () => setCreatingAccount(false)} />;
  }

  if (state?.hasUsers && !state.user) {
    return <LoginPage onSignedIn={signedIn} />;
  }

  if (state?.user?.mustChangePassword) {
    return <ForcedPasswordChangePage onChanged={signedIn} onSignOut={() => void signOut()} />;
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
