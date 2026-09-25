import { describe, expect, it } from "vitest";
import i18n, { SUPPORTED_LANGUAGES } from ".";
import en from "./locales/en.json";

type Tree = { [key: string]: string | Tree };

function flatten(tree: Tree, prefix = ""): Map<string, string> {
  const result = new Map<string, string>();
  for (const [key, value] of Object.entries(tree)) {
    if (typeof value === "string") result.set(prefix + key, value);
    else for (const [k, v] of flatten(value, `${prefix}${key}.`)) result.set(k, v);
  }
  return result;
}

const placeholders = (text: string) => [...text.matchAll(/\{\{(\w+)\}\}/g)].map((m) => m[1]).sort();

const english = flatten(en as Tree);
/** Keys counted with i18next plurals: en has `<base>_other` (and `<base>` or `<base>_one`). */
const pluralBases = [...english.keys()].filter((k) => k.endsWith("_other")).map((k) => k.slice(0, -"_other".length));
const plainKeys = [...english.keys()].filter(
  (k) => !pluralBases.some((base) => k === base || k.startsWith(`${base}_`)),
);

describe.each(SUPPORTED_LANGUAGES)("locale %s", (lng) => {
  const strings = flatten(i18n.getResourceBundle(lng, "translation") as Tree);

  it("has every text the app uses, with the same placeholders", () => {
    const missing = plainKeys.filter((key) => !strings.has(key));
    expect(missing).toEqual([]);

    const mismatched = plainKeys.filter((key) => placeholders(strings.get(key)!).join() !== placeholders(english.get(key)!).join());
    expect(mismatched).toEqual([]);
  });

  it("has every plural form the language needs", () => {
    const categories = new Intl.PluralRules(lng).resolvedOptions().pluralCategories;
    const missing = pluralBases.flatMap((base) =>
      categories
        // Older files spell the singular as the bare key, which i18next falls back to.
        .filter((cat) => !strings.has(`${base}_${cat}`) && !(cat === "one" && strings.has(base)))
        .map((cat) => `${base}_${cat}`),
    );
    expect(missing).toEqual([]);
  });
});

describe("text direction", () => {
  it("lays Arabic out right-to-left and everything else left-to-right", async () => {
    await i18n.changeLanguage("ar");
    expect(document.documentElement.dir).toBe("rtl");
    expect(document.documentElement.lang).toBe("ar");

    await i18n.changeLanguage("ru");
    expect(document.documentElement.dir).toBe("ltr");

    await i18n.changeLanguage("en");
  });
});
