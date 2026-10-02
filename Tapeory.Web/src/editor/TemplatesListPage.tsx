import { useEffect, useMemo, useRef, useState, type CSSProperties } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import {
  deleteTemplate,
  duplicateTemplate,
  importLbxTemplate,
  importTemplateFile,
  templateFileUrl,
  listTemplates,
  renameTemplateGroup,
  templateThumbnailUrl,
  updateTemplateMetadata,
  type TemplateSummaryResponse,
} from "../api/templates";
import { useNotifications } from "../notifications/NotificationsContext";
import { useAuth } from "../auth/AuthContext";
import { collectGroups, groupHue, groupKey, normalizeText, type GroupSummary } from "./groups";
import { formatRelativeTime } from "../relativeTime";
import { DataGrid, DataGridRow, useUrlSort } from "../components/DataGrid";
import { CopyIcon, DownloadIcon, ImportedFileIcon, PencilIcon, PrinterIcon, TagIcon, TrashIcon } from "../components/icons";
import {
  isSortKey,
  sortTemplates,
  TEMPLATE_SORT_DESCENDING_FIRST,
  type SortKey,
  type TemplateSort,
} from "./templateSort";
import "./templates.css";

type ViewMode = "cards" | "list";

const VIEW_STORAGE_KEY = "tapeory.templatesView";
const UNGROUPED = "__none";
const GROUP_DATALIST_ID = "template-group-options";

/** Which templates to list once there are accounts: your own, public ones, or all you can see. */
type OwnershipFilter = "mine" | "public" | "";
const OWNERSHIP_FILTERS: OwnershipFilter[] = ["", "mine", "public"];

/** Older servers don't send canEdit: everything was editable then. */
const canEdit = (template: TemplateSummaryResponse) => template.canEdit !== false;

/** Private or public, and whose it is (only once there are accounts). */
function AccessBadge({ template }: { template: TemplateSummaryResponse }) {
  const { t } = useTranslation();
  const { hasUsers } = useAuth();

  if (!hasUsers) return null;

  const owner = template.isMine
    ? null
    : template.ownerName
      ? t("templates.byOwner", { name: template.ownerName })
      : t("templates.shared");

  return (
    <span className={`template-card__chip template-access template-access--${template.isPublic ? "public" : "private"}`}>
      {template.isPublic ? t("templates.public") : t("templates.private")}
      {owner && <span className="template-access__owner"> · {owner}</span>}
    </span>
  );
}

function readStoredView(): ViewMode {
  try {
    return localStorage.getItem(VIEW_STORAGE_KEY) === "list" ? "list" : "cards";
  } catch {
    return "cards";
  }
}

function hueStyle(group: string): CSSProperties {
  return { "--group-hue": groupHue(group) } as CSSProperties;
}

function StatusPill({ status }: { status: string }) {
  const { t } = useTranslation();
  return (
    <span className={`status-pill ${status === "Published" ? "status-pill--success" : "status-pill--neutral"}`}>
      {t(`templates.status.${status.toLowerCase()}`, { defaultValue: status })}
    </span>
  );
}

function GroupChip({ group }: { group: string }) {
  return (
    <span className="group-chip" style={hueStyle(group)}>
      {group}
    </span>
  );
}

function TemplatePreview({ template }: { template: TemplateSummaryResponse }) {
  const { t } = useTranslation();
  const [state, setState] = useState<"loading" | "loaded" | "error">("loading");

  return (
    <Link
      to={`/templates/${template.id}/edit`}
      className="template-card__preview"
      aria-hidden="true"
      tabIndex={-1}
    >
      {state !== "error" && (
        <img
          src={templateThumbnailUrl(template.id, template.currentVersionNumber)}
          alt=""
          loading="lazy"
          className={state === "loaded" ? "is-loaded" : undefined}
          onLoad={() => setState("loaded")}
          onError={() => setState("error")}
        />
      )}
      {state === "loading" && (
        <span
          className="template-card__skeleton"
          style={{ aspectRatio: `${template.widthMm} / ${template.heightMm}` }}
        />
      )}
      {state === "error" && <span className="template-card__no-preview">{t("templates.previewUnavailable")}</span>}
    </Link>
  );
}

