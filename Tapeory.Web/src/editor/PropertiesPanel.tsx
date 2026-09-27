import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useUnits } from "../settings/AppSettingsContext";
import type { ReorderDirection } from "./document";
import { FontFamilySelect } from "./FontFamilySelect";
import { useBarcodeEncoding } from "./BarcodeShape";
import type {
  BarcodeObject,
  BarcodeSymbology,
  DynamicFieldObject,
  LabelObject,
  LabelObjectPatch,
  TextFit,
  TextObject,
} from "./types";

interface PropertiesPanelProps {
  object: LabelObject | null;
  onChange: (changes: LabelObjectPatch) => void;
  onDelete: () => void;
  onDuplicate: () => void;
  onReorder: (direction: ReorderDirection) => void;
  /** The template's field names, offered when binding a barcode to a field. */
  fieldNames?: string[];
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

function Section({ title, className, children }: { title: string; className?: string; children: ReactNode }) {
  return (
    <section className={`properties-section${className ? ` ${className}` : ""}`}>
      <h4>{title}</h4>
      <div className="properties-section__fields">{children}</div>
    </section>
  );
}

/** Text and dynamic fields have the most settings, so they're grouped into labelled sections. */
function TextObjectSections({
  object,
  onChange,
}: {
  object: TextObject | DynamicFieldObject;
  onChange: (changes: LabelObjectPatch) => void;
}) {
  const { t } = useTranslation();
  const fit = object.fit ?? "none";

  return (
    <div className="properties-sections">
      <Section title={t("editor.properties.sectionContent")} className="properties-section--content">
        {object.type === "text" ? (
          <label className="properties-field properties-field--full">
            {t("editor.properties.text")}
            <textarea value={object.text} onChange={(e) => onChange({ text: e.target.value })} />
          </label>
        ) : (
          <>
            <label className="properties-field">
              {t("editor.properties.fieldName")}
              <input type="text" value={object.fieldName} onChange={(e) => onChange({ fieldName: e.target.value })} />
            </label>
            <label className="properties-field">
              {t("editor.properties.label")}
              <input type="text" value={object.label} onChange={(e) => onChange({ label: e.target.value })} />
            </label>
            <label className="properties-field">
              {t("editor.properties.defaultValue")}
              <input
                type="text"
                value={object.defaultValue}
                onChange={(e) => onChange({ defaultValue: e.target.value })}
              />
            </label>
            <label className="properties-field properties-field--inline properties-field--full">
              <input
                type="checkbox"
                checked={object.required}
                onChange={(e) => onChange({ required: e.target.checked })}
              />
              {t("editor.properties.required")}
            </label>
          </>
        )}
      </Section>

      <Section title={t("editor.properties.sectionFont")}>
        <div className="properties-field--full">
          <FontFamilySelect value={object.fontFamily} onChange={(fontFamily) => onChange({ fontFamily })} />
        </div>
        <NumberField
          label={t(fit === "none" ? "editor.properties.fontSize" : "editor.properties.maxFontSize")}
          value={object.fontSize}
          step={1}
          onChange={(fontSize) => onChange({ fontSize })}
        />
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
          <input type="color" value={object.fill} onChange={(e) => onChange({ fill: e.target.value })} />
        </label>
        <label className="properties-field properties-field--inline properties-field--end">
          <input
            type="checkbox"
            checked={object.fontWeight === "bold"}
            onChange={(e) => onChange({ fontWeight: e.target.checked ? "bold" : "normal" })}
          />
          {t("editor.properties.bold")}
        </label>
      </Section>

      <Section title={t("editor.properties.sectionLayout")}>
        <LengthField labelKey="editor.properties.x" value={object.x} onChange={(x) => onChange({ x })} />
        <LengthField labelKey="editor.properties.y" value={object.y} onChange={(y) => onChange({ y })} />
        <LengthField labelKey="editor.properties.width" value={object.width} onChange={(width) => onChange({ width })} />
        <LengthField
          labelKey="editor.properties.height"
          value={object.height}
          onChange={(height) => onChange({ height })}
        />
        <NumberField
          label={t("editor.properties.rotation")}
          value={object.rotation}
          step={1}
          onChange={(rotation) => onChange({ rotation })}
        />
      </Section>

      <Section title={t("editor.properties.sectionFit")}>
        <div className="properties-field--full">
          <FitControls id={object.id} fit={fit} onChange={(value) => onChange({ fit: value })} />
        </div>
        <div className="properties-field--full properties-options">
          <label className="properties-field properties-field--inline">
            <input type="checkbox" checked={object.locked} onChange={(e) => onChange({ locked: e.target.checked })} />
            {t("editor.properties.locked")}
          </label>
          <label className="properties-field properties-field--inline">
            <input type="checkbox" checked={object.hidden} onChange={(e) => onChange({ hidden: e.target.checked })} />
            {t("editor.properties.hidden")}
          </label>
        </div>
      </Section>
    </div>
  );
}

const SYMBOLOGIES: BarcodeSymbology[] = [
  "code128",
  "code39",
  "ean13",
  "ean8",
  "upca",
  "upce",
  "itf",
  "codabar",
  "qr",
  "datamatrix",
  "pdf417",
  "aztec",
];

const TWO_DIMENSIONAL: ReadonlySet<BarcodeSymbology> = new Set(["qr", "datamatrix", "pdf417", "aztec"]);

/** Barcodes: what they encode (a fixed value or a template field), how they look, and where. */
function BarcodeSections({
  object,
  onChange,
  fieldNames,
}: {
  object: BarcodeObject;
  onChange: (changes: LabelObjectPatch) => void;
  fieldNames: string[];
}) {
  const { t } = useTranslation();
  const encoding = useBarcodeEncoding(object.symbology, object.data);
  const fromField = object.fieldName !== "";
  const twoDimensional = TWO_DIMENSIONAL.has(object.symbology);
  const datalistId = `barcode-fields-${object.id}`;

  return (
    <div className="properties-sections">
      <Section title={t("editor.properties.sectionContent")} className="properties-section--content">
        <label className="properties-field properties-field--full">
          {t("editor.properties.barcodeType")}
          <select value={object.symbology} onChange={(e) => onChange({ symbology: e.target.value as BarcodeSymbology })}>
            {SYMBOLOGIES.map((symbology) => (
              <option key={symbology} value={symbology}>
                {t(`editor.barcode.${symbology}`)}
              </option>
            ))}
          </select>
        </label>
        <label className="properties-field properties-field--full">
          {t("editor.properties.barcodeSource")}
          <select
            value={fromField ? "field" : "fixed"}
            onChange={(e) =>
              onChange({ fieldName: e.target.value === "field" ? (fieldNames[0] ?? "code") : "" })
            }
          >
            <option value="fixed">{t("editor.properties.barcodeSourceFixed")}</option>
            <option value="field">{t("editor.properties.barcodeSourceField")}</option>
          </select>
        </label>
        {fromField && (
          <label className="properties-field properties-field--full">
            {t("editor.properties.fieldName")}
            <input
              type="text"
              list={datalistId}
              value={object.fieldName}
              onChange={(e) => onChange({ fieldName: e.target.value })}
            />
            <datalist id={datalistId}>
              {fieldNames.map((name) => (
                <option key={name} value={name} />
              ))}
            </datalist>
          </label>
        )}
        <label className="properties-field properties-field--full">
          {fromField ? t("editor.properties.defaultValue") : t("editor.properties.barcodeValue")}
          <input type="text" value={object.data} onChange={(e) => onChange({ data: e.target.value })} />
        </label>
        {encoding?.error && (
          <p className="properties-hint properties-hint--error properties-field--full" role="alert">
            {encoding.error}
          </p>
        )}
        {fromField && <p className="properties-hint properties-field--full">{t("editor.properties.barcodeFieldHint")}</p>}
      </Section>

      <Section title={t("editor.properties.sectionAppearance")}>
        {!twoDimensional && (
          <label className="properties-field properties-field--inline properties-field--full">
            <input type="checkbox" checked={object.showText} onChange={(e) => onChange({ showText: e.target.checked })} />
            {t("editor.properties.barcodeShowText")}
          </label>
        )}
        <label className="properties-field">
          {t("editor.properties.color")}
          <input type="color" value={object.fill} onChange={(e) => onChange({ fill: e.target.value })} />
        </label>
        <p className="properties-hint properties-field--full">{t("editor.properties.barcodeSizeHint")}</p>
      </Section>

      <Section title={t("editor.properties.sectionLayout")}>
        <LengthField labelKey="editor.properties.x" value={object.x} onChange={(x) => onChange({ x })} />
        <LengthField labelKey="editor.properties.y" value={object.y} onChange={(y) => onChange({ y })} />
        <LengthField labelKey="editor.properties.width" value={object.width} onChange={(width) => onChange({ width })} />
        <LengthField
          labelKey="editor.properties.height"
          value={object.height}
          onChange={(height) => onChange({ height })}
        />
        <NumberField
          label={t("editor.properties.rotation")}
          value={object.rotation}
          step={1}
          onChange={(rotation) => onChange({ rotation })}
        />
      </Section>

      <Section title={t("editor.properties.sectionOptions")}>
        <div className="properties-field--full properties-options properties-options--plain">
          <label className="properties-field properties-field--inline">
            <input type="checkbox" checked={object.locked} onChange={(e) => onChange({ locked: e.target.checked })} />
            {t("editor.properties.locked")}
          </label>
          <label className="properties-field properties-field--inline">
            <input type="checkbox" checked={object.hidden} onChange={(e) => onChange({ hidden: e.target.checked })} />
            {t("editor.properties.hidden")}
          </label>
        </div>
      </Section>
    </div>
  );
}

export function PropertiesPanel({
  object,
  onChange,
  onDelete,
  onDuplicate,
  onReorder,
  fieldNames = [],
}: PropertiesPanelProps) {
  const { t } = useTranslation();

  const typeLabels: Record<LabelObject["type"], string> = {
    text: t("editor.properties.typeText"),
    dynamicField: t("editor.properties.typeDynamicField"),
    rect: t("editor.properties.typeRect"),
    line: t("editor.properties.typeLine"),
    image: t("editor.properties.typeImage"),
    ellipse: t("editor.properties.typeEllipse"),
    barcode: t("editor.properties.typeBarcode"),
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
      <div className="properties-panel__header">
        <h3>{typeLabels[object.type]}</h3>
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
      </div>

      {object.type === "text" || object.type === "dynamicField" ? (
        <TextObjectSections object={object} onChange={onChange} />
      ) : object.type === "barcode" ? (
        <BarcodeSections object={object} onChange={onChange} fieldNames={fieldNames} />
      ) : (
        <>
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

        {object.type === "ellipse" && (
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
              <input type="text" value={object.fill} onChange={(e) => onChange({ fill: e.target.value })} />
            </label>
            <label className="properties-field">
              {t("editor.properties.stroke")}
              <input type="color" value={object.stroke} onChange={(e) => onChange({ stroke: e.target.value })} />
            </label>
            <LengthField
              labelKey="editor.properties.strokeWidth"
              value={object.strokeWidth}
              mmStep={0.1}
              onChange={(strokeWidth) => onChange({ strokeWidth })}
            />
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
        </>
      )}
    </aside>
  );
}
