import { useTranslation } from "react-i18next";
import type { TFunction } from "i18next";
import { TrashIcon } from "../components/icons";
import type { LabelObject } from "./types";

/** What an object is called in the list: its kind and what it shows. */
export function objectLabel(object: LabelObject, t: TFunction): string {
  const short = (text: string) => {
    const line = text.replace(/\s+/g, " ").trim();
    return line.length > 40 ? `${line.slice(0, 39)}…` : line;
  };

  switch (object.type) {
    case "text":
      return t("editor.objects.text", { text: short(object.text) });
    case "dynamicField":
      return t("editor.objects.field", { name: object.fieldName });
    case "barcode":
      return t("editor.objects.barcode", {
        type: t(`editor.barcode.${object.symbology}`, { defaultValue: object.symbology }),
        value: object.fieldName || short(object.data),
      });
    case "image":
      return t("editor.objects.image");
    case "rect":
      return t("editor.objects.rect");
    case "ellipse":
      return t("editor.objects.ellipse");
    case "line":
      return t("editor.objects.line");
  }
}

interface ObjectListProps {
  objects: LabelObject[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onDelete: (id: string) => void;
}

/** Everything on the label, by name: click to select, or delete without finding it on the canvas. */
export function ObjectList({ objects, selectedId, onSelect, onDelete }: ObjectListProps) {
  const { t, i18n } = useTranslation();
  const items = objects
    .map((object) => ({ object, label: objectLabel(object, t) }))
    .sort((a, b) => a.label.localeCompare(b.label, i18n.language, { numeric: true, sensitivity: "base" }));

  return (
    <section className="object-list" aria-labelledby="object-list-heading">
      <h3 id="object-list-heading">
        {t("editor.objects.heading")} <span className="object-list__count">{objects.length}</span>
      </h3>
      {items.length === 0 ? (
        <p>{t("editor.objects.empty")}</p>
      ) : (
        <ul>
          {items.map(({ object, label }) => (
            <li key={object.id} className={object.id === selectedId ? "object-list__item--selected" : undefined}>
              <button
                type="button"
                className="object-list__name"
                aria-pressed={object.id === selectedId}
                onClick={() => onSelect(object.id)}
              >
                {label}
                {object.hidden && <span className="object-list__note">{t("editor.objects.hidden")}</span>}
              </button>
              <button
                type="button"
                className="icon-link icon-link--danger"
                aria-label={t("editor.objects.delete", { name: label })}
                title={t("editor.objects.delete", { name: label })}
                onClick={() => onDelete(object.id)}
              >
                <TrashIcon />
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
