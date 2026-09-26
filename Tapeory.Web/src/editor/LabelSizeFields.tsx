import { useTranslation } from "react-i18next";
import { useUnits } from "../settings/AppSettingsContext";
import { findMediaPreset, getMediaPreset, MEDIA_GROUPS, MEDIA_PRESETS, type MediaPreset } from "./brotherMedia";

export interface LabelSizePatch {
  widthMm?: number;
  heightMm?: number;
  media?: string;
}

interface LabelSizeFieldsProps {
  widthMm: number;
  heightMm: number;
  media?: string;
  onChange: (patch: LabelSizePatch) => void;
}

const CUSTOM = "custom";

/**
 * Label height from Brother's official media only, plus the length (width). Die-cut labels fix
 * both, so the width field locks while one is selected.
 */
export function LabelSizeFields({ widthMm, heightMm, media, onChange }: LabelSizeFieldsProps) {
  const { t } = useTranslation();
  const units = useUnits();
  const preset = findMediaPreset(widthMm, heightMm, media);
  const dieCut = preset?.widthMm !== undefined;

  function presetLabel(item: MediaPreset): string {
    const code = item.code ? ` · ${item.code}` : "";
    if (item.round) return `Ø ${units.dimension(item.heightMm)}${code}`;
    if (item.widthMm !== undefined) {
      // Brother names die-cut labels "height × length", e.g. 29 × 90 mm.
      return `${units.number(item.heightMm)} × ${units.number(item.widthMm)} ${units.symbol}${code}`;
    }
    return `${units.dimension(item.heightMm)}${code}`;
  }

  return (
    <>
      <label className="properties-field">
        {t("editor.metadataHeight", { unit: units.symbol })}
        <select
          value={preset?.id ?? CUSTOM}
          onChange={(e) => {
            const next = getMediaPreset(e.target.value);
            if (!next) return;
            onChange({
              heightMm: next.heightMm,
              media: next.id,
              ...(next.widthMm !== undefined ? { widthMm: next.widthMm } : {}),
            });
          }}
        >
          {!preset && (
            <option value={CUSTOM}>{t("editor.mediaCustom", { size: units.dimension(heightMm) })}</option>
          )}
          {MEDIA_GROUPS.map((group) => (
            <optgroup key={group} label={t(`editor.mediaGroup.${group}`)}>
              {MEDIA_PRESETS.filter((item) => item.group === group).map((item) => (
                <option key={item.id} value={item.id}>
                  {presetLabel(item)}
                </option>
              ))}
            </optgroup>
          ))}
        </select>
      </label>

      <label className="properties-field">
        {t("editor.metadataWidth", { unit: units.symbol })}
        <input
          type="number"
          min={0}
          step={units.step}
          value={units.toDisplay(widthMm)}
          disabled={dieCut}
          title={dieCut ? t("editor.widthFixedByLabel") : undefined}
          onChange={(e) => onChange({ widthMm: units.fromDisplay(Number(e.target.value)) })}
        />
      </label>
    </>
  );
}