/** Label height in the card corner — on tape printers this is the tape width to load (9, 12, 24 mm…). */
function HeightBadge({ heightMm }: { heightMm: number }) {
  const { t, i18n } = useTranslation();
  const height = new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 1 }).format(heightMm);

  return (
    <span className="template-card__height" aria-label={t("templates.heightBadgeLabel", { height })} title={t("templates.heightBadgeLabel", { height })}>
      <span aria-hidden="true">
        {height}
        <small>mm</small>
      </span>
    </span>
  );
}

interface GroupPickerProps {
  template: TemplateSummaryResponse;
  onSave: (group: string) => Promise<void>;
}

/** The group chip on a card; clicking it opens an inline field to move the template. */
function GroupPicker({ template, onSave }: GroupPickerProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [value, setValue] = useState(template.category ?? "");
  const [saving, setSaving] = useState(false);

  async function save(group: string) {
    setSaving(true);
    try {
      await onSave(group);
      setOpen(false);
    } catch {
      // The page already showed an error notification; keep the picker open to retry.
    } finally {
      setSaving(false);
    }
  }

  if (!open) {
    return (
      <button
        type="button"
        className={`group-chip group-chip--button${template.category ? "" : " group-chip--empty"}`}
        style={template.category ? hueStyle(template.category) : undefined}
        aria-label={
          template.category ? `${t("templates.changeGroup")}: ${template.category}` : t("templates.setGroup")
        }
        title={template.category ? t("templates.changeGroup") : t("templates.setGroup")}
        onClick={() => {
          setValue(template.category ?? "");
          setOpen(true);
        }}
      >
        {template.category ?? `+ ${t("templates.groupLabel")}`}
      </button>
    );
  }

  return (
    <form
      className="group-picker"
      onSubmit={(e) => {
        e.preventDefault();
        void save(value);
      }}
    >
      <input
        autoFocus
        list={GROUP_DATALIST_ID}
        aria-label={t("templates.groupLabel")}
        placeholder={t("templates.groupPlaceholder")}
        value={value}
        maxLength={100}
        disabled={saving}
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Escape") setOpen(false);
        }}
      />
      <div className="group-picker__actions">
        <button type="submit" className="btn btn-primary btn-sm" disabled={saving}>
          {t("common.save")}
        </button>
        {template.category && (
          <button type="button" className="btn btn-sm" disabled={saving} onClick={() => void save("")}>
            {t("templates.removeFromGroup")}
          </button>
        )}
        <button type="button" className="btn btn-sm" disabled={saving} onClick={() => setOpen(false)}>
          {t("common.cancel")}
        </button>
      </div>
    </form>
  );
}

interface DeleteTemplateButtonProps {
  template: TemplateSummaryResponse;
  deleting: boolean;
  onDelete: (template: TemplateSummaryResponse) => void;
}

function DeleteTemplateButton({ template, deleting, onDelete }: DeleteTemplateButtonProps) {
  const { t } = useTranslation();

  return (
    <button
      type="button"
      className="icon-link icon-link--danger"
      onClick={() => onDelete(template)}
      disabled={deleting}
      aria-label={`${t("common.delete")}: ${template.name}`}
      title={t("common.delete")}
    >
      <TrashIcon />
    </button>
  );
}

interface DuplicateTemplateButtonProps {
  template: TemplateSummaryResponse;
  duplicating: boolean;
  onDuplicate: (template: TemplateSummaryResponse) => void;
}

