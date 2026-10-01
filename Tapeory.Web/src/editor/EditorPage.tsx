import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import {
  createTemplate,
  createTemplateVersion,
  duplicateTemplate,
  getTemplate,
  setTemplateVisibility,
  listTemplates,
  updateTemplateMetadata,
  uploadImage,
} from "../api/templates";
import {
  addObject,
  createDynamicFieldObject,
  createImageObject,
  createBarcodeObject,
  createEllipseObject,
  createLineObject,
  createRectObject,
  createTextObject,
  defaultFontFamily,
  duplicateObject,
  extractFields,
  fitIntoLabel,
  normalizeDocument,
  removeObject,
  reorderObject,
  updateObject,
  type ReorderDirection,
} from "./document";
import { listFonts } from "../api/fonts";
import { useHistory } from "./history";
import { LabelCanvas } from "./LabelCanvas";
import { PropertiesPanel } from "./PropertiesPanel";
import { Toolbar } from "./Toolbar";
import { createEmptyDocument, type LabelDocument, type LabelObjectPatch, type LabelObjectType } from "./types";
import { useKeyboardShortcuts } from "./useKeyboardShortcuts";
import { collectGroups } from "./groups";
import { LabelSizeFields } from "./LabelSizeFields";
import { useAuth } from "../auth/AuthContext";
import "./editor.css";

