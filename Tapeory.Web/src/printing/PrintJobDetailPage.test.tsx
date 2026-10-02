import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { PrintJobDetailPage } from "./PrintJobDetailPage";

function renderPage(id = 12) {
  return render(
    <MemoryRouter initialEntries={[`/print-jobs/${id}`]}>
      <Routes>
        <Route path="/print-jobs/:id" element={<PrintJobDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

function jsonResponse(body: unknown, ok = true, status = ok ? 200 : 400) {
  return { ok, status, json: async () => body } as Response;
}

function job(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: 12,
    templateId: 1,
    templateName: "Shipping Label",
    templateVersionNumber: 1,
    printerName: "Front Desk",
    status: "Processing",
    errorMessage: null,
    createdAt: "2026-01-01T00:00:00Z",
    completedAt: null,
    items: [
      {
        id: 1,
        fieldValues: { name: "Alice" },
        quantity: 1,
        status: "Processing",
        errorMessage: null,
        previewUrl: null,
      },
    ],
    ...overrides,
  };
}

describe("PrintJobDetailPage", () => {
  beforeEach(() => {
    // shouldAdvanceTime lets real time pass through for Testing Library's own internal polling
    // (waitFor/findBy*), while still letting the test explicitly fast-forward the component's
    // setTimeout-based poll via advanceTimersByTimeAsync. Without it, waitFor's internal
    // setTimeout-based retries never fire and every assertion hangs until Vitest's test timeout.
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("shows a refreshing indicator while the job is not in a terminal state", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(job())));

    renderPage();

    await waitFor(() => expect(screen.getByText(/refreshing/i)).toBeInTheDocument());
    expect(screen.getByRole("link", { name: /back to templates/i })).toHaveAttribute("href", "/templates");
    expect(screen.getByRole("link", { name: /back to history/i })).toHaveAttribute("href", "/print-jobs");
    expect(screen.getAllByText("Processing")).toHaveLength(2); // job status + item status
  });

  it("polls again and reflects the job reaching a terminal status", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(job()))
      .mockResolvedValueOnce(
        jsonResponse(
          job({
            status: "Completed",
            completedAt: "2026-01-01T00:02:00Z",
            items: [
              {
                id: 1,
                fieldValues: { name: "Alice" },
                quantity: 1,
                status: "Completed",
                errorMessage: null,
                previewUrl: "/api/print-jobs/items/1/preview",
              },
            ],
          }),
        ),
      );
    vi.stubGlobal("fetch", fetchMock);

    renderPage();

    await waitFor(() => expect(screen.getByText(/refreshing/i)).toBeInTheDocument());

    await vi.advanceTimersByTimeAsync(1500);

    await waitFor(() => expect(screen.queryByText(/refreshing/i)).not.toBeInTheDocument());
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(screen.getByRole("img", { name: /print job item 1 preview/i })).toHaveAttribute(
      "src",
      "/api/print-jobs/items/1/preview",
    );
  });

  it("shows the job and item error messages when the job fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse(
          job({
            status: "Failed",
            errorMessage: "One or more items failed to render.",
            items: [
              {
                id: 1,
                fieldValues: {},
                quantity: 1,
                status: "Failed",
                errorMessage: "Boom",
                previewUrl: null,
              },
            ],
          }),
        ),
      ),
    );

    renderPage();

    await waitFor(() => expect(screen.getAllByRole("alert")).toHaveLength(2));
    expect(screen.getByText("One or more items failed to render.")).toBeInTheDocument();
    expect(screen.getByText("Boom")).toBeInTheDocument();
  });

  it("shows an error message when the request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(null, false, 500)));

    renderPage();

    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });

  const item = (id: number, status: string, errorMessage: string | null = null) => ({
    id,
    fieldValues: { name: `Row ${id}` },
    quantity: 1,
    status,
    errorMessage,
    previewUrl: null,
  });

  it("shows how far a job with many rows is, and stops it on request", async () => {
    const printing = job({ status: "Printing", items: [item(1, "Completed"), item(2, "Printing"), item(3, "Queued"), item(4, "Queued")] });
    const fetchMock = vi.fn(async (input: RequestInfo | URL) =>
      jsonResponse(String(input).endsWith("/cancel") ? printing : printing),
    );
    vi.stubGlobal("fetch", fetchMock);

    renderPage();

    expect(await screen.findByText("1 of 4 rows done")).toBeInTheDocument();
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "25");

    fireEvent.click(screen.getByRole("button", { name: "Stop printing" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith("/api/print-jobs/12/cancel", expect.objectContaining({ method: "POST" })),
    );
    expect(await screen.findByRole("button", { name: "Stopping…" })).toBeDisabled();
  });

  it("offers to print the rows of a stopped job that didn't come out", async () => {
    const stopped = job({
      status: "Failed",
      errorMessage: "A label failed, and the rest of the job wasn't printed.",
      items: [item(1, "Completed"), item(2, "Failed", "No tape"), item(3, "Cancelled", "Not printed: the job stopped.")],
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL) =>
      jsonResponse(String(input).endsWith("/reprint-unprinted") ? job({ id: 13, status: "Queued" }) : stopped),
    );
    vi.stubGlobal("fetch", fetchMock);

    renderPage();

    expect(await screen.findByText("Not printed")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Stop printing" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Print the 2 missing rows" }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith("/api/print-jobs/12/reprint-unprinted", expect.objectContaining({ method: "POST" })),
    );
  });

  it("offers nothing to reprint when every row was printed", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(job({ status: "Completed", items: [item(1, "Completed")] }))));

    renderPage();

    await screen.findByText("Shipping Label");
    expect(screen.queryByRole("button", { name: /missing row/ })).not.toBeInTheDocument();
  });
});