function DuplicateTemplateButton({ template, duplicating, onDuplicate }: DuplicateTemplateButtonProps) {
  const { t } = useTranslation();

  return (
    <button
      type="button"
      className="icon-link"
      onClick={() => onDuplicate(template)}
      disabled={duplicating}
      aria-label={`${t("templates.duplicate")}: ${template.name}`}
      title={t("templates.duplicate")}
    >
      <CopyIcon />
    </button>
  );
}

/** Downloads the template as a ".tapeory" file, to keep or to upload to another Tapeory. */
function DownloadTemplateLink({ template }: { template: TemplateSummaryResponse }) {
  const { t } = useTranslation();

  return (
    <a
      className="icon-link"
      href={templateFileUrl(template.id)}
      download
      aria-label={`${t("templates.download")}: ${template.name}`}
      title={t("templates.download")}
    >
      <DownloadIcon />
    </a>
  );
}

interface TemplateCardProps {
  template: TemplateSummaryResponse;
  index: number;
  onChangeGroup: (template: TemplateSummaryResponse, group: string) => Promise<void>;
  deleting: boolean;
  onDelete: (template: TemplateSummaryResponse) => void;
  duplicating: boolean;
  onDuplicate: (template: TemplateSummaryResponse) => void;
}

function TemplateCard({ template, index, onChangeGroup, deleting, onDelete, duplicating, onDuplicate }: TemplateCardProps) {
  const { t, i18n } = useTranslation();

  return (
    <article className="template-card" style={{ animationDelay: `${Math.min(index, 11) * 50}ms` }}>
      <TemplatePreview template={template} />
      <HeightBadge heightMm={template.heightMm} />

      <div className="template-card__body">
        <div className="template-card__title-row">
          <h3>
            <Link to={`/templates/${template.id}/edit`}>{template.name}</Link>
          </h3>
          <StatusPill status={template.status} />
        </div>

        <div className="template-card__meta">
          {canEdit(template) ? (
            <GroupPicker template={template} onSave={(group) => onChangeGroup(template, group)} />
          ) : (
            template.category && <GroupChip group={template.category} />
          )}
          <AccessBadge template={template} />
          <span className="template-card__chip">
            {template.widthMm}×{template.heightMm}mm
          </span>
          <span className="template-card__chip">
            {template.sourceLbxUrl ? t("templates.sourceImported") : t("templates.sourceNative")}
          </span>
        </div>

        <div className="template-card__footer">
          <span className="template-card__updated">
            {t("templates.updatedOn", {
              date: new Date(template.updatedAt).toLocaleDateString(i18n.language),
            })}
          </span>
          <div className="template-card__actions">
            {canEdit(template) && <DeleteTemplateButton template={template} deleting={deleting} onDelete={onDelete} />}
            <DuplicateTemplateButton template={template} duplicating={duplicating} onDuplicate={onDuplicate} />
            <DownloadTemplateLink template={template} />
            {canEdit(template) && (
              <Link className="btn btn-sm" to={`/templates/${template.id}/edit`}>
                {t("common.edit")}
              </Link>
            )}
            <Link className="btn btn-primary btn-sm" to={`/templates/${template.id}/print`}>
              {t("templates.print")}
            </Link>
          </div>
        </div>
      </div>
    </article>
  );
}

interface GroupSectionHeaderProps {
  group: GroupSummary | null;
  count: number;
  onRename: (group: GroupSummary, newName: string) => Promise<void>;
}

