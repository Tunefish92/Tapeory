import { useEffect, useState, type PropsWithChildren } from "react";
import { useTranslation } from "react-i18next";
import { isSetupRequired } from "../api/setup";
import { useNotifications } from "../notifications/NotificationsContext";
import { SetupPage } from "./SetupPage";

/**
 * Shows the first-run database setup instead of the app while the server has no database
 * connection. The app renders straight away rather than waiting for the check, so every normal
 * page load stays as fast as before; only a brand-new install switches over to the setup.
 */
export function SetupGate({ children }: PropsWithChildren) {
  const { t } = useTranslation();
  const { notify } = useNotifications();
  const [setupRequired, setSetupRequired] = useState(false);

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
          notify(t("setup.completed"), "success");
        }}
      />
    );
  }

  return <>{children}</>;
}
