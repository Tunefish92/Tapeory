import { useRef } from "react";
import { useTranslation } from "react-i18next";
import { MAX_ZOOM, MIN_ZOOM, ZOOM_STEP } from "./constants";
import type { LabelObjectType } from "./types";

interface ToolbarProps {
  onAddObject: (type: Exclude<LabelObjectType, "image">) => void;
  onAddImage: (file: File) => void;
  onUndo: () => void;
  onRedo: () => void;
  canUndo: boolean;
  canRedo: boolean;
  zoom: number;
  onZoomChange: (zoom: number) => void;
  previewMode: boolean;
  onTogglePreview: () => void;
  onSave: () => void;
  saving: boolean;
  saveError: string | null;
  /** Omitted for a template that hasn't been created yet — publishing only makes sense once
   * there's a saved template to publish. */
  onPublish?: () => void;
}

export function Toolbar({
  onAddObject,
  onAddImage,
  onUndo,
  onRedo,
  canUndo,
  canRedo,
  zoom,
  onZoomChange,
  previewMode,
  onTogglePreview,
  onSave,
  saving,
  saveError,
  onPublish,
}: ToolbarProps) {
  const { t } = useTranslation();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const clampZoom = (value: number) => Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, value));

  return (
    <div className="editor-toolbar" role="toolbar" aria-label="Editor tools">
      <div className="editor-toolbar__group">
        <button type="button" onClick={() => onAddObject("text")} disabled={previewMode}>
          {t("editor.toolbar.addText")}
        </button>
        <button type="button" onClick={() => onAddObject("dynamicField")} disabled={previewMode}>
          {t("editor.toolbar.addField")}
        </button>
        <button type="button" onClick={() => onAddObject("rect")} disabled={previewMode}>
          {t("editor.toolbar.addRectangle")}
        </button>
        <button type="button" onClick={() => onAddObject("line")} disabled={previewMode}>
          {t("editor.toolbar.addLine")}
        </button>
        <button
          type="button"
          onClick={() => fileInputRef.current?.click()}
          disabled={previewMode}
        >
          {t("editor.toolbar.addImage")}
        </button>
        <input
          ref={fileInputRef}
          type="file"
          accept="image/png,image/jpeg,image/webp,image/svg+xml,image/tiff,image/bmp,.tif,.tiff,.bmp"
          data-testid="image-file-input"
          hidden
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) {
              onAddImage(file);
            }
            e.target.value = "";
          }}
        />
      </div>

      <div className="editor-toolbar__group">
        <button type="button" onClick={onUndo} disabled={!canUndo} aria-label={t("editor.toolbar.undo")}>
          {t("editor.toolbar.undo")}
        </button>
        <button type="button" onClick={onRedo} disabled={!canRedo} aria-label={t("editor.toolbar.redo")}>
          {t("editor.toolbar.redo")}
        </button>
      </div>

      <div className="editor-toolbar__group">
        <button
          type="button"
          onClick={() => onZoomChange(clampZoom(zoom - ZOOM_STEP))}
          aria-label={t("editor.toolbar.zoomOut")}
        >
          −
        </button>
        <span>{Math.round(zoom * 100)}%</span>
        <button
          type="button"
          onClick={() => onZoomChange(clampZoom(zoom + ZOOM_STEP))}
          aria-label={t("editor.toolbar.zoomIn")}
        >
          +
        </button>
      </div>

      <div className="editor-toolbar__group">
        <button type="button" onClick={onTogglePreview} aria-pressed={previewMode}>
          {previewMode ? t("editor.toolbar.backToEditing") : t("editor.toolbar.preview")}
        </button>
        <button type="button" className="btn btn-primary" onClick={onSave} disabled={saving}>
          {saving ? t("editor.toolbar.saving") : t("editor.toolbar.save")}
        </button>
        {onPublish && (
          <button type="button" onClick={onPublish} disabled={saving}>
            {t("editor.toolbar.publish")}
          </button>
        )}
        {saveError && <span role="alert">{saveError}</span>}
      </div>
    </div>
  );
}