function GroupSectionHeader({ group, count, onRename }: GroupSectionHeaderProps) {
  const { t } = useTranslation();
  const [renaming, setRenaming] = useState(false);
  const [value, setValue] = useState(group?.name ?? "");
  const [saving, setSaving] = useState(false);

  if (group && renaming) {
    return (
      <form
        className="group-section__header"
        onSubmit={async (e) => {
          e.preventDefault();
          setSaving(true);
          try {
            await onRename(group, value);
            setRenaming(false);
          } catch {
            // The page already showed an error notification; keep the field open to retry.
          } finally {
            setSaving(false);
          }
        }}
      >
        <span className="group-dot" style={hueStyle(group.name)} />
        <input
          autoFocus
          className="group-section__rename-input"
          aria-label={t("templates.renameGroupLabel", { group: group.name })}
          value={value}
          maxLength={100}
          disabled={saving}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Escape") setRenaming(false);
          }}
        />
        <button type="submit" className="btn btn-primary btn-sm" disabled={saving || !value.trim()}>
          {t("common.save")}
        </button>
        <button type="button" className="btn btn-sm" disabled={saving} onClick={() => setRenaming(false)}>
          {t("common.cancel")}
        </button>
      </form>
    );
  }

  return (
    <div className="group-section__header">
      {group ? <span className="group-dot" style={hueStyle(group.name)} /> : <span className="group-dot group-dot--none" />}
      <h3>{group ? group.name : t("templates.ungrouped")}</h3>
      <span className="group-section__count">{count}</span>
      {group && (
        <button
          type="button"
          className="group-section__rename"
          aria-label={`${t("templates.renameGroup")}: ${group.name}`}
          title={t("templates.renameGroup")}
          onClick={() => {
            setValue(group.name);
            setRenaming(true);
          }}
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M12 20h9" />
            <path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z" />
          </svg>
        </button>
      )}
    </div>
  );
}

const TABLE_COLUMNS: { key: SortKey; label: string }[] = [
  { key: "name", label: "templates.columnName" },
  { key: "group", label: "templates.columnCategory" },
  { key: "status", label: "templates.columnStatus" },
  { key: "source", label: "templates.columnSource" },
  { key: "size", label: "templates.columnSize" },
  { key: "updated", label: "templates.columnUpdated" },
];

/** Small live preview in the name cell; falls back to a neutral tile if rendering fails. */
function RowThumb({ template }: { template: TemplateSummaryResponse }) {
  const [state, setState] = useState<"loading" | "loaded" | "error">("loading");

  return (
    <span className={`data-grid__thumb is-${state}`} aria-hidden="true">
      {state !== "error" && (
        <img
          src={templateThumbnailUrl(template.id, template.currentVersionNumber)}
          alt=""
          loading="lazy"
          onLoad={() => setState("loaded")}
          onError={() => setState("error")}
        />
      )}
    </span>
  );
}

interface TemplatesTableProps {
  templates: TemplateSummaryResponse[];
  sort: TemplateSort | null;
  onSortChange: (sort: TemplateSort | null) => void;
  deletingId: number | null;
  onDelete: (template: TemplateSummaryResponse) => void;
  duplicatingId: number | null;
  onDuplicate: (template: TemplateSummaryResponse) => void;
}

