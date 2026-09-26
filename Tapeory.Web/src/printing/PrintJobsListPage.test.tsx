import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, useLocation } from "react-router-dom";
import { NotificationsProvider } from "../notifications/NotificationsContext";
import { PrintJobsListPage } from "./PrintJobsListPage";

function LocationProbe() {
  const location = useLocation();
  return <span data-testid="location">{location.pathname + location.search}</span>;
}

function renderPage(initialPath = "/print-jobs") {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <PrintJobsListPage />
      <LocationProbe />
    </MemoryRouter>,
  );
}

function makeJob(id: number, overrides: Record<string, unknown> = {}) {
  return {
    id,
    templateId: 1,
    templateName: "Shipping Label",
    templateVersionNumber: 2,
    printerId: null,
    printerName: null,
    status: "Completed",
    errorMessage: null,
    createdAt: "2026-01-01T00:00:00Z",
    completedAt: null,
    items: [],
    ...overrides,
  };
}

function item(quantity: number) {
  return { id: quantity, fieldValues: {}, quantity, status: "Completed", errorMessage: null, previewUrl: null };
}

function jsonResponse(body: unknown, ok = true, status = ok ? 200 : 400) {
  return { ok, status, json: async () => body } as Response;
}

describe("PrintJobsListPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows a loading state, then an empty message when there are no jobs", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([])));

    renderPage();

    expect(screen.getByText(/loading print jobs/i)).toBeInTheDocument();
    expect(await screen.findByText(/no print jobs yet/i)).toBeInTheDocument();
  });

  it("lists jobs with a link to each detail page and a status badge", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse([
          {
            id: 12,
            templateId: 1,
            templateName: "Shipping Label",
            templateVersionNumber: 1,
            printerName: "Front Desk",
            status: "Completed",
            errorMessage: null,
            createdAt: "2026-01-01T00:00:00Z",
            completedAt: "2026-01-01T00:01:00Z",
            items: [],
          },
        ]),
      ),
    );

    renderPage();

    const link = await screen.findByRole("link", { name: "#12" });
    expect(link).toHaveAttribute("href", "/print-jobs/12");
    expect(screen.getByText("Shipping Label")).toBeInTheDocument();
    expect(screen.getByText("Front Desk")).toBeInTheDocument();
    expect(screen.getByText("Completed")).toBeInTheDocument();
  });

  it("shows an error message when the request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(null, false, 500)));

    renderPage();

    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });

  describe("grid", () => {
    const jobs = [
      makeJob(1, { templateName: "Jam", printerName: "Kitchen", items: [item(10)], createdAt: "2026-01-01T00:00:00Z" }),
      makeJob(2, { templateId: 7, templateName: "Cable", items: [item(2), item(3)], status: "Failed", errorMessage: "Paper out" }),
      makeJob(3, { templateName: "Box", printerName: "Office", items: [item(40)], createdAt: "2026-03-01T00:00:00Z" }),
    ];

    function rowIds() {
      return screen
        .getAllByRole("row")
        .slice(1)
        .map((row) => within(row).getAllByRole("link")[0].textContent);
    }

    beforeEach(() => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(jobs)));
    });

    it("shows label totals, item counts, the template version and a job count", async () => {
      renderPage();
      await screen.findByRole("link", { name: "#1" });

      expect(screen.getByText("40")).toBeInTheDocument();
      expect(screen.getByText("2 items")).toBeInTheDocument();
      expect(screen.getAllByText("Version 2").length).toBe(3);
      expect(screen.getByText("3 print jobs")).toBeInTheDocument();
      expect(screen.getByText("Failed")).toHaveAttribute("title", "Paper out");
    });

    it("sorts by label count, most first on the first click, and keeps it in the URL", async () => {
      renderPage();
      await screen.findByRole("link", { name: "#1" });

      fireEvent.click(screen.getByRole("button", { name: "Labels" }));
      expect(rowIds()).toEqual(["#3", "#1", "#2"]);
      expect(screen.getByRole("columnheader", { name: "Labels" })).toHaveAttribute("aria-sort", "descending");
      expect(screen.getByTestId("location")).toHaveTextContent("?sort=labels&dir=desc");

      fireEvent.click(screen.getByRole("button", { name: "Labels" }));
      expect(rowIds()).toEqual(["#2", "#1", "#3"]);
    });

    it("restores a sort from the URL, e.g. printer with unassigned jobs last", async () => {
      renderPage("/print-jobs?sort=printer");
      await screen.findByRole("link", { name: "#1" });

      expect(rowIds()).toEqual(["#1", "#3", "#2"]);
    });

    it("opens a job when its row is clicked", async () => {
      renderPage();
      const link = await screen.findByRole("link", { name: "#3" });

      fireEvent.click(link.closest("tr")!.querySelector(".data-grid__col--labels")!);

      expect(screen.getByTestId("location")).toHaveTextContent("/print-jobs/3");
    });

    it("offers view and print-again icon actions", async () => {
      renderPage();
      await screen.findByRole("link", { name: "#2" });

      expect(screen.getByRole("link", { name: "View details: #2" })).toHaveAttribute("href", "/print-jobs/2");
      expect(screen.getByRole("link", { name: "Print again: Cable" })).toHaveAttribute("href", "/templates/7/print");
      expect(screen.getByRole("link", { name: "Cable" })).toHaveAttribute("href", "/templates/7/edit");
    });
  });

  describe("deleting", () => {
    function renderWithNotifications() {
      return render(
        <NotificationsProvider>
          <MemoryRouter initialEntries={["/print-jobs"]}>
            <PrintJobsListPage />
          </MemoryRouter>
        </NotificationsProvider>,
      );
    }

    /** GET /print-jobs answers with each of `lists` in turn; DELETEs answer with `onDelete`. */
    function stubApi(lists: unknown[][], onDelete: (url: string) => Response) {
      let listCall = 0;
      const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = input.toString();
        if (init?.method === "DELETE") return onDelete(url);
        return jsonResponse(lists[Math.min(listCall++, lists.length - 1)]);
      });
      vi.stubGlobal("fetch", fetchMock);
      return fetchMock;
    }

    function deleteCalls(fetchMock: ReturnType<typeof stubApi>) {
      return fetchMock.mock.calls.filter(([, init]) => init?.method === "DELETE").map(([url]) => url.toString());
    }

    it("deletes a single job after confirming, and removes its row", async () => {
      const fetchMock = stubApi([[makeJob(1), makeJob(2)]], () => ({ ok: true, status: 204 }) as Response);
      vi.stubGlobal("confirm", vi.fn(() => true));
      renderWithNotifications();

      fireEvent.click(await screen.findByRole("button", { name: "Delete from history: #2" }));

      expect(await screen.findByText("Print job #2 deleted.")).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "#2" })).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "#1" })).toBeInTheDocument();
      expect(deleteCalls(fetchMock)).toEqual(["/api/print-jobs/2"]);
      expect(confirm).toHaveBeenCalledWith(expect.stringContaining("still count in the statistics"));
    });

    it("keeps the job when the confirmation is cancelled", async () => {
      const fetchMock = stubApi([[makeJob(1)]], () => ({ ok: true, status: 204 }) as Response);
      vi.stubGlobal("confirm", vi.fn(() => false));
      renderWithNotifications();

      fireEvent.click(await screen.findByRole("button", { name: "Delete from history: #1" }));

      expect(deleteCalls(fetchMock)).toEqual([]);
      expect(screen.getByRole("link", { name: "#1" })).toBeInTheDocument();
    });

    it("can't delete a job that is still printing", async () => {
      stubApi([[makeJob(1, { status: "Processing" }), makeJob(2, { status: "Queued" })]], () => ({ ok: true }) as Response);
      renderWithNotifications();

      const button = await screen.findByRole("button", { name: "Delete from history: #1" });
      expect(button).toBeDisabled();
      expect(button).toHaveAttribute("title", expect.stringMatching(/still printing/i));
      expect(screen.getByRole("button", { name: "Delete from history: #2" })).toBeDisabled();
      // Nothing finished yet, so there's nothing "Delete all" could remove either.
      expect(screen.getByRole("button", { name: "Delete all" })).toBeDisabled();
    });

    it("shows the server's reason and keeps the row when deleting fails", async () => {
      stubApi([[makeJob(1)]], () => jsonResponse({ detail: "This print job is still printing." }, false, 409));
      vi.stubGlobal("confirm", vi.fn(() => true));
      renderWithNotifications();

      fireEvent.click(await screen.findByRole("button", { name: "Delete from history: #1" }));

      expect(await screen.findByText("This print job is still printing.")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "#1" })).toBeInTheDocument();
    });

    it("deletes all jobs, reloads the list and reports jobs that were kept because they're printing", async () => {
      const printing = makeJob(3, { status: "Processing" });
      const fetchMock = stubApi(
        [[makeJob(1), makeJob(2), printing], [printing]],
        () => jsonResponse({ deleted: 2, skippedInProgress: 1 }),
      );
      vi.stubGlobal("confirm", vi.fn(() => true));
      renderWithNotifications();

      fireEvent.click(await screen.findByRole("button", { name: "Delete all" }));

      expect(
        await screen.findByText("2 print jobs deleted. 1 job that is still printing was kept."),
      ).toBeInTheDocument();
      expect(deleteCalls(fetchMock)).toEqual(["/api/print-jobs"]);
      expect(confirm).toHaveBeenCalledWith(expect.stringContaining("Delete all 3 print jobs"));
      expect(screen.queryByRole("link", { name: "#1" })).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "#3" })).toBeInTheDocument();
    });
  });

  it("shows a deleted template's name without links to edit or print it again", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([makeJob(4, { templateName: "Old Label", templateDeleted: true })])));
    renderPage();

    expect(await screen.findByText("Old Label")).toBeInTheDocument();
    expect(screen.getByText("Template deleted")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Old Label" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /print again/i })).not.toBeInTheDocument();
  });
});
