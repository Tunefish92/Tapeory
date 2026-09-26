import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import LanguageDetector from "i18next-browser-languagedetector";
import en from "./locales/en.json";
import de from "./locales/de.json";
import it from "./locales/it.json";
import es from "./locales/es.json";
import zh from "./locales/zh.json";
import hi from "./locales/hi.json";
import ar from "./locales/ar.json";
import fr from "./locales/fr.json";
import bn from "./locales/bn.json";
import pt from "./locales/pt.json";
import ru from "./locales/ru.json";
import id from "./locales/id.json";

export const SUPPORTED_LANGUAGES = ["en", "de", "it", "es", "zh", "hi", "ar", "fr", "bn", "pt", "ru", "id"] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      en: { translation: en },
      de: { translation: de },
      it: { translation: it },
      es: { translation: es },
      zh: { translation: zh },
      hi: { translation: hi },
      ar: { translation: ar },
      fr: { translation: fr },
      bn: { translation: bn },
      pt: { translation: pt },
      ru: { translation: ru },
      id: { translation: id },
    },
    fallbackLng: "en",
    supportedLngs: SUPPORTED_LANGUAGES,
    detection: {
      order: ["localStorage"],
      lookupLocalStorage: "tapeory.language",
      caches: ["localStorage"],
    },
    interpolation: { escapeValue: false },
  });

/** Keeps <html lang> and <html dir> in step with the UI language, so the browser picks fitting
 * fonts and hyphenation, and Arabic lays out right-to-left. */
function applyToDocument(lng: string) {
  document.documentElement.lang = lng;
  document.documentElement.dir = i18n.dir(lng);
}

if (typeof document !== "undefined") {
  i18n.on("languageChanged", applyToDocument);
  if (i18n.language) applyToDocument(i18n.language);
}

export default i18n;
