import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ensureFontLoaded, listFonts } from "../api/fonts";

interface FontFamilySelectProps {
  value: string;
  onChange: (family: string) => void;
}

/**
 * Picks from the fonts installed on the server — the only ones the printed label can use. A
 * family that isn't installed (e.g. from an imported .lbx) stays selectable and is flagged, so
 * opening an old template never silently changes its font.
 */
export function FontFamilySelect({ value, onChange }: FontFamilySelectProps) {
  const { t } = useTranslation();
  const [families, setFamilies] = useState<string[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    listFonts()
      .then((list) => {
        if (!cancelled) setFamilies(list);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    // So the preview line below shows the real font, even if this computer lacks it.
    void ensureFontLoaded(value, false);
  }, [value]);

  const label = t("editor.properties.fontFamily");

  // Without the list (API unreachable) fall back to free text rather than blocking editing.
  if (failed) {
    return (
      <label className="properties-field">
        {label}
        <input type="text" value={value} onChange={(e) => onChange(e.target.value)} />
      </label>
    );
  }

  const installed = families?.find((family) => family.toLowerCase() === value.toLowerCase());
  const selected = installed ?? value;

  return (
    <label className="properties-field">
      {label}
      <select value={selected} disabled={!families} onChange={(e) => onChange(e.target.value)}>
        {!families && <option value={value}>{value}</option>}
        {families && !installed && (
          <option value={value}>{t("editor.properties.fontNotInstalled", { font: value || "—" })}</option>
        )}
        {families?.map((family) => (
          <option key={family} value={family}>
            {family}
          </option>
        ))}
      </select>
      <span className="font-preview" style={{ fontFamily: `"${selected.replace(/"/g, "")}", sans-serif` }} aria-hidden="true">
        {t("editor.properties.fontPreview")}
      </span>
    </label>
  );
}
