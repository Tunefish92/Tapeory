import { createContext, useContext, useEffect, useState, type PropsWithChildren } from "react";
import { useTranslation } from "react-i18next";
import { isSetupRequired } from "../api/setup";
import { useNotifications } from "../notifications/NotificationsContext";
import { SetupPage } from "./SetupPage";

const SetupJustCompletedContext = createContext(false);

/** True right after the database setup in this browser: a brand-new install. */
export function useSetupJustCompleted() {
  return useContext(SetupJustCompletedContext);
}

/**
 * Shows the first-run database setup instead of the app while the server has no database
 * connection. The app renders straight away rather than waiting for the check, so every normal
 * page load stays as fast as before; only a brand-new install switches over to the setup.
 */
export function SetupGate({ children }: PropsWithChildren) {
  const { t } = useTranslation();
  const { notify } = useNotifications();
  const [setupRequired, setSetupRequired] = useState(false);
  const [justCompleted, setJustCompleted] = useState(false);

  useEffect(() => {
    let cancelled = false;

    void isSetupRequired().then((required) => {
      if (!cancelled) setSetupRequired(required);
    });

    return () => {
      cancelled = true;
    };
  }, []);

  if (setupRequired) {
    return (
      <SetupPage
        onComplete={() => {
          setSetupRequired(false);
          setJustCompleted(true);
          notify(t("setup.completed"), "success");
        }}
      />
    );
  }

  return <SetupJustCompletedContext.Provider value={justCompleted}>{children}</SetupJustCompletedContext.Provider>;
}
