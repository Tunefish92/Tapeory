import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { ChangePasswordForm } from "../auth/AuthPages";
import { useNotifications } from "../notifications/NotificationsContext";

/** The signed-in account in the header: change password, sign out. */
export function AccountMenu() {
  const { t } = useTranslation();
  const { user, signOut, refresh } = useAuth();
  const { notify } = useNotifications();
  const [open, setOpen] = useState(false);
  const [changingPassword, setChangingPassword] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;

    const close = (event: MouseEvent | KeyboardEvent) => {
      if (event instanceof KeyboardEvent ? event.key === "Escape" : !menuRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };

    document.addEventListener("mousedown", close);
    document.addEventListener("keydown", close);
    return () => {
      document.removeEventListener("mousedown", close);
      document.removeEventListener("keydown", close);
    };
  }, [open]);

  if (!user) return null;

  return (
    <div className="account-menu" ref={menuRef}>
      <button
        type="button"
        className="account-menu__toggle"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <span className="account-menu__avatar" aria-hidden="true">
          {user.displayName.slice(0, 1).toUpperCase()}
        </span>
        <span className="account-menu__name">{user.displayName}</span>
      </button>

      {open && (
        <div className="account-menu__popover" role="menu">
          <div className="account-menu__who">
            <strong>{user.displayName}</strong>
            <span>
              {user.userName} · {t(`auth.role${user.role}`)}
            </span>
          </div>
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setOpen(false);
              setChangingPassword(true);
            }}
          >
            {t("auth.changePassword")}
          </button>
          <button type="button" role="menuitem" onClick={() => void signOut()}>
            {t("auth.signOut")}
          </button>
        </div>
      )}

      {changingPassword && (
        <div className="modal-backdrop">
          <div className="card modal" role="dialog" aria-modal="true" aria-labelledby="change-password-title">
            <h3 id="change-password-title">{t("auth.changePassword")}</h3>
            <ChangePasswordForm
              onCancel={() => setChangingPassword(false)}
              onChanged={() => {
                setChangingPassword(false);
                notify(t("auth.passwordChanged"), "success");
                void refresh();
              }}
            />
          </div>
        </div>
      )}
    </div>
  );
}

/** Shown while Tapeory has no accounts: anyone on the network can use it. */
export function OpenAccessBanner() {
  const { t } = useTranslation();
  const { openAccess, startCreatingAccount } = useAuth();

  if (!openAccess) return null;

  return (
    <div className="open-access-banner" role="status">
      <div className="open-access-banner__inner">
        <span>{t("auth.openBanner")}</span>
        <button type="button" className="btn btn-sm" onClick={startCreatingAccount}>
          {t("auth.openBannerAction")}
        </button>
      </div>
    </div>
  );
}