function TemplatesTable({
  templates,
  sort,
  onSortChange,
  deletingId,
  onDelete,
  duplicatingId,
  onDuplicate,
}: TemplatesTableProps) {
  const { t, i18n } = useTranslation();
  const rows = useMemo(
    () => (sort ? sortTemplates(templates, sort.key, sort.direction, i18n.language) : templates),
    [templates, sort, i18n.language],
  );
  const columns = TABLE_COLUMNS.map((column) => ({ key: column.key, label: t(column.label) }));
  const numberFormat = new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 1 });
  const dateFormat = new Intl.DateTimeFormat(i18n.language, { dateStyle: "medium", timeStyle: "short" });

  return (
    <DataGrid
      columns={columns}
      sort={sort}
      onSortChange={onSortChange}
      descendingFirst={TEMPLATE_SORT_DESCENDING_FIRST}
      hasActions
      footer={t("templates.gridCount", { count: rows.length })}
    >
      {rows.map((template) => {
        const imported = !!template.sourceLbxUrl;
        const editUrl = `/templates/${template.id}/edit`;
        const height = numberFormat.format(template.heightMm);
        return (
          <DataGridRow key={template.id} href={editUrl}>
            <td className="data-grid__primary data-grid__col--name">
              <span className="data-grid__name">
                <RowThumb template={template} />
                <span className="data-grid__title">
                  <Link to={editUrl}>{template.name}</Link>
                  <span className="data-grid__subtitle">
                    {template.description || t("templates.versionShort", { version: template.currentVersionNumber })}
                  </span>
                  <AccessBadge template={template} />
                </span>
              </span>
            </td>
            <td className="data-grid__col--group">
              {template.category ? (
                <GroupChip group={template.category} />
              ) : (
                <span className="data-grid__empty">{t("templates.ungrouped")}</span>
              )}
            </td>
            <td className="data-grid__col--status">
              <StatusPill status={template.status} />
            </td>
            <td className="data-grid__col--source data-grid__hide-mobile">
              <span className="data-grid__with-icon data-grid__muted">
                {imported ? <ImportedFileIcon /> : <TagIcon />}
                {imported ? t("templates.sourceImported") : t("templates.sourceNative")}
              </span>
            </td>
            <td className="data-grid__col--size">
              <span className="data-grid__size">
                {numberFormat.format(template.widthMm)}
                <span className="data-grid__times">×</span>
                <span className="data-grid__height" title={t("templates.heightBadgeLabel", { height })}>
                  {height}
                </span>
                <small>mm</small>
              </span>
            </td>
            <td className="data-grid__col--updated data-grid__end">
              <time
                className="data-grid__muted"
                dateTime={template.updatedAt}
                title={dateFormat.format(new Date(template.updatedAt))}
              >
                {formatRelativeTime(template.updatedAt, i18n.language)}
              </time>
            </td>
            <td className="data-grid__actions-cell">
              <span className="data-grid__actions">
                {canEdit(template) && (
                  <Link
                    to={editUrl}
                    className="icon-link"
                    aria-label={`${t("common.edit")}: ${template.name}`}
                    title={t("common.edit")}
                  >
                    <PencilIcon />
                  </Link>
                )}
                <Link
                  to={`/templates/${template.id}/print`}
                  className="icon-link icon-link--primary"
                  aria-label={t("templates.print")}
                  title={t("templates.print")}
                >
                  <PrinterIcon />
                </Link>
                <DuplicateTemplateButton
                  template={template}
                  duplicating={duplicatingId === template.id}
                  onDuplicate={onDuplicate}
                />
                <DownloadTemplateLink template={template} />
                {canEdit(template) && (
                  <DeleteTemplateButton template={template} deleting={deletingId === template.id} onDelete={onDelete} />
                )}
              </span>
            </td>
          </DataGridRow>
        );
      })}
    </DataGrid>
  );
}

