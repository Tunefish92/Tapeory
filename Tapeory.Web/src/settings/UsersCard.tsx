import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import {
  createUser,
  deleteUser,
  listUsers,
  resetUserPassword,
  updateUser,
  type TemporaryPasswordResponse,
  type UserResponse,
  type UserRole,
} from "../api/auth";
import { useAuth } from "../auth/AuthContext";
import { formatRelativeTime } from "../relativeTime";

const ROLES: UserRole[] = ["User", "Admin"];

interface EditState {
  id: number;
  displayName: string;
  role: UserRole;
  disabled: boolean;
}

/** Accounts, for administrators: add, edit role or name, disable, reset password, delete. */
export function UsersCard({ icon }: { icon: ReactNode }) {
  const { t, i18n } = useTranslation();
  const { user: me, refresh: refreshMe } = useAuth();
  const [users, setUsers] = useState<UserResponse[] | null>(null);
  const [editing, setEditing] = useState<EditState | null>(null);
  const [temporary, setTemporary] = useState<TemporaryPasswordResponse | null>(null);
  const [deleting, setDeleting] = useState<{ user: UserResponse; templates: "transfer" | "delete" } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [newUserName, setNewUserName] = useState("");
  const [newDisplayName, setNewDisplayName] = useState("");
  const [newRole, setNewRole] = useState<UserRole>("User");

  const reload = useCallback(
    () =>
      listUsers()
        .then(setUsers)
        .catch((err: unknown) => setError(err instanceof Error ? err.message : t("settings.usersLoadError"))),
    [t],
  );

  useEffect(() => {
    void reload();
  }, [reload]);

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(null);

    try {
      await action();
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("settings.usersActionError"));
    } finally {
      setBusy(false);
    }
  }

  function handleCreate(e: FormEvent) {
    e.preventDefault();
    void run(async () => {
      const created = await createUser({ userName: newUserName.trim(), displayName: newDisplayName.trim(), role: newRole });
      setTemporary(created);
      setNewUserName("");
      setNewDisplayName("");
      setNewRole("User");
    });
  }

  function handleSave(e: FormEvent) {
    e.preventDefault();
    if (!editing) return;
    void run(async () => {
      await updateUser(editing.id, { displayName: editing.displayName.trim(), role: editing.role, disabled: editing.disabled });
      setEditing(null);
      if (editing.id === me?.id) await refreshMe();
    });
  }

  function handleReset(user: UserResponse) {
    if (!confirm(t("settings.usersResetConfirm", { name: user.displayName }))) return;
    void run(async () => setTemporary(await resetUserPassword(user.id)));
  }

  function handleDelete() {
    if (!deleting) return;
    const { user, templates } = deleting;
    void run(async () => {
      await deleteUser(user.id, templates);
      setDeleting(null);
    });
  }

  return (
    <div className="card settings-section settings-section--wide">
      <div className="settings-section__head">
        {icon}
        <h3>{t("settings.users")}</h3>
      </div>
      <p className="settings-backup__intro">{t("settings.usersIntro")}</p>

      {temporary && (
        <div className="settings-users__temporary" role="status">
          <p>{t("settings.usersTemporaryPassword", { name: temporary.user.userName })}</p>
          <code>{temporary.temporaryPassword}</code>
          <p className="settings-users__hint">{t("settings.usersTemporaryHint")}</p>
          <div className="settings-confirm__actions">
            <button
              type="button"
              className="btn btn-sm"
              onClick={() => void navigator.clipboard?.writeText(temporary.temporaryPassword)}
            >
              {t("settings.usersCopy")}
            </button>
            <button type="button" className="btn btn-sm" onClick={() => setTemporary(null)}>
              {t("common.close")}
            </button>
          </div>
        </div>
      )}

      {error && (
        <p className="settings-users__error" role="alert">
          {error}
        </p>
      )}

      {users === null ? (
        <p className="settings-backup__empty">{t("common.loading")}</p>
      ) : (
        <ul className="settings-users__list">
          {users.map((user) =>
            editing?.id === user.id ? (
              <li key={user.id} className="settings-users__item">
                <form className="settings-users__edit" onSubmit={handleSave}>
                  <label className="properties-field">
                    {t("settings.usersDisplayName")}
                    <input
                      value={editing.displayName}
                      onChange={(e) => setEditing({ ...editing, displayName: e.target.value })}
                      autoFocus
                    />
                  </label>
                  <label className="properties-field">
                    {t("settings.usersRole")}
                    <select
                      value={editing.role}
                      onChange={(e) => setEditing({ ...editing, role: e.target.value as UserRole })}
                      disabled={user.id === me?.id}
                    >
                      {ROLES.map((role) => (
                        <option key={role} value={role}>
                          {t(`auth.role${role}`)}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="settings-users__check">
                    <input
                      type="checkbox"
                      checked={editing.disabled}
                      onChange={(e) => setEditing({ ...editing, disabled: e.target.checked })}
                      disabled={user.id === me?.id}
                    />
                    {t("settings.usersDisabled")}
                  </label>
                  <div className="settings-confirm__actions">
                    <button type="submit" className="btn btn-primary btn-sm" disabled={busy}>
                      {t("common.save")}
                    </button>
                    <button type="button" className="btn btn-sm" disabled={busy} onClick={() => setEditing(null)}>
                      {t("common.cancel")}
                    </button>
                  </div>
                </form>
              </li>
            ) : (
              <li key={user.id} className="settings-users__item">
                <div className="settings-users__who">
                  <span className="settings-users__name">
                    {user.displayName}
                    {user.id === me?.id && <span className="settings-users__you">{t("settings.usersYou")}</span>}
                  </span>
                  <span className="settings-users__meta">
                    {user.userName} ·{" "}
                    {user.lastLoginAt
                      ? t("settings.usersLastLogin", { when: formatRelativeTime(user.lastLoginAt, i18n.language) })
                      : t("settings.usersNeverSignedIn")}
                  </span>
                </div>
                <div className="settings-users__badges">
                  <span className={`status-pill ${user.role === "Admin" ? "status-pill--info" : "status-pill--neutral"}`}>
                    {t(`auth.role${user.role}`)}
                  </span>
                  {user.disabled && <span className="status-pill status-pill--danger">{t("settings.usersDisabled")}</span>}
                  {!user.disabled && user.mustChangePassword && (
                    <span className="status-pill status-pill--neutral">{t("settings.usersTemporary")}</span>
                  )}
                </div>
                <div className="settings-users__actions">
                  <button
                    type="button"
                    className="btn btn-sm"
                    disabled={busy}
                    onClick={() =>
                      setEditing({ id: user.id, displayName: user.displayName, role: user.role, disabled: user.disabled })
                    }
                  >
                    {t("common.edit")}
                  </button>
                  {user.id !== me?.id && (
                    <>
                      <button type="button" className="btn btn-sm" disabled={busy} onClick={() => handleReset(user)}>
                        {t("settings.usersResetPassword")}
                      </button>
                      <button
                        type="button"
                        className="btn btn-danger btn-sm"
                        disabled={busy}
                        onClick={() => setDeleting({ user, templates: "transfer" })}
                      >
                        {t("common.delete")}
                      </button>
                    </>
                  )}
                </div>
                {deleting?.user.id === user.id && (
                  <div className="settings-confirm settings-users__delete" role="group" aria-label={t("settings.usersDeleteTitle", { name: user.displayName })}>
                    <p>{t("settings.usersDeleteConfirm", { name: user.displayName })}</p>
                    <fieldset className="settings-users__choice">
                      <legend>{t("settings.usersDeleteTemplates")}</legend>
                      <label>
                        <input
                          type="radio"
                          name="delete-templates"
                          checked={deleting.templates === "transfer"}
                          onChange={() => setDeleting({ ...deleting, templates: "transfer" })}
                        />
                        {t("settings.usersDeleteTransfer")}
                      </label>
                      <label>
                        <input
                          type="radio"
                          name="delete-templates"
                          checked={deleting.templates === "delete"}
                          onChange={() => setDeleting({ ...deleting, templates: "delete" })}
                        />
                        {t("settings.usersDeleteTemplatesToo")}
                      </label>
                    </fieldset>
                    <div className="settings-confirm__actions">
                      <button type="button" className="btn btn-danger btn-sm settings-confirm__go" disabled={busy} onClick={handleDelete}>
                        {t("settings.usersDeleteAction")}
                      </button>
                      <button type="button" className="btn btn-sm" disabled={busy} onClick={() => setDeleting(null)}>
                        {t("common.cancel")}
                      </button>
                    </div>
                  </div>
                )}
              </li>
            ),
          )}
        </ul>
      )}

      <form className="settings-users__add" onSubmit={handleCreate} aria-label={t("settings.usersAdd")}>
        <label className="properties-field">
          {t("settings.usersUserName")}
          <input
            value={newUserName}
            onChange={(e) => setNewUserName(e.target.value)}
            autoComplete="off"
            spellCheck={false}
            required
          />
        </label>
        <label className="properties-field">
          {t("settings.usersDisplayNameOptional")}
          <input value={newDisplayName} onChange={(e) => setNewDisplayName(e.target.value)} autoComplete="off" />
        </label>
        <label className="properties-field">
          {t("settings.usersRole")}
          <select value={newRole} onChange={(e) => setNewRole(e.target.value as UserRole)}>
            {ROLES.map((role) => (
              <option key={role} value={role}>
                {t(`auth.role${role}`)}
              </option>
            ))}
          </select>
        </label>
        <button type="submit" className="btn btn-primary" disabled={busy || !newUserName.trim()}>
          {t("settings.usersAdd")}
        </button>
      </form>
    </div>
  );
}
