import { useState, type FormEvent, type PropsWithChildren } from "react";
import { useTranslation } from "react-i18next";
import { changePassword, createFirstUser, login, type AuthState } from "../api/auth";
import "../setup/setup.css";

const MIN_PASSWORD_LENGTH = 8;

function AuthCard({ title, intro, onSubmit, children }: PropsWithChildren<{ title: string; intro: string; onSubmit: (e: FormEvent) => void }>) {
  const { t } = useTranslation();

  return (
    <main className="setup-page">
      <form className="card setup-card page-enter" onSubmit={onSubmit}>
        <div className="setup-card__brand">
          <img src="/logo.svg" alt="" width={36} height={36} />
          <span>{t("app.name")}</span>
        </div>
        <div>
          <h2>{title}</h2>
          <p className="setup-card__intro">{intro}</p>
        </div>
        {children}
      </form>
    </main>
  );
}

function ErrorMessage({ message }: { message: string | null }) {
  return message ? (
    <p className="setup-card__result setup-card__result--error" role="alert">
      {message}
    </p>
  ) : null;
}

function errorText(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

/** New password twice, checked here before the server checks it again. */
function usePasswordPair() {
  const [password, setPassword] = useState("");
  const [repeat, setRepeat] = useState("");
  return { password, setPassword, repeat, setRepeat };
}

function passwordProblem(t: (key: string, options?: Record<string, unknown>) => string, password: string, repeat: string) {
  if (password.length < MIN_PASSWORD_LENGTH) return t("auth.passwordTooShort", { count: MIN_PASSWORD_LENGTH });
  if (password !== repeat) return t("auth.passwordsDiffer");
  return null;
}

export function LoginPage({ onSignedIn }: { onSignedIn: (state: AuthState) => void }) {
  const { t } = useTranslation();
  const [userName, setUserName] = useState("");
  const [password, setPassword] = useState("");
  const [rememberMe, setRememberMe] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);

    try {
      onSignedIn(await login({ userName: userName.trim(), password, rememberMe }));
    } catch (err) {
      setError(errorText(err, t("auth.failedFallback")));
      setBusy(false);
    }
  }

  return (
    <AuthCard title={t("auth.signInTitle")} intro={t("auth.signInIntro")} onSubmit={handleSubmit}>
      <label className="setup-field">
        {t("auth.userName")}
        <input
          value={userName}
          onChange={(e) => setUserName(e.target.value)}
          autoComplete="username"
          spellCheck={false}
          required
          autoFocus
        />
      </label>
      <label className="setup-field">
        {t("auth.password")}
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoComplete="current-password"
          required
        />
      </label>
      <label className="setup-check">
        <input type="checkbox" checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} />
        {t("auth.rememberMe")}
      </label>
      <ErrorMessage message={error} />
      <div className="setup-card__actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? t("auth.signingIn") : t("auth.signIn")}
        </button>
      </div>
      <p className="setup-card__note">{t("auth.forgotPassword")}</p>
    </AuthCard>
  );
}

/** The first account, which becomes the administrator. */
export function CreateAccountPage({
  onCreated,
  onCancel,
}: {
  onCreated: (state: AuthState) => void;
  /** Only for an existing install that stays open until an account is created. */
  onCancel?: () => void;
}) {
  const { t } = useTranslation();
  const [userName, setUserName] = useState("");
  const [displayName, setDisplayName] = useState("");
  const pair = usePasswordPair();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const problem = passwordProblem(t, pair.password, pair.repeat);
    if (problem) {
      setError(problem);
      return;
    }

    setBusy(true);
    setError(null);

    try {
      onCreated(await createFirstUser({ userName: userName.trim(), displayName: displayName.trim(), password: pair.password }));
    } catch (err) {
      setError(errorText(err, t("auth.failedFallback")));
      setBusy(false);
    }
  }

  return (
    <AuthCard title={t("auth.createTitle")} intro={t("auth.createIntro")} onSubmit={handleSubmit}>
      <label className="setup-field">
        {t("auth.userName")}
        <input
          value={userName}
          onChange={(e) => setUserName(e.target.value)}
          autoComplete="username"
          spellCheck={false}
          required
          autoFocus
        />
      </label>
      <label className="setup-field">
        {t("auth.displayNameOptional")}
        <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} autoComplete="name" />
      </label>
      <label className="setup-field">
        {t("auth.password")}
        <input
          type="password"
          value={pair.password}
          onChange={(e) => pair.setPassword(e.target.value)}
          autoComplete="new-password"
          required
        />
      </label>
      <label className="setup-field">
        {t("auth.repeatPassword")}
        <input
          type="password"
          value={pair.repeat}
          onChange={(e) => pair.setRepeat(e.target.value)}
          autoComplete="new-password"
          required
        />
      </label>
      <p className="setup-card__note">{t("auth.createNote")}</p>
      <ErrorMessage message={error} />
      <div className="setup-card__actions">
        {onCancel && (
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>
            {t("common.cancel")}
          </button>
        )}
        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? t("auth.creating") : t("auth.create")}
        </button>
      </div>
    </AuthCard>
  );
}

/** Change password form, used both forced (after a temporary password) and from the account menu. */
export function ChangePasswordForm({
  onChanged,
  onCancel,
  cancelLabel,
}: {
  onChanged: (state: AuthState) => void;
  onCancel?: () => void;
  cancelLabel?: string;
}) {
  const { t } = useTranslation();
  const [current, setCurrent] = useState("");
  const pair = usePasswordPair();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const problem = passwordProblem(t, pair.password, pair.repeat);
    if (problem) {
      setError(problem);
      return;
    }

    setBusy(true);
    setError(null);

    try {
      onChanged(await changePassword({ currentPassword: current, newPassword: pair.password }));
    } catch (err) {
      setError(errorText(err, t("auth.failedFallback")));
      setBusy(false);
    }
  }

  return (
    <form className="auth-password-form" onSubmit={handleSubmit}>
      <label className="setup-field">
        {t("auth.currentPassword")}
        <input
          type="password"
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
          autoComplete="current-password"
          required
          autoFocus
        />
      </label>
      <label className="setup-field">
        {t("auth.newPassword")}
        <input
          type="password"
          value={pair.password}
          onChange={(e) => pair.setPassword(e.target.value)}
          autoComplete="new-password"
          required
        />
      </label>
      <label className="setup-field">
        {t("auth.repeatPassword")}
        <input
          type="password"
          value={pair.repeat}
          onChange={(e) => pair.setRepeat(e.target.value)}
          autoComplete="new-password"
          required
        />
      </label>
      <ErrorMessage message={error} />
      <div className="setup-card__actions">
        {onCancel && (
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>
            {cancelLabel ?? t("common.cancel")}
          </button>
        )}
        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? t("auth.saving") : t("auth.changePassword")}
        </button>
      </div>
    </form>
  );
}

/** After signing in with a temporary password. */
export function ForcedPasswordChangePage({
  onChanged,
  onSignOut,
}: {
  onChanged: (state: AuthState) => void;
  onSignOut: () => void;
}) {
  const { t } = useTranslation();

  return (
    <main className="setup-page">
      <div className="card setup-card page-enter">
        <div className="setup-card__brand">
          <img src="/logo.svg" alt="" width={36} height={36} />
          <span>{t("app.name")}</span>
        </div>
        <div>
          <h2>{t("auth.forcedTitle")}</h2>
          <p className="setup-card__intro">{t("auth.forcedIntro")}</p>
        </div>
        <ChangePasswordForm onChanged={onChanged} onCancel={onSignOut} cancelLabel={t("auth.signOut")} />
      </div>
    </main>
  );
}
