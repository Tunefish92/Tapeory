import { createContext, useCallback, useContext, useRef, useState, type PropsWithChildren } from "react";
import "./notifications.css";

export type NotificationKind = "success" | "error" | "info";

interface Notification {
  id: number;
  kind: NotificationKind;
  message: string;
}

interface NotificationsContextValue {
  notify: (message: string, kind?: NotificationKind) => void;
}

const NotificationsContext = createContext<NotificationsContextValue | null>(null);

const AUTO_DISMISS_MS = 5000;

export function NotificationsProvider({ children }: PropsWithChildren) {
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const nextId = useRef(0);

  const dismiss = useCallback((id: number) => {
    setNotifications((prev) => prev.filter((n) => n.id !== id));
  }, []);

  const notify = useCallback(
    (message: string, kind: NotificationKind = "info") => {
      const id = nextId.current++;
      setNotifications((prev) => [...prev, { id, kind, message }]);
      setTimeout(() => dismiss(id), AUTO_DISMISS_MS);
    },
    [dismiss],
  );

  return (
    <NotificationsContext.Provider value={{ notify }}>
      {children}
      <div className="notifications-host" aria-live="polite" aria-atomic="false">
        {notifications.map((n) => (
          <div key={n.id} className={`notification notification--${n.kind}`} role="status">
            <span>{n.message}</span>
            <button type="button" onClick={() => dismiss(n.id)} aria-label="Dismiss">
              ×
            </button>
          </div>
        ))}
      </div>
    </NotificationsContext.Provider>
  );
}

const NOOP_NOTIFICATIONS: NotificationsContextValue = { notify: () => {} };

/** Falls back to a no-op outside a NotificationsProvider (e.g. a page rendered in isolation in a
 * test) rather than throwing — notifications are a UX enhancement, not a rendering dependency. */
export function useNotifications(): NotificationsContextValue {
  return useContext(NotificationsContext) ?? NOOP_NOTIFICATIONS;
}
