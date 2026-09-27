import { useTranslation } from "react-i18next";

/** Editor and publisher of this Tapeory build; a name, so never translated. */
const PUBLISHER = "Tunefish";
const PUBLISHER_URL = "https://github.com/Tunefish92?tab=repositories";

export function Footer() {
  const { t } = useTranslation();
  const year = new Date().getFullYear();

  return (
    <footer className="app-footer">
      <p className="app-footer__copy">
        {t("footer.copyright", { year })}{" "}
        <a href={PUBLISHER_URL} target="_blank" rel="noreferrer">
          {PUBLISHER}
        </a>
      </p>
    </footer>
  );
}
