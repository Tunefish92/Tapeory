import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { configureDatabase, testDatabaseConnection, type DatabaseSetupRequest } from "../api/setup";
import "./setup.css";

interface FormState {
  host: string;
  /** Kept as text so the field can be cleared while typing. */
  port: string;
  database: string;
  user: string;
  password: string;
}

const INITIAL_FORM: FormState = { host: "", port: "3306", database: "tapeory", user: "tapeory", password: "" };

function toRequest(form: FormState): DatabaseSetupRequest {
  return {
    host: form.host.trim(),
    port: Number(form.port) || 3306,
    database: form.database.trim(),
    user: form.user.trim(),
    password: form.password,
  };
}

/** First-run screen: asks for the database connection before anything else can work. */
export function SetupPage({ onComplete }: { onComplete: () => void }) {
  const { t } = useTranslation();
  const [form, setForm] = useState<FormState>(INITIAL_FORM);
  const [busy, setBusy] = useState<"test" | "save" | null>(null);
  const [result, setResult] = useState<{ success: boolean; message: string } | null>(null);

  function update(field: keyof FormState, value: string) {
    setForm((prev) => ({ ...prev, [field]: value }));
    setResult(null);
  }

  async function handleTest() {
    setBusy("test");
    setResult(null);

    try {
      const request = toRequest(form);
      const test = await testDatabaseConnection(request);
      setResult(
        test.isSuccess
          ? {
              success: true,
              message: test.databaseExists
                ? t("setup.testSucceeded")
                : t("setup.testSucceededNewDatabase", { database: request.database }),
            }
          : { success: false, message: test.errorMessage ?? t("setup.failedFallback") },
      );
    } catch (err) {
      setResult({ success: false, message: err instanceof Error ? err.message : t("setup.failedFallback") });
    } finally {
      setBusy(null);
    }
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy("save");
    setResult(null);

    try {
      await configureDatabase(toRequest(form));
      onComplete();
    } catch (err) {
      setResult({ success: false, message: err instanceof Error ? err.message : t("setup.failedFallback") });
      setBusy(null);
    }
  }

  return (
    <main className="setup-page">
      <form className="card setup-card page-enter" onSubmit={handleSubmit}>
        <div className="setup-card__brand">
          <img src="/logo.svg" alt="" width={36} height={36} />
          <span>{t("app.name")}</span>
        </div>

        <div>
          <h2>{t("setup.title")}</h2>
          <p className="setup-card__intro">{t("setup.intro")}</p>
        </div>

        <div className="setup-card__row">
          <label className="setup-field setup-field--grow">
            {t("setup.host")}
            <input
              value={form.host}
              onChange={(e) => update("host", e.target.value)}
              placeholder={t("setup.hostPlaceholder")}
              autoComplete="off"
              spellCheck={false}
              required
              autoFocus
            />
          </label>
          <label className="setup-field setup-field--port">
            {t("setup.port")}
            <input
              type="number"
              min={1}
              max={65535}
              value={form.port}
              onChange={(e) => update("port", e.target.value)}
              required
            />
          </label>
        </div>

        <label className="setup-field">
          {t("setup.database")}
          <input
            value={form.database}
            onChange={(e) => update("database", e.target.value)}
            autoComplete="off"
            spellCheck={false}
            required
          />
        </label>

        <label className="setup-field">
          {t("setup.user")}
          <input
            value={form.user}
            onChange={(e) => update("user", e.target.value)}
            autoComplete="off"
            spellCheck={false}
            required
          />
        </label>

        <label className="setup-field">
          {t("setup.password")}
          <input
            type="password"
            value={form.password}
            onChange={(e) => update("password", e.target.value)}
            autoComplete="new-password"
          />
        </label>

        <p className="setup-card__note">{t("setup.storedNote")}</p>

        {busy === "test" && <p className="setup-card__progress">{t("setup.testing")}</p>}
        {busy === "save" && <p className="setup-card__progress">{t("setup.saving")}</p>}
        {result && (
          <p className={`setup-card__result setup-card__result--${result.success ? "success" : "error"}`} role={result.success ? "status" : "alert"}>
            {result.message}
          </p>
        )}

        <div className="setup-card__actions">
          <button type="button" className="btn" onClick={handleTest} disabled={busy !== null || !form.host.trim()}>
            {t("setup.test")}
          </button>
          <button type="submit" className="btn btn-primary" disabled={busy !== null}>
            {busy === "save" ? t("setup.saving") : t("setup.save")}
          </button>
        </div>
      </form>
    </main>
  );
}
