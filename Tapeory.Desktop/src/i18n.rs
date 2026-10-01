//! The web UI's translations (Tapeory.Web/src/i18n/locales), compiled in, so both say the same.
//! Keys are dotted paths ("nav.dashboard"); `{{name}}` placeholders are filled in, and a `count`
//! argument picks the `_one` / `_other` form where one exists.

use std::collections::HashMap;
use std::sync::{OnceLock, RwLock};

use serde_json::Value;

pub const LANGUAGES: [(&str, &str); 5] = [("de", "Deutsch"), ("en", "English"), ("es", "Español"), ("fr", "Français"), ("it", "Italiano")];

const SOURCES: [(&str, &str); 5] = [
    ("en", include_str!("../../Tapeory.Web/src/i18n/locales/en.json")),
    ("de", include_str!("../../Tapeory.Web/src/i18n/locales/de.json")),
    ("es", include_str!("../../Tapeory.Web/src/i18n/locales/es.json")),
    ("fr", include_str!("../../Tapeory.Web/src/i18n/locales/fr.json")),
    ("it", include_str!("../../Tapeory.Web/src/i18n/locales/it.json")),
];

struct Catalog {
    language: String,
    texts: HashMap<&'static str, HashMap<String, String>>,
}

fn catalog() -> &'static RwLock<Catalog> {
    static CATALOG: OnceLock<RwLock<Catalog>> = OnceLock::new();

    CATALOG.get_or_init(|| {
        let texts = SOURCES
            .iter()
            .map(|(language, json)| {
                let mut flat = HashMap::new();
                if let Ok(value) = serde_json::from_str::<Value>(json) {
                    flatten("", &value, &mut flat);
                }
                (*language, flat)
            })
            .collect();

        RwLock::new(Catalog { language: system_language(), texts })
    })
}

fn flatten(prefix: &str, value: &Value, out: &mut HashMap<String, String>) {
    match value {
        Value::Object(map) => {
            for (key, child) in map {
                let path = if prefix.is_empty() { key.clone() } else { format!("{prefix}.{key}") };
                flatten(&path, child, out);
            }
        }
        Value::String(text) => {
            out.insert(prefix.to_string(), text.clone());
        }
        _ => {}
    }
}

/// The operating system's language, if Tapeory has it; English otherwise.
fn system_language() -> String {
    let candidates = ["LC_ALL", "LC_MESSAGES", "LANG", "LANGUAGE"];
    let from_env = candidates.iter().filter_map(|name| std::env::var(name).ok()).next();

    from_env
        .map(|value| value.chars().take(2).collect::<String>().to_lowercase())
        .filter(|code| LANGUAGES.iter().any(|(known, _)| known == code))
        .unwrap_or_else(|| "en".to_string())
}

pub fn language() -> String {
    catalog().read().map(|c| c.language.clone()).unwrap_or_else(|_| "en".to_string())
}

pub fn set_language(code: &str) {
    if LANGUAGES.iter().any(|(known, _)| *known == code)
        && let Ok(mut catalog) = catalog().write()
    {
        catalog.language = code.to_string();
    }
}

fn lookup(key: &str) -> Option<String> {
    let catalog = catalog().read().ok()?;
    catalog
        .texts
        .get(catalog.language.as_str())
        .and_then(|texts| texts.get(key))
        .or_else(|| catalog.texts.get("en").and_then(|texts| texts.get(key)))
        .cloned()
}

/// The text for `key`, or the key itself if there's none.
pub fn t(key: &str) -> String {
    lookup(key).unwrap_or_else(|| key.to_string())
}

/// The text for `key` with `{{name}}` placeholders filled in.
pub fn tf(key: &str, args: &[(&str, &str)]) -> String {
    let count = args.iter().find(|(name, _)| *name == "count").map(|(_, value)| *value);

    let template = count
        .and_then(|count| lookup(&format!("{key}_{}", if count == "1" { "one" } else { "other" })))
        .or_else(|| lookup(key))
        .unwrap_or_else(|| key.to_string());

    args.iter().fold(template, |text, (name, value)| text.replace(&format!("{{{{{name}}}}}"), value))
}

#[cfg(test)]
mod tests {
    use super::*;

    // One test: the language is global, and tests run in parallel.
    #[test]
    fn translates_fills_in_placeholders_and_plurals() {
        set_language("en");
        assert_eq!(t("nav.dashboard"), "Dashboard");
        assert_eq!(tf("auth.passwordTooShort", &[("count", "8")]), "Use at least 8 characters.");
        assert_eq!(tf("printing.printJobsList.itemsCount", &[("count", "1")]), "1 item");
        assert_eq!(tf("printing.printJobsList.itemsCount", &[("count", "3")]), "3 items");
        assert_eq!(t("no.such.key"), "no.such.key");

        set_language("de");
        assert_ne!(t("nav.settings"), "Settings");
        set_language("xx");
        assert_eq!(language(), "de");
        set_language("en");
    }
}
