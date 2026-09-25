import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import {
  createTemplate,
  createTemplateVersion,
  getTemplate,
  listTemplates,
  updateTemplateMetadata,
  uploadImage,
} from "../api/templates";
import {
  addObject,
  createDynamicFieldObject,
  createImageObject,
  createLineObject,
  createRectObject,
  createTextObject,
  duplicateObject,
  extractFields,
  normalizeDocument,
  removeObject,
  reorderObject,
  updateObject,
  type ReorderDirection,
} from "./document";
import { useHistory } from "./history";
import { LabelCanvas } from "./LabelCanvas";
import { PropertiesPanel } from "./PropertiesPanel";
import { Toolbar } from "./Toolbar";
import { createEmptyDocument, type LabelDocument, type LabelObjectPatch, type LabelObjectType } from "./types";
import { useKeyboardShortcuts } from "./useKeyboardShortcuts";
import { collectGroups } from "./groups";
import { LabelSizeFields } from "./LabelSizeFields";
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

  const [sourceLbxUrl, setSourceLbxUrl] = useState<string | null>(null);
  const [conversionWarnings, setConversionWarnings] = useState<string[]>([]);
  const [warningsDismissed, setWarningsDismissed] = useState(false);

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
    }[type];

    const object = factory();
    history.set(addObject(history.value, object));
    setSelectedId(object.id);
  }

  async function handleAddImage(file: File) {
    setImageError(null);

    try {
      const uploaded = await uploadImage(file);
      const object = createImageObject(uploaded.id, uploaded.url);
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
    enabled: !previewMode && !loading,
    onUndo: history.undo,
    onRedo: history.redo,
    onDelete: handleDeleteSelected,
    onDuplicate: handleDuplicateSelected,
    onDeselect: () => setSelectedId(null),
    onNudge: handleNudge,
  });

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
          <input value={name} onChange={(e) => setName(e.target.value)} />
        </label>
        <label className="properties-field">
          {t("editor.metadataCategory")}
          <input
            value={category}
            list="editor-group-options"
            maxLength={100}
            placeholder={t("templates.groupPlaceholder")}
            onChange={(e) => setCategory(e.target.value)}
          />
          <datalist id="editor-group-options">
            {groupOptions.map((group) => (
              <option key={group} value={group} />
            ))}
          </datalist>
        </label>
        <label className="properties-field">
          {t("editor.metadataDescription")}
          <input value={description} onChange={(e) => setDescription(e.target.value)} />
        </label>
        <LabelSizeFields
          widthMm={history.value.widthMm}
          heightMm={history.value.heightMm}
          media={history.value.media}
          onChange={(patch) => history.set({ ...history.value, ...patch })}
        />
        {templateId !== null && (
          <Link className="btn" to={`/templates/${templateId}/print`}>
            {t("editor.print")}
          </Link>
        )}
      </div>

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
      />

      {imageError && <p role="alert">{imageError}</p>}

      <div className="editor-body">
        <div className="editor-canvas-scroll">
          <LabelCanvas
            document={history.value}
            selectedId={selectedId}
            previewMode={previewMode}
            zoom={zoom}
            onSelect={setSelectedId}
            onChange={handleObjectChange}
          />
        </div>

        {!previewMode && (
          <PropertiesPanel
            object={selectedObject}
            onChange={(changes) => selectedId && handleObjectChange(selectedId, changes)}
            onDelete={handleDeleteSelected}
            onDuplicate={handleDuplicateSelected}
            onReorder={handleReorder}
          />
        )}
      </div>
    </div>
  );
}
