import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StatisticsCard } from "./StatisticsCard";

const allTime = {
  templateCount: 4,
  printerCount: 1,
  printJobCount: 9,
  completedPrintJobCount: 8,
  failedPrintJobCount: 1,
  labelsPrinted: 126,
  totalPrintedLengthMm: 6300,
  totalPrintedAreaMm2: 100000,
  mostPrintedTemplateName: "Jam",
  mostPrintedTemplateCount: 60,
  lastPrintedAt: "2026-09-01T10:00:00Z",
  statsSince: null as string | null,
};

const afterReset = {
  ...allTime,
  printJobCount: 0,
  completedPrintJobCount: 0,
  failedPrintJobCount: 0,
  labelsPrinted: 0,
  totalPrintedLengthMm: 0,
  totalPrintedAreaMm2: 0,
  mostPrintedTemplateName: null,
  mostPrintedTemplateCount: 0,
  lastPrintedAt: null,
  statsSince: "2026-09-23T20:00:00Z",
};

function jsonResponse(body: unknown, ok = true) {
  return { ok, status: ok ? 200 : 500, json: async () => body } as Response;
}

/** Routes by method + path; records every call so tests can assert what was sent. */
function stubApi(handlers: Record<string, () => Response>) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const key = `${init?.method ?? "GET"} ${typeof input === "string" ? input : input.toString()}`;
    const handler = handlers[key];
    if (!handler) throw new Error(`Unexpected fetch: ${key}`);
    return handler();
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function renderCard() {
  return render(<StatisticsCard icon={null} />);
}

describe("StatisticsCard", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows the current totals and that they cover all time", async () => {
    stubApi({ "GET /api/stats": () => jsonResponse(allTime) });

    renderCard();

    expect(await screen.findByText("126")).toBeInTheDocument();
    expect(screen.getByText("6.3 m")).toBeInTheDocument();
    expect(screen.getByText("The beginning")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Restore all-time statistics" })).not.toBeInTheDocument();
  });

  it("asks for confirmation before resetting, and cancelling changes nothing", async () => {
    const fetchMock = stubApi({ "GET /api/stats": () => jsonResponse(allTime) });

    renderCard();
    await screen.findByText("126");
    fireEvent.click(screen.getByRole("button", { name: "Reset statistics" }));

    const confirm = screen.getByRole("group", { name: "Reset statistics" });
    expect(within(confirm).getByText(/print history are kept/i)).toBeInTheDocument();

    fireEvent.click(within(confirm).getByRole("button", { name: "Cancel" }));

    expect(screen.queryByRole("group", { name: "Reset statistics" })).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("resets after confirming, then offers to restore all-time statistics", async () => {
    const fetchMock = stubApi({
      "GET /api/stats": () => jsonResponse(allTime),
      "POST /api/stats/reset": () => jsonResponse(afterReset),
      "DELETE /api/stats/reset": () => jsonResponse(allTime),
    });

    renderCard();
    await screen.findByText("126");
    fireEvent.click(screen.getByRole("button", { name: "Reset statistics" }));
    fireEvent.click(screen.getByRole("button", { name: "Yes, reset" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Statistics reset");
    expect(screen.getByText("0")).toBeInTheDocument();
    expect(screen.queryByText("The beginning")).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith("/api/stats/reset", { method: "POST" });

    fireEvent.click(screen.getByRole("button", { name: "Restore all-time statistics" }));

    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("All-time statistics restored"));
    expect(screen.getByText("126")).toBeInTheDocument();
    expect(screen.getByText("The beginning")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith("/api/stats/reset", { method: "DELETE" });
  });

  it("keeps the confirmation open and shows an error when the reset fails", async () => {
    stubApi({
      "GET /api/stats": () => jsonResponse(allTime),
      "POST /api/stats/reset": () => jsonResponse(null, false),
    });

    renderCard();
    await screen.findByText("126");
    fireEvent.click(screen.getByRole("button", { name: "Reset statistics" }));
    fireEvent.click(screen.getByRole("button", { name: "Yes, reset" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/couldn’t update the statistics/i);
    expect(screen.getByRole("button", { name: "Yes, reset" })).toBeEnabled();
    expect(screen.getByText("126")).toBeInTheDocument();
  });
});
