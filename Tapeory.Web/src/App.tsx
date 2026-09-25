import { BrowserRouter, Route, Routes } from "react-router-dom";
import { DashboardPage } from "./DashboardPage";
import { EditorPage } from "./editor/EditorPage";
import { TemplatesListPage } from "./editor/TemplatesListPage";
import { AppLayout } from "./layout/AppLayout";
import { PrintTemplatePage } from "./printing/PrintTemplatePage";
import { PrintJobDetailPage } from "./printing/PrintJobDetailPage";
import { PrintJobsListPage } from "./printing/PrintJobsListPage";
import { PrintersPage } from "./printers/PrintersPage";
import { SettingsPage } from "./settings/SettingsPage";
import { SetupGate } from "./setup/SetupGate";

export function App() {
  return (
    <SetupGate>
      <BrowserRouter>
        <AppLayout>
          <Routes>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/templates" element={<TemplatesListPage />} />
            <Route path="/templates/new" element={<EditorPage />} />
            <Route path="/templates/:id/edit" element={<EditorPage />} />
            <Route path="/templates/:id/print" element={<PrintTemplatePage />} />
            <Route path="/print-jobs" element={<PrintJobsListPage />} />
            <Route path="/print-jobs/:id" element={<PrintJobDetailPage />} />
            <Route path="/printers" element={<PrintersPage />} />
            <Route path="/settings" element={<SettingsPage />} />
          </Routes>
        </AppLayout>
      </BrowserRouter>
    </SetupGate>
  );
}
