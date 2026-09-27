import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { installApiFetch } from "./api/session";
import { NotificationsProvider } from "./notifications/NotificationsContext";
import { initializeTheme } from "./theme/theme";
import "./i18n";
import "./index.css";

initializeTheme();
installApiFetch();

const container = document.getElementById("root");

if (!container) {
  throw new Error("Root element not found");
}

createRoot(container).render(
  <StrictMode>
    <NotificationsProvider>
      <App />
    </NotificationsProvider>
  </StrictMode>,
);
