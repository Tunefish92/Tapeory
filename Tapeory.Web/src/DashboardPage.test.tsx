import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { DashboardPage } from "./DashboardPage";

const health = { status: "ok", storagePath: "/data", databaseConnected: true };

const stats = {
  templateCount: 7,
  printerCount: 2,
  printJobCount: 12,
  completedPrintJobCount: 9,
  failedPrintJobCount: 3,
  labelsPrinted: 480,
  totalPrintedLengthMm: 24_000,
  totalPrintedAreaMm2: 600_000,
  mostPrintedTemplateName: "Shipping Label",
  mostPrintedTemplateCount: 300,
  lastPrintedAt: "2026-01-01T12:00:00Z",
  statsSince: null as string | null,
};

function stubFetch(statsBody: unknown = stats, statsOk = true, healthBody: unknown = health, healthOk = true) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === "string" ? input : input.toString();

      if (url.endsWith("/api/stats")) {
        return { ok: statsOk, status: statsOk ? 200 : 500, json: async () => statsBody } as Response;
      }

      return { ok: healthOk, status: healthOk ? 200 : 502, json: async () => healthBody } as Response;
    }),
  );
}

function renderPage() {
  return render(
    <MemoryRouter>
      <DashboardPage />
    </MemoryRouter>,
  );
}

describe("DashboardPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows a loading state and then the health status once the API responds", async () => {
    stubFetch();

    renderPage();

    expect(screen.getByText(/checking whether everything is running/i)).toBeInTheDocument();

    expect(await screen.findByText("Everything is working.")).toBeInTheDocument();
    expect(screen.getByText("Server")).toBeInTheDocument();
    expect(screen.getByText("Online")).toBeInTheDocument();
    expect(screen.getByText("Database")).toBeInTheDocument();
    expect(screen.getByText("Connected")).toBeInTheDocument();
    expect(screen.getByText("File storage")).toBeInTheDocument();
    expect(screen.getByText("/data")).toBeInTheDocument();
  });

  it("explains when the server is running but the database isn't connected", async () => {
    stubFetch(stats, true, { ...health, databaseConnected: false });

    renderPage();

    expect(await screen.findByText("Not connected")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent(/can't reach its database/);
    expect(screen.getByText("Online")).toBeInTheDocument();
  });

  it("shows the server as offline, with the rest unknown, when the health check fails", async () => {
    stubFetch(stats, true, {}, false);

    renderPage();

    expect(await screen.findByText("Offline")).toBeInTheDocument();
    expect(screen.getAllByText("Unknown")).toHaveLength(2);
    expect(screen.getByRole("alert")).toHaveTextContent(/server can't be reached/);
    expect(screen.getByText(/Details: Health check failed with status 502/)).toBeInTheDocument();
  });

  it("shows stat cards that link to their pages", async () => {
    stubFetch();

    renderPage();

    expect(await screen.findByText("480")).toBeInTheDocument();
    expect(screen.getByText("7").closest("a")).toHaveAttribute("href", "/templates");
    expect(screen.getByText("2").closest("a")).toHaveAttribute("href", "/printers");
    expect(screen.getByText("12")).toBeInTheDocument();
    expect(screen.getByText("75% successful")).toBeInTheDocument();
  });

  it("shows fun facts with a real-world comparison", async () => {
    stubFetch();

    renderPage();

    expect(await screen.findByText("24 m")).toBeInTheDocument();
    expect(screen.getByText("6,000 cm²")).toBeInTheDocument();
    expect(screen.getByText("Shipping Label · 300×")).toBeInTheDocument();
    expect(screen.getByText("That's about 2× the length of a city bus.")).toBeInTheDocument();
  });

  it("encourages more printing before a comparison is possible", async () => {
    stubFetch({
      ...stats,
      completedPrintJobCount: 0,
      failedPrintJobCount: 0,
      labelsPrinted: 0,
      totalPrintedLengthMm: 0,
      totalPrintedAreaMm2: 0,
      mostPrintedTemplateName: null,
      mostPrintedTemplateCount: 0,
      lastPrintedAt: null,
    });

    renderPage();

    expect(await screen.findByText(/unlock a comparison/i)).toBeInTheDocument();
    expect(screen.getByText("Never")).toBeInTheDocument();
    expect(screen.getByText("Nothing printed yet")).toBeInTheDocument();
  });

  it("says when the statistics were last reset, linking to settings", async () => {
    stubFetch({ ...stats, statsSince: "2026-09-01T10:00:00Z" });

    renderPage();

    expect(await screen.findByText(/print statistics since/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Change" })).toHaveAttribute("href", "/settings");
  });

  it("does not mention a reset for all-time statistics", async () => {
    stubFetch();

    renderPage();
    await screen.findByText("12");

    expect(screen.queryByText(/print statistics since/i)).not.toBeInTheDocument();
  });

  it("shows an alert when statistics fail to load", async () => {
    stubFetch(null, false);

    renderPage();

    expect(await screen.findByText(/stats request failed/i)).toBeInTheDocument();
  });

  it("explains a network failure in plain words and keeps the technical message as a detail", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === "string" ? input : input.toString();
        if (url.endsWith("/api/stats")) {
          return { ok: true, json: async () => stats } as Response;
        }
        throw new Error("network down");
      }),
    );

    renderPage();

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/server can't be reached/));
    expect(screen.getByText("Details: network down")).toBeInTheDocument();
  });
});
