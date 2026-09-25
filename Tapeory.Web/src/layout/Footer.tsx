import { useTranslation } from "react-i18next";

/** Editor and publisher of this Tapeory build; a name, so never translated. */
const PUBLISHER = "Tunefish";

export function Footer() {
  const { t } = useTranslation();
  const year = new Date().getFullYear();

  return (
    <footer className="app-footer">
      <div className="app-footer__inner">
        <div className="app-footer__brand">
          <img src="/logo.svg" alt="" width={20} height={20} />
          <span>{t("app.name")}</span>
        </div>

        <div className="app-footer__copy-group">
          <p className="app-footer__copy">{t("footer.copyright", { year })}</p>
          <p className="app-footer__tagline">{t("footer.tagline")}</p>
          <p className="app-footer__credit">{t("footer.editorPublisher", { name: PUBLISHER })}</p>
        </div>

        <div className="app-footer__meta">v{__APP_VERSION__}</div>
      </div>
    </footer>
  );
}
