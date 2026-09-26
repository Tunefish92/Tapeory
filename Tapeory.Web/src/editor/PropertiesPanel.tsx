import { useTranslation } from "react-i18next";
import { useUnits } from "../settings/AppSettingsContext";
import type { ReorderDirection } from "./document";
import { FontFamilySelect } from "./FontFamilySelect";
import type { LabelObject, LabelObjectPatch, TextFit } from "./types";

interface PropertiesPanelProps {
  object: LabelObject | null;
  onChange: (changes: LabelObjectPatch) => void;
  onDelete: () => void;
  onDuplicate: () => void;
  onReorder: (direction: ReorderDirection) => void;
}

function NumberField({
  label,
  value,
  onChange,
  step = 0.5,
}: {
  label: string;
  value: number;
  onChange: (value: number) => void;
  step?: number;
}) {
  return (
    <label className="properties-field">
      {label}
      <input
        type="number"
        step={step}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
      />
    </label>
  );
}

/** A length stored in millimetres, shown and typed in the configured unit. */
function LengthField({
  labelKey,
  value,
  onChange,
  mmStep,
}: {
  labelKey: string;
  value: number;
  onChange: (mm: number) => void;
  /** Step when the unit is mm (inch fields always step by 0.01 in). */
  mmStep?: number;
}) {
  const { t } = useTranslation();
  const units = useUnits();

  return (
    <NumberField
      label={t(labelKey, { unit: units.symbol })}
      value={units.toDisplay(value)}
      step={units.unit === "mm" ? (mmStep ?? units.step) : units.step}
      onChange={(display) => onChange(units.fromDisplay(display))}
    />
  );
}

function FitControls({ id, fit, onChange }: { id: string; fit: TextFit; onChange: (fit: TextFit) => void }) {
  const { t } = useTranslation();
  const hint = fit === "shrink" ? "fitShrinkHint" : fit === "wrap" ? "fitWrapHint" : "fixedSizeHint";

  return (
    <div className="properties-fit">
      <label className="properties-field properties-field--inline">
        <input
          type="checkbox"
          checked={fit !== "none"}
          onChange={(e) => onChange(e.target.checked ? "shrink" : "none")}
        />
        {t("editor.properties.fixedSize")}
      </label>
      {fit !== "none" && (
        <fieldset className="properties-fit__modes">
          <legend className="visually-hidden">{t("editor.properties.fitMode")}</legend>
          <label className="properties-field properties-field--inline">
            <input type="radio" name={`fit-${id}`} checked={fit === "shrink"} onChange={() => onChange("shrink")} />
            {t("editor.properties.fitShrink")}
          </label>
          <label className="properties-field properties-field--inline">
            <input type="radio" name={`fit-${id}`} checked={fit === "wrap"} onChange={() => onChange("wrap")} />
            {t("editor.properties.fitWrap")}
          </label>
        </fieldset>
      )}
      <p className="properties-hint">{t(`editor.properties.${hint}`)}</p>
    </div>
  );
}