export function EditorPage() {
  const { t } = useTranslation();
  const params = useParams<{ id: string }>();
  const navigate = useNavigate();
  const templateId = params.id && params.id !== "new" ? Number(params.id) : null;

  // New labels start on the most common QL roll: DK-22210, 29 mm continuous.
  const history = useHistory<LabelDocument>({ ...createEmptyDocument(62, 29), media: "DK-22210" });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [zoom, setZoom] = useState(2);
  const [previewMode, setPreviewMode] = useState(false);

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [category, setCategory] = useState("");
  // Not editable here, but must be sent back on save: the update endpoint replaces tags wholesale.
  const [tags, setTags] = useState<string[]>([]);
  const [groupOptions, setGroupOptions] = useState<string[]>([]);

  const [loading, setLoading] = useState(templateId !== null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [imageError, setImageError] = useState<string | null>(null);

  const { hasUsers } = useAuth();
  // Who may do what with this template; a new one is yours.
  const [access, setAccess] = useState({ canEdit: true, isPublic: false, ownerName: null as string | null });
  const [accessBusy, setAccessBusy] = useState(false);
  const readOnly = !access.canEdit;

  const [sourceLbxUrl, setSourceLbxUrl] = useState<string | null>(null);
  const [conversionWarnings, setConversionWarnings] = useState<string[]>([]);
  const [warningsDismissed, setWarningsDismissed] = useState(false);
  // The server's fonts, to start new text in one that exists there.
  const [fontFamilies, setFontFamilies] = useState<string[] | null>(null);

  useEffect(() => {
    let cancelled = false;
    listFonts()
      .then((list) => {
        if (!cancelled) setFontFamilies(list);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, []);

  // Set right before handleSave navigates from /templates/new to /templates/:id/edit, so the
  // load effect below doesn't immediately refetch (and flash a loading screen over) the
  // document the user just saved.
  const skipNextLoadRef = useRef(false);

  useEffect(() => {
    if (templateId === null) {
      return;
    }

    if (skipNextLoadRef.current) {
      skipNextLoadRef.current = false;
      return;
    }

    let cancelled = false;
    setLoading(true);

    getTemplate(templateId)
      .then((detail) => {
        if (cancelled) return;

        setName(detail.name);
        setDescription(detail.description ?? "");
        setCategory(detail.category ?? "");
        setTags(detail.tags);
        setAccess({
          canEdit: detail.canEdit !== false,
          isPublic: detail.isPublic ?? true,
          ownerName: detail.ownerName ?? null,
        });
        setSourceLbxUrl(detail.sourceLbxUrl);
        setConversionWarnings(detail.conversionWarnings);
        setWarningsDismissed(false);

        const doc = normalizeDocument(JSON.parse(detail.currentVersion.editorJson) as LabelDocument);
        history.reset(doc);
        setLoadError(null);
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setLoadError(err instanceof Error ? err.message : t("editor.loadErrorFallback"));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [templateId]);

  useEffect(() => {
    listTemplates()
      .then((all) => setGroupOptions(collectGroups(all).map((group) => group.name)))
      .catch(() => {
        // Suggestions only; the field still accepts any group name.
      });
  }, []);

  // If undo/redo (or a load) leaves the selected object no longer present, clear selection.
  useEffect(() => {
    if (selectedId && !history.value.objects.some((object) => object.id === selectedId)) {
      setSelectedId(null);
    }
  }, [history.value, selectedId]);

  const selectedObject = history.value.objects.find((object) => object.id === selectedId) ?? null;

  function handleAddObject(type: Exclude<LabelObjectType, "image">) {
    const factory = {
      text: createTextObject,
      dynamicField: createDynamicFieldObject,
      rect: createRectObject,
      line: createLineObject,
      ellipse: createEllipseObject,
      barcode: createBarcodeObject,
    }[type];

    let created = factory();
    if (created.type === "text" || created.type === "dynamicField") {
      created = { ...created, fontFamily: defaultFontFamily(fontFamilies) };
    }
    const object = fitIntoLabel(created, history.value.widthMm, history.value.heightMm);
    history.set(addObject(history.value, object));
    setSelectedId(object.id);
  }

  async function handleAddImage(file: File) {
    setImageError(null);

    try {
      const uploaded = await uploadImage(file);
      const object = fitIntoLabel(
        createImageObject(uploaded.id, uploaded.url),
        history.value.widthMm,
        history.value.heightMm,
      );
      history.set(addObject(history.value, object));
      setSelectedId(object.id);
    } catch (err) {
      setImageError(err instanceof Error ? err.message : t("editor.imageErrorFallback"));
    }
  }

  function handleObjectChange(id: string, changes: LabelObjectPatch) {
    history.set(updateObject(history.value, id, changes));
  }

  function handleDeleteSelected() {
    if (!selectedId) return;
    history.set(removeObject(history.value, selectedId));
    setSelectedId(null);
  }

  function handleDuplicateSelected() {
    if (!selectedId) return;
    const updated = duplicateObject(history.value, selectedId);
    history.set(updated);
    setSelectedId(updated.objects.at(-1)?.id ?? null);
  }

  function handleReorder(direction: ReorderDirection) {
    if (!selectedId) return;
    history.set(reorderObject(history.value, selectedId, direction));
  }

  function handleNudge(dxMm: number, dyMm: number) {
    if (!selectedId || !selectedObject || selectedObject.locked) return;
    history.set(
      updateObject(history.value, selectedId, {
        x: selectedObject.x + dxMm,
        y: selectedObject.y + dyMm,
      }),
    );
  }

  async function handleSave() {
    if (!name.trim()) {
      setSaveError(t("editor.nameRequired"));
      return;
    }

    setSaving(true);
    setSaveError(null);

    const fields = extractFields(history.value);
    const editorJson = JSON.stringify(history.value);

    try {
      if (templateId !== null) {
        await createTemplateVersion(templateId, {
          widthMm: history.value.widthMm,
          heightMm: history.value.heightMm,
          editorJson,
          fields,
        });
        await updateTemplateMetadata(templateId, {
          name,
          description: description || null,
          category: category || null,
          tags,
        });
      } else {
        const created = await createTemplate({
          name,
          description: description || null,
          category: category || null,
          widthMm: history.value.widthMm,
          heightMm: history.value.heightMm,
          editorJson,
          fields,
        });
        skipNextLoadRef.current = true;
        navigate(`/templates/${created.id}/edit`, { replace: true });
      }
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : t("editor.saveErrorFallback"));
    } finally {
      setSaving(false);
    }
  }

  async function handlePublish() {
    if (templateId === null) return;

    setSaving(true);
    setSaveError(null);

    try {
      await updateTemplateMetadata(templateId, {
        name,
        description: description || null,
        category: category || null,
        tags,
        status: "Published",
      });
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : t("editor.publishErrorFallback"));
    } finally {
      setSaving(false);
    }
  }

  useKeyboardShortcuts({
    enabled: !previewMode && !loading && !readOnly,
    onUndo: history.undo,
    onRedo: history.redo,
    onDelete: handleDeleteSelected,
    onDuplicate: handleDuplicateSelected,
    onDeselect: () => setSelectedId(null),
    onNudge: handleNudge,
  });

  async function handleVisibility(isPublic: boolean) {
    if (templateId === null) return;
    setAccessBusy(true);
    setSaveError(null);

    try {
      const detail = await setTemplateVisibility(templateId, isPublic);
      setAccess({ canEdit: detail.canEdit !== false, isPublic: detail.isPublic ?? isPublic, ownerName: detail.ownerName ?? null });
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : t("editor.saveErrorFallback"));
    } finally {
      setAccessBusy(false);
    }
  }

  async function handleDuplicateToEdit() {
    if (templateId === null) return;
    setAccessBusy(true);

    try {
      const copy = await duplicateTemplate(templateId, t("templates.copyName", { name }));
      navigate(`/templates/${copy.id}/edit`);
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : t("editor.saveErrorFallback"));
    } finally {
      setAccessBusy(false);
    }
  }

  // Read-only: every object locked, so nothing can be dragged or resized.
  const shownDocument = readOnly
    ? { ...history.value, objects: history.value.objects.map((object) => ({ ...object, locked: true })) }
    : history.value;

  if (loading) {
    return <p>{t("editor.loading")}</p>;
  }

  if (loadError) {
    return <p role="alert">{loadError}</p>;
  }

  return (
    <div className="editor-page page-enter">
      <div className="editor-metadata">
        <label className="properties-field">
          {t("editor.metadataName")}
          <input value={name} onChange={(e) => setName(e.target.value)} disabled={readOnly} />
        </label>
        <label className="properties-field">
          {t("editor.metadataCategory")}
          <input
            value={category}
            list="editor-group-options"
            maxLength={100}
            placeholder={t("templates.groupPlaceholder")}
            onChange={(e) => setCategory(e.target.value)}
            disabled={readOnly}
          />
          <datalist id="editor-group-options">
            {groupOptions.map((group) => (
              <option key={group} value={group} />
            ))}
          </datalist>
        </label>
        <label className="properties-field">
          {t("editor.metadataDescription")}
          <input value={description} onChange={(e) => setDescription(e.target.value)} disabled={readOnly} />
        </label>
        {!readOnly && (
          <LabelSizeFields
            widthMm={history.value.widthMm}
            heightMm={history.value.heightMm}
            media={history.value.media}
            onChange={(patch) => history.set({ ...history.value, ...patch })}
          />
        )}
        {hasUsers && templateId !== null && !readOnly && (
          <div className="view-toggle editor-visibility" role="group" aria-label={t("editor.visibility")}>
            <button type="button" aria-pressed={!access.isPublic} disabled={accessBusy} onClick={() => void handleVisibility(false)}>
              {t("templates.private")}
            </button>
            <button type="button" aria-pressed={access.isPublic} disabled={accessBusy} onClick={() => void handleVisibility(true)}>
              {t("templates.public")}
            </button>
          </div>
        )}
        {templateId !== null && (
          <Link className="btn" to={`/templates/${templateId}/print`}>
            {t("editor.print")}
          </Link>
        )}
      </div>

      {readOnly && (
        <div className="editor-import-banner" role="status">
          <span>
            {access.ownerName
              ? t("editor.readOnlyOwned", { name: access.ownerName })
              : t("editor.readOnlyShared")}
          </span>
          <button type="button" className="btn btn-primary btn-sm" disabled={accessBusy} onClick={() => void handleDuplicateToEdit()}>
            {t("editor.duplicateToEdit")}
          </button>
        </div>
      )}

      {sourceLbxUrl && (
        <div className="editor-import-banner">
          <span>{t("editor.importedBanner")}</span>
          <a href={sourceLbxUrl}>{t("editor.downloadOriginal")}</a>
        </div>
      )}

      {conversionWarnings.length > 0 && !warningsDismissed && (
        <div className="editor-import-banner editor-import-banner--warning" role="alert">
          <div>
            <strong>{t("editor.warningsHeading", { count: conversionWarnings.length })}</strong>
            <ul>
              {conversionWarnings.map((warning) => (
                <li key={warning}>{warning}</li>
              ))}
            </ul>
          </div>
          <button type="button" onClick={() => setWarningsDismissed(true)}>
            {t("editor.dismiss")}
          </button>
        </div>
      )}

      <Toolbar
        onAddObject={handleAddObject}
        onAddImage={handleAddImage}
        onUndo={history.undo}
        onRedo={history.redo}
        canUndo={history.canUndo}
        canRedo={history.canRedo}
        zoom={zoom}
        onZoomChange={setZoom}
        previewMode={previewMode}
        onTogglePreview={() => {
          setPreviewMode((value) => !value);
          setSelectedId(null);
        }}
        onSave={handleSave}
        saving={saving}
        saveError={saveError}
        onPublish={templateId !== null ? handlePublish : undefined}
        readOnly={readOnly}
      />

      {imageError && <p role="alert">{imageError}</p>}

      <div className="editor-body">
        <div className="editor-canvas-scroll">
          <LabelCanvas
            document={shownDocument}
            selectedId={readOnly ? null : selectedId}
            previewMode={previewMode}
            zoom={zoom}
            onSelect={readOnly ? () => {} : setSelectedId}
            onChange={readOnly ? () => {} : handleObjectChange}
          />
        </div>

        {!previewMode && !readOnly && (
          <PropertiesPanel
            object={selectedObject}
            onChange={(changes) => selectedId && handleObjectChange(selectedId, changes)}
            onDelete={handleDeleteSelected}
            onDuplicate={handleDuplicateSelected}
            onReorder={handleReorder}
            fieldNames={extractFields(history.value).map((field) => field.name)}
          />
        )}
      </div>
    </div>
  );
}