export function TemplatesListPage() {
  const { t } = useTranslation();
  const { notify } = useNotifications();
  const navigate = useNavigate();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [templates, setTemplates] = useState<TemplateSummaryResponse[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [importError, setImportError] = useState<string | null>(null);
  const [importing, setImporting] = useState(false);
  const [deletingId, setDeletingId] = useState<number | null>(null);
  const [duplicatingId, setDuplicatingId] = useState<number | null>(null);
  const [view, setView] = useState<ViewMode>(readStoredView);
  const [searchParams, setSearchParams] = useSearchParams();
  const query = searchParams.get("q") ?? "";
  const groupFilter = searchParams.get("group") ?? "";
  const [tableSort, setSort] = useUrlSort(isSortKey);
  const { hasUsers } = useAuth();
  const showParam = searchParams.get("show") ?? "";
  const ownership: OwnershipFilter = hasUsers && (showParam === "mine" || showParam === "public") ? showParam : "";

  // Mine / public narrows everything below it, groups and their counts included.
  const ownedTemplates = useMemo(
    () =>
      templates?.filter((template) =>
        ownership === "mine" ? template.isMine : ownership === "public" ? template.isPublic : true,
      ) ?? null,
    [templates, ownership],
  );

  const groups = useMemo(() => collectGroups(ownedTemplates ?? []), [ownedTemplates]);
  const ungroupedCount = useMemo(
    () => (ownedTemplates ?? []).filter((template) => !template.category?.trim()).length,
    [ownedTemplates],
  );

  const filteredTemplates = useMemo(() => {
    if (!ownedTemplates) return null;
    const needle = normalizeText(query.trim());

    return ownedTemplates.filter((template) => {
      if (needle && !normalizeText(template.name).includes(needle)) return false;
      if (groupFilter === UNGROUPED) return !template.category?.trim();
      if (groupFilter) return !!template.category && groupKey(template.category) === groupFilter;
      return true;
    });
  }, [ownedTemplates, query, groupFilter]);

  useEffect(() => {
    let cancelled = false;

    listTemplates()
      .then((results) => {
        if (!cancelled) setTemplates(results);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : t("templates.loadErrorFallback"));
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function updateParams(changes: Record<string, string>) {
    const next = new URLSearchParams(searchParams);
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value);
      else next.delete(key);
    }
    setSearchParams(next, { replace: true });
  }

  const setQuery = (value: string) => updateParams({ q: value });
  const setGroupFilter = (value: string) => updateParams({ group: value });


  function changeView(next: ViewMode) {
    setView(next);
    try {
      localStorage.setItem(VIEW_STORAGE_KEY, next);
    } catch {
      // Remembering the view is a convenience only.
    }
  }

  async function handleChangeGroup(template: TemplateSummaryResponse, group: string) {
    try {
      const updated = await updateTemplateMetadata(template.id, {
        name: template.name,
        description: template.description,
        category: group.trim() || null,
        tags: template.tags,
      });
      setTemplates((prev) =>
        prev?.map((item) => (item.id === template.id ? { ...item, category: updated.category } : item)) ?? prev,
      );
      notify(
        updated.category ? t("templates.groupUpdated", { group: updated.category }) : t("templates.groupRemoved"),
        "success",
      );
    } catch (err) {
      notify(err instanceof Error ? err.message : t("templates.groupSaveErrorFallback"), "error");
      throw err;
    }
  }

  async function handleRenameGroup(group: GroupSummary, newName: string) {
    try {
      await renameTemplateGroup(group.name, newName);
      const renamed = newName.trim() || null;
      setTemplates((prev) =>
        prev?.map((item) =>
          item.category && groupKey(item.category) === group.key ? { ...item, category: renamed } : item,
        ) ?? prev,
      );
      if (groupFilter === group.key) {
        setGroupFilter(renamed ? groupKey(renamed) : "");
      }
      notify(t("templates.groupRenamed"), "success");
    } catch (err) {
      notify(err instanceof Error ? err.message : t("templates.groupSaveErrorFallback"), "error");
      throw err;
    }
  }

  async function handleDelete(template: TemplateSummaryResponse) {
    if (!confirm(t("templates.confirmDelete", { name: template.name }))) return;

    setDeletingId(template.id);

    try {
      await deleteTemplate(template.id);
      const remaining = (templates ?? []).filter((item) => item.id !== template.id);
      setTemplates(remaining);
      // Don't leave the list filtered to a group that no longer has any templates.
      if (groupFilter && groupFilter !== UNGROUPED && !remaining.some((item) => item.category && groupKey(item.category) === groupFilter)) {
        setGroupFilter("");
      }
      notify(t("templates.deleted", { name: template.name }), "success");
    } catch (err) {
      notify(err instanceof Error ? err.message : t("templates.deleteErrorFallback"), "error");
    } finally {
      setDeletingId(null);
    }
  }

  async function handleDuplicate(template: TemplateSummaryResponse) {
    setDuplicatingId(template.id);

    try {
      const copy = await duplicateTemplate(template.id, t("templates.copyName", { name: template.name }));
      // Newest first, like the list the server returns.
      setTemplates((prev) => (prev ? [copy, ...prev] : [copy]));
      notify(t("templates.duplicated", { name: copy.name }), "success");
    } catch (err) {
      notify(err instanceof Error ? err.message : t("templates.duplicateErrorFallback"), "error");
    } finally {
      setDuplicatingId(null);
    }
  }

  async function handleImportFile(file: File) {
    setImporting(true);
    setImportError(null);

    try {
      // A P-touch Editor label is converted; anything else is taken as a Tapeory template file.
      const created = /\.lbx$/i.test(file.name) ? await importLbxTemplate(file) : await importTemplateFile(file);
      navigate(`/templates/${created.id}/edit`);
    } catch (err) {
      setImportError(err instanceof Error ? err.message : t("templates.importErrorFallback"));
      setImporting(false);
    }
  }

  // Cards are split into one section per group, but only when showing every group — a group
  // filter already narrows things down to one.
  const sections = useMemo(() => {
    if (!filteredTemplates || groupFilter || groups.length === 0) return null;

    const result = groups
      .map((group) => ({
        group: group as GroupSummary | null,
        key: group.key,
        items: filteredTemplates.filter((item) => item.category && groupKey(item.category) === group.key),
      }))
      .filter((section) => section.items.length > 0);

    const ungrouped = filteredTemplates.filter((item) => !item.category?.trim());
    if (ungrouped.length > 0) {
      result.push({ group: null, key: UNGROUPED, items: ungrouped });
    }

    return result;
  }, [filteredTemplates, groupFilter, groups]);

  let cardIndex = 0;

  return (
    <section className="page-enter">
      <div className="page-header">
        <h2>{t("templates.title")}</h2>
        <div className="page-header__actions">
          <div className="view-toggle" role="group" aria-label={t("templates.viewLabel")}>
            <button
              type="button"
              aria-pressed={view === "cards"}
              onClick={() => changeView("cards")}
            >
              {t("templates.viewCards")}
            </button>
            <button type="button" aria-pressed={view === "list"} onClick={() => changeView("list")}>
              {t("templates.viewList")}
            </button>
          </div>
          <button
            type="button"
            onClick={() => fileInputRef.current?.click()}
            disabled={importing}
            title={t("templates.importHint")}
          >
            {importing ? t("templates.importing") : t("templates.import")}
          </button>
          <input
            ref={fileInputRef}
            type="file"
            accept=".tapeory,.lbx,.json"
            data-testid="template-file-input"
            hidden
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (file) {
                void handleImportFile(file);
              }
              e.target.value = "";
            }}
          />
          <Link className="btn btn-primary" to="/templates/new">
            {t("templates.newTemplate")}
          </Link>
        </div>
      </div>

      <datalist id={GROUP_DATALIST_ID}>
        {groups.map((group) => (
          <option key={group.key} value={group.name} />
        ))}
      </datalist>

      {templates && templates.length > 0 && (
        <div className="template-search">
          <svg
            className="template-search__icon"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            aria-hidden="true"
          >
            <circle cx="11" cy="11" r="7" />
            <path d="M20 20l-3.5-3.5" />
          </svg>
          <input
            type="search"
            aria-label={t("templates.searchLabel")}
            placeholder={t("templates.searchPlaceholder")}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Escape") setQuery("");
            }}
          />
          {query && (
            <button
              type="button"
              className="template-search__clear"
              aria-label={t("templates.clearSearch")}
              onClick={() => setQuery("")}
            >
              ×
            </button>
          )}
        </div>
      )}

      {hasUsers && templates && templates.length > 0 && (
        <div className="view-toggle template-ownership" role="group" aria-label={t("templates.ownershipLabel")}>
          {OWNERSHIP_FILTERS.map((filter) => (
            <button
              key={filter || "all"}
              type="button"
              aria-pressed={ownership === filter}
              onClick={() => updateParams({ show: filter, group: "" })}
            >
              {t(`templates.ownership.${filter || "all"}`)}
            </button>
          ))}
        </div>
      )}

      {groups.length > 0 && (
        <div className="group-filter" role="group" aria-label={t("templates.groupFilterLabel")}>
          <button
            type="button"
            aria-pressed={!groupFilter}
            aria-label={`${t("templates.allGroups")} (${ownedTemplates?.length ?? 0})`}
            onClick={() => setGroupFilter("")}
          >
            {t("templates.allGroups")}
            <span className="group-filter__count">{ownedTemplates?.length ?? 0}</span>
          </button>
          {groups.map((group) => (
            <button
              key={group.key}
              type="button"
              aria-pressed={groupFilter === group.key}
              aria-label={`${group.name} (${group.count})`}
              style={hueStyle(group.name)}
              onClick={() => setGroupFilter(groupFilter === group.key ? "" : group.key)}
            >
              <span className="group-dot" />
              {group.name}
              <span className="group-filter__count">{group.count}</span>
            </button>
          ))}
          {ungroupedCount > 0 && (
            <button
              type="button"
              aria-pressed={groupFilter === UNGROUPED}
              aria-label={`${t("templates.ungrouped")} (${ungroupedCount})`}
              onClick={() => setGroupFilter(groupFilter === UNGROUPED ? "" : UNGROUPED)}
            >
              <span className="group-dot group-dot--none" />
              {t("templates.ungrouped")}
              <span className="group-filter__count">{ungroupedCount}</span>
            </button>
          )}
        </div>
      )}

      {importError && <p role="alert">{importError}</p>}
      {error && <p role="alert">{error}</p>}
      {!error && !templates && <p>{t("templates.loading")}</p>}
      {templates && templates.length === 0 && <p>{t("templates.empty")}</p>}

      {filteredTemplates && templates && templates.length > 0 && filteredTemplates.length === 0 && (
        <div className="template-search__empty">
          <p>{t("templates.noMatches", { query: query.trim() })}</p>
          <button
            type="button"
            onClick={() => updateParams({ q: "", group: "" })}
          >
            {t("templates.clearSearch")}
          </button>
        </div>
      )}

      {filteredTemplates && filteredTemplates.length > 0 && view === "cards" && sections && (
        <div className="group-sections">
          {sections.map((section) => (
            <section key={section.key} className="group-section">
              <GroupSectionHeader group={section.group} count={section.items.length} onRename={handleRenameGroup} />
              <div className="template-grid">
                {section.items.map((template) => (
                  <TemplateCard
                    key={template.id}
                    template={template}
                    index={cardIndex++}
                    onChangeGroup={handleChangeGroup}
                    deleting={deletingId === template.id}
                    onDelete={(item) => void handleDelete(item)}
                    duplicating={duplicatingId === template.id}
                    onDuplicate={(item) => void handleDuplicate(item)}
                  />
                ))}
              </div>
            </section>
          ))}
        </div>
      )}

      {filteredTemplates && filteredTemplates.length > 0 && view === "cards" && !sections && (
        <div className="template-grid">
          {filteredTemplates.map((template, index) => (
            <TemplateCard
              key={template.id}
              template={template}
              index={index}
              onChangeGroup={handleChangeGroup}
              deleting={deletingId === template.id}
              onDelete={(item) => void handleDelete(item)}
              duplicating={duplicatingId === template.id}
              onDuplicate={(item) => void handleDuplicate(item)}
            />
          ))}
        </div>
      )}

      {filteredTemplates && filteredTemplates.length > 0 && view === "list" && (
        <TemplatesTable
          templates={filteredTemplates}
          sort={tableSort}
          onSortChange={setSort}
          deletingId={deletingId}
          onDelete={(item) => void handleDelete(item)}
          duplicatingId={duplicatingId}
          onDuplicate={(item) => void handleDuplicate(item)}
        />
      )}
    </section>
  );
}