export function PropertiesPanel({
  object,
  onChange,
  onDelete,
  onDuplicate,
  onReorder,
}: PropertiesPanelProps) {
  const { t } = useTranslation();

  const typeLabels: Record<LabelObject["type"], string> = {
    text: t("editor.properties.typeText"),
    dynamicField: t("editor.properties.typeDynamicField"),
    rect: t("editor.properties.typeRect"),
    line: t("editor.properties.typeLine"),
    image: t("editor.properties.typeImage"),
  };

  if (!object) {
    return (
      <aside className="properties-panel" aria-label="Properties">
        <p>{t("editor.properties.selectPrompt")}</p>
      </aside>
    );
  }

  return (
    <aside className="properties-panel" aria-label="Properties">
      <h3>{typeLabels[object.type]}</h3>

      <div className="properties-field-group">
        <LengthField labelKey="editor.properties.x" value={object.x} onChange={(x) => onChange({ x })} />
        <LengthField labelKey="editor.properties.y" value={object.y} onChange={(y) => onChange({ y })} />
        <NumberField
          label={t("editor.properties.rotation")}
          value={object.rotation}
          step={1}
          onChange={(rotation) => onChange({ rotation })}
        />
      </div>

      {(object.type === "text" || object.type === "dynamicField") && (
        <div className="properties-field-group">
          {object.type === "text" ? (
            <label className="properties-field">
              {t("editor.properties.text")}
              <textarea
                value={object.text}
                onChange={(e) => onChange({ text: e.target.value })}
              />
            </label>
          ) : (
            <>
              <label className="properties-field">
                {t("editor.properties.fieldName")}
                <input
                  type="text"
                  value={object.fieldName}
                  onChange={(e) => onChange({ fieldName: e.target.value })}
                />
              </label>
              <label className="properties-field">
                {t("editor.properties.label")}
                <input
                  type="text"
                  value={object.label}
                  onChange={(e) => onChange({ label: e.target.value })}
                />
              </label>
              <label className="properties-field">
                {t("editor.properties.defaultValue")}
                <input
                  type="text"
                  value={object.defaultValue}
                  onChange={(e) => onChange({ defaultValue: e.target.value })}
                />
              </label>
              <label className="properties-field properties-field--inline">
                <input
                  type="checkbox"
                  checked={object.required}
                  onChange={(e) => onChange({ required: e.target.checked })}
                />
                {t("editor.properties.required")}
              </label>
            </>
          )}

          <LengthField
            labelKey="editor.properties.width"
            value={object.width}
            onChange={(width) => onChange({ width })}
          />
          <LengthField
            labelKey="editor.properties.height"
            value={object.height}
            onChange={(height) => onChange({ height })}
          />
          <FitControls id={object.id} fit={object.fit ?? "none"} onChange={(fit) => onChange({ fit })} />
          <NumberField
            label={t(
              (object.fit ?? "none") === "none" ? "editor.properties.fontSize" : "editor.properties.maxFontSize",
            )}
            value={object.fontSize}
            step={1}
            onChange={(fontSize) => onChange({ fontSize })}
          />
          <FontFamilySelect value={object.fontFamily} onChange={(fontFamily) => onChange({ fontFamily })} />
          <label className="properties-field properties-field--inline">
            <input
              type="checkbox"
              checked={object.fontWeight === "bold"}
              onChange={(e) => onChange({ fontWeight: e.target.checked ? "bold" : "normal" })}
            />
            {t("editor.properties.bold")}
          </label>
          <label className="properties-field">
            {t("editor.properties.align")}
            <select
              value={object.align}
              onChange={(e) => onChange({ align: e.target.value as "left" | "center" | "right" })}
            >
              <option value="left">{t("editor.properties.alignLeft")}</option>
              <option value="center">{t("editor.properties.alignCenter")}</option>
              <option value="right">{t("editor.properties.alignRight")}</option>
            </select>
          </label>
          <label className="properties-field">
            {t("editor.properties.color")}
            <input
              type="color"
              value={object.fill}
              onChange={(e) => onChange({ fill: e.target.value })}
            />
          </label>
        </div>
      )}

      {object.type === "rect" && (
        <div className="properties-field-group">
          <LengthField
            labelKey="editor.properties.width"
            value={object.width}
            onChange={(width) => onChange({ width })}
          />
          <LengthField
            labelKey="editor.properties.height"
            value={object.height}
            onChange={(height) => onChange({ height })}
          />
          <label className="properties-field">
            {t("editor.properties.fill")}
            <input
              type="text"
              value={object.fill}
              onChange={(e) => onChange({ fill: e.target.value })}
            />
          </label>
          <label className="properties-field">
            {t("editor.properties.stroke")}
            <input
              type="color"
              value={object.stroke}
              onChange={(e) => onChange({ stroke: e.target.value })}
            />
          </label>
          <LengthField
            labelKey="editor.properties.strokeWidth"
            value={object.strokeWidth}
            mmStep={0.1}
            onChange={(strokeWidth) => onChange({ strokeWidth })}
          />
          <LengthField
            labelKey="editor.properties.cornerRadius"
            value={object.cornerRadius}
            onChange={(cornerRadius) => onChange({ cornerRadius })}
          />
        </div>
      )}

      {object.type === "line" && (
        <div className="properties-field-group">
          <LengthField
            labelKey="editor.properties.strokeWidth"
            value={object.strokeWidth}
            mmStep={0.1}
            onChange={(strokeWidth) => onChange({ strokeWidth })}
          />
          <label className="properties-field">
            {t("editor.properties.stroke")}
            <input
              type="color"
              value={object.stroke}
              onChange={(e) => onChange({ stroke: e.target.value })}
            />
          </label>
        </div>
      )}

      {object.type === "image" && (
        <div className="properties-field-group">
          <LengthField
            labelKey="editor.properties.width"
            value={object.width}
            onChange={(width) => onChange({ width })}
          />
          <LengthField
            labelKey="editor.properties.height"
            value={object.height}
            onChange={(height) => onChange({ height })}
          />
        </div>
      )}

      <div className="properties-field-group">
        <label className="properties-field properties-field--inline">
          <input
            type="checkbox"
            checked={object.locked}
            onChange={(e) => onChange({ locked: e.target.checked })}
          />
          {t("editor.properties.locked")}
        </label>
        <label className="properties-field properties-field--inline">
          <input
            type="checkbox"
            checked={object.hidden}
            onChange={(e) => onChange({ hidden: e.target.checked })}
          />
          {t("editor.properties.hidden")}
        </label>
      </div>

      <div className="properties-actions">
        <button type="button" onClick={() => onReorder("front")}>
          {t("editor.properties.bringToFront")}
        </button>
        <button type="button" onClick={() => onReorder("forward")}>
          {t("editor.properties.forward")}
        </button>
        <button type="button" onClick={() => onReorder("backward")}>
          {t("editor.properties.backward")}
        </button>
        <button type="button" onClick={() => onReorder("back")}>
          {t("editor.properties.sendToBack")}
        </button>
        <button type="button" onClick={onDuplicate}>
          {t("editor.properties.duplicate")}
        </button>
        <button type="button" className="btn btn-danger" onClick={onDelete}>
          {t("editor.properties.delete")}
        </button>
      </div>
    </aside>
  );
}
