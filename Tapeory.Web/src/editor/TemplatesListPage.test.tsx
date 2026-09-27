import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, useLocation } from "react-router-dom";
import { NotificationsProvider } from "../notifications/NotificationsContext";
import { TemplatesListPage } from "./TemplatesListPage";

function LocationProbe() {
  const location = useLocation();
  return (
    <>
      <span data-testid="location">{location.search}</span>
      <span data-testid="pathname">{location.pathname}</span>
    </>
  );
}

function renderPage(initialPath = "/templates") {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <TemplatesListPage />
      <LocationProbe />
    </MemoryRouter>,
  );
}

function jsonResponse(body: unknown, ok = true, status = ok ? 200 : 400) {
  return { ok, status, json: async () => body } as Response;
}

const shippingLabel = {
  id: 5,
  name: "Shipping Label",
  description: null,
  category: "Shipping",
  tags: [],
  status: "Published",
  currentVersionNumber: 3,
  widthMm: 62,
  heightMm: 29,
  previewImageUrl: null,
  sourceLbxUrl: null,
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-02T00:00:00Z",
};

describe("TemplatesListPage", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows a loading state, then an empty message when there are no templates", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([])));

    renderPage();

    expect(screen.getByText(/loading templates/i)).toBeInTheDocument();
    expect(await screen.findByText(/no templates yet/i)).toBeInTheDocument();
  });

  it("lists templates with a link to edit each one, and marks native vs imported source", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse([
          {
            id: 5,
            name: "Shipping Label",
            description: null,
            category: "Shipping",
            tags: [],
            status: "Draft",
            currentVersionNumber: 1,
            widthMm: 62,
            heightMm: 29,
            previewImageUrl: null,
            sourceLbxUrl: null,
            createdAt: "2026-01-01T00:00:00Z",
            updatedAt: "2026-01-02T00:00:00Z",
          },
          {
            id: 6,
            name: "Imported Label",
            description: null,
            category: null,
            tags: [],
            status: "Draft",
            currentVersionNumber: 1,
            widthMm: 50,
            heightMm: 25,
            previewImageUrl: null,
            sourceLbxUrl: "/api/templates/6/original-lbx",
            createdAt: "2026-01-01T00:00:00Z",
            updatedAt: "2026-01-02T00:00:00Z",
          },
        ]),
      ),
    );

    renderPage();

    const link = await screen.findByRole("link", { name: "Shipping Label" });
    expect(link).toHaveAttribute("href", "/templates/5/edit");
    expect(screen.getByRole("heading", { name: "Shipping" })).toBeInTheDocument();
    expect(screen.getAllByText("62×29mm").length).toBeGreaterThan(0);
    expect(screen.getByText("Native")).toBeInTheDocument();
    expect(screen.getByText("Imported (.lbx)")).toBeInTheDocument();
  });

  it("shows an error message when the request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(null, false, 500)));

    renderPage();

    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });

  it("links to the new-template route", () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([])));

    renderPage();

    expect(screen.getByRole("link", { name: "New Template" })).toHaveAttribute(
      "href",
      "/templates/new",
    );
  });

  it("shows an error when importing a file fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse([]))
        .mockResolvedValueOnce(jsonResponse({ detail: "Only .lbx files are supported." }, false, 400)),
    );

    renderPage();
    await screen.findByText(/no templates yet/i);

    const input = screen.getByTestId("lbx-file-input") as HTMLInputElement;
    const file = new File(["bytes"], "label.lbx", { type: "application/octet-stream" });
    fireEvent.change(input, { target: { files: [file] } });

    expect(await screen.findByRole("alert")).toHaveTextContent("Only .lbx files are supported.");
  });

  it("shows each template as a card with a lazily loaded preview of its current version", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([shippingLabel])));

    const { container } = renderPage();
    await screen.findByRole("link", { name: "Shipping Label" });

    const preview = container.querySelector(".template-card__preview img") as HTMLImageElement;
    expect(preview).toHaveAttribute("src", "/api/templates/5/thumbnail?v=3");
    expect(preview).toHaveAttribute("loading", "lazy");
    expect(screen.getByText("Published")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Edit" })).toHaveAttribute("href", "/templates/5/edit");
    expect(screen.getByRole("link", { name: "Print" })).toHaveAttribute("href", "/templates/5/print");
  });

  it("reveals the preview once it loads and falls back to a message if it fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([shippingLabel, { ...shippingLabel, id: 6, name: "Broken" }])));

    const { container } = renderPage();
    await screen.findByRole("link", { name: "Broken" });

    const [first, second] = Array.from(container.querySelectorAll(".template-card__preview img"));
    fireEvent.load(first);
    expect(first).toHaveClass("is-loaded");

    fireEvent.error(second);
    expect(await screen.findByText(/no preview available/i)).toBeInTheDocument();
  });

  it.each(["cards", "list"])("duplicates a template from the %s view and adds the copy", async (view) => {
    localStorage.setItem("tapeory.templatesView", view);
    const copy = { ...shippingLabel, id: 6, name: "Shipping Label (copy)", status: "Draft", currentVersionNumber: 1 };
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) =>
      String(input).endsWith("/templates/5/duplicate") && init?.method === "POST"
        ? jsonResponse(copy)
        : jsonResponse([shippingLabel]),
    );
    vi.stubGlobal("fetch", fetchMock);

    render(
      <NotificationsProvider>
        <MemoryRouter initialEntries={["/templates"]}>
          <TemplatesListPage />
        </MemoryRouter>
      </NotificationsProvider>,
    );
    await screen.findAllByText("Shipping Label");

    fireEvent.click(screen.getByRole("button", { name: "Duplicate: Shipping Label" }));

    expect(await screen.findAllByText("Shipping Label (copy)")).not.toHaveLength(0);
    const request = fetchMock.mock.calls.find(([input]) => String(input).endsWith("/duplicate"));
    expect(JSON.parse(String(request?.[1]?.body))).toEqual({ name: "Shipping Label (copy)" });
  });

  it("switches to a list view and remembers the choice", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([shippingLabel])));

    renderPage();
    await screen.findByRole("link", { name: "Shipping Label" });
    expect(screen.queryByRole("table")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "List" }));

    expect(screen.getByRole("table")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "List" })).toHaveAttribute("aria-pressed", "true");
    expect(localStorage.getItem("tapeory.templatesView")).toBe("list");
  });

  it("opens in the list view when that was the last choice", async () => {
    localStorage.setItem("tapeory.templatesView", "list");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([shippingLabel])));

    renderPage();

    expect(await screen.findByRole("table")).toBeInTheDocument();
  });

  describe("search", () => {
    const templates = [
      shippingLabel,
      { ...shippingLabel, id: 7, name: "Étikett Lager" },
      { ...shippingLabel, id: 8, name: "Asset Tag" },
    ];

    it("filters templates by name, ignoring case and accents, and keeps the query in the URL", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(templates)));

      renderPage();
      await screen.findByRole("link", { name: "Asset Tag" });

      fireEvent.change(screen.getByRole("searchbox", { name: "Search templates" }), { target: { value: "ETIKETT" } });

      expect(screen.getByRole("link", { name: "Étikett Lager" })).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Shipping Label" })).not.toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Asset Tag" })).not.toBeInTheDocument();
      expect(screen.getByTestId("location")).toHaveTextContent("?q=ETIKETT");
    });

    it("applies a query from the URL on load", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(templates)));

      renderPage("/templates?q=asset");

      expect(await screen.findByRole("link", { name: "Asset Tag" })).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Shipping Label" })).not.toBeInTheDocument();
      expect(screen.getByRole("searchbox")).toHaveValue("asset");
    });

    it("shows a no-matches message that can clear the search", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(templates)));

      renderPage("/templates?q=zzz");

      expect(await screen.findByText("No templates match “zzz”.")).toBeInTheDocument();

      fireEvent.click(screen.getAllByRole("button", { name: "Clear search" })[1]);

      expect(screen.getByRole("link", { name: "Shipping Label" })).toBeInTheDocument();
      expect(screen.getByRole("searchbox")).toHaveValue("");
      expect(screen.getByTestId("location")).toBeEmptyDOMElement();
    });

    it("clears the search with Escape", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(templates)));

      renderPage("/templates?q=asset");
      const search = await screen.findByRole("searchbox");

      fireEvent.keyDown(search, { key: "Escape" });

      expect(search).toHaveValue("");
      expect(screen.getByRole("link", { name: "Shipping Label" })).toBeInTheDocument();
    });

    it("filters the list view too", async () => {
      localStorage.setItem("tapeory.templatesView", "list");
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(templates)));

      renderPage("/templates?q=shipping");

      await screen.findByRole("table");
      expect(screen.getAllByRole("row")).toHaveLength(2);
    });
  });

  describe("groups", () => {
    const jam = { ...shippingLabel, id: 10, name: "Strawberry", category: "Marmelade", tags: ["kitchen"] };
    const jam2 = { ...shippingLabel, id: 11, name: "Apricot", category: "marmelade" };
    const cable = { ...shippingLabel, id: 12, name: "HDMI", category: "Cables" };
    const loose = { ...shippingLabel, id: 13, name: "Loose", category: null };
    const all = [jam, jam2, cable, loose];

    function routedFetch(handlers: Record<string, (init?: RequestInit) => unknown>) {
      return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === "string" ? input : input.toString();
        const key = `${init?.method ?? "GET"} ${url}`;
        const handler = handlers[key];
        if (!handler) throw new Error(`Unexpected fetch: ${key}`);
        return jsonResponse(handler(init));
      });
    }

    it("shows filter pills with counts, merging groups that differ only in case", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(all)));

      renderPage();
      const filters = await screen.findByRole("group", { name: "Filter by group" });

      expect(within(filters).getByRole("button", { name: "All (4)" })).toHaveAttribute("aria-pressed", "true");
      expect(within(filters).getByRole("button", { name: "Marmelade (2)" })).toBeInTheDocument();
      expect(within(filters).getByRole("button", { name: "Cables (1)" })).toBeInTheDocument();
      expect(within(filters).getByRole("button", { name: "Ungrouped (1)" })).toBeInTheDocument();
    });

    it("sorts cards into one section per group with ungrouped last", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(all)));

      renderPage();
      await screen.findByRole("link", { name: "HDMI" });

      const headings = screen.getAllByRole("heading", { level: 3 }).filter((h) => h.closest(".group-section__header"));
      expect(headings.map((h) => h.textContent)).toEqual(["Cables", "Marmelade", "Ungrouped"]);
    });

    it("filters to one group and keeps it in the URL", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(all)));

      renderPage();
      fireEvent.click(await screen.findByRole("button", { name: "Marmelade (2)" }));

      expect(screen.getByRole("link", { name: "Strawberry" })).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Apricot" })).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "HDMI" })).not.toBeInTheDocument();
      expect(screen.getByTestId("location")).toHaveTextContent("?group=marmelade");
    });

    it("shows only ungrouped templates with the Ungrouped filter", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(all)));

      renderPage("/templates?group=__none");

      expect(await screen.findByRole("link", { name: "Loose" })).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Strawberry" })).not.toBeInTheDocument();
    });

    it("adds a template to a group from its card, keeping its tags", async () => {
      let sentBody: Record<string, unknown> = {};
      vi.stubGlobal(
        "fetch",
        routedFetch({
          "GET /api/templates": () => all,
          "PUT /api/templates/13": (init) => {
            sentBody = JSON.parse(init!.body as string);
            return { ...loose, category: "Cables" };
          },
        }),
      );

      renderPage();
      fireEvent.click(await screen.findByRole("button", { name: "Add to group" }));
      fireEvent.change(screen.getByRole("combobox", { name: "Group" }), { target: { value: "Cables" } });
      fireEvent.click(screen.getByRole("button", { name: "Save" }));

      await waitFor(() => expect(screen.getByRole("button", { name: "Cables (2)" })).toBeInTheDocument());
      expect(sentBody).toMatchObject({ name: "Loose", category: "Cables", tags: [] });
      expect(screen.queryByRole("button", { name: /^Ungrouped/ })).not.toBeInTheDocument();
    });

    it("removes a template from its group", async () => {
      let sentBody: Record<string, unknown> = {};
      vi.stubGlobal(
        "fetch",
        routedFetch({
          "GET /api/templates": () => all,
          "PUT /api/templates/12": (init) => {
            sentBody = JSON.parse(init!.body as string);
            return { ...cable, category: null };
          },
        }),
      );

      renderPage("/templates?group=cables");
      const card = (await screen.findByRole("link", { name: "HDMI" })).closest("article")!;
      fireEvent.click(within(card as HTMLElement).getByRole("button", { name: "Change group: Cables" }));
      fireEvent.click(within(card as HTMLElement).getByRole("button", { name: "Remove from group" }));

      await waitFor(() => expect(sentBody).toMatchObject({ category: null }));
    });

    it("renames a group from its section header", async () => {
      let renameBody: Record<string, unknown> = {};
      vi.stubGlobal(
        "fetch",
        routedFetch({
          "GET /api/templates": () => all,
          "POST /api/templates/groups/rename": (init) => {
            renameBody = JSON.parse(init!.body as string);
            return { updatedCount: 2 };
          },
        }),
      );

      renderPage();
      fireEvent.click(await screen.findByRole("button", { name: "Rename group: Marmelade" }));
      fireEvent.change(screen.getByRole("textbox", { name: "New name for “Marmelade”" }), {
        target: { value: "Jam" },
      });
      fireEvent.click(screen.getByRole("button", { name: "Save" }));

      expect(await screen.findByRole("heading", { name: "Jam" })).toBeInTheDocument();
      expect(renameBody).toEqual({ from: "Marmelade", to: "Jam" });
      expect(screen.getByRole("button", { name: "Jam (2)" })).toBeInTheDocument();
    });

    it("hides group filters when no template has a group", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([loose])));

      renderPage();
      await screen.findByRole("link", { name: "Loose" });

      expect(screen.queryByRole("group", { name: "Filter by group" })).not.toBeInTheDocument();
    });
  });

  it("shows the label height in the card corner", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse([{ ...shippingLabel, heightMm: 9 }, { ...shippingLabel, id: 6, name: "Wide", heightMm: 12.5 }])),
    );

    renderPage();
    await screen.findByRole("link", { name: "Wide" });

    expect(screen.getByLabelText("Label height: 9 mm")).toBeInTheDocument();
    expect(screen.getByLabelText("Label height: 12.5 mm")).toBeInTheDocument();
  });

  describe("list view sorting", () => {
    const rows = [
      { ...shippingLabel, id: 1, name: "Beta", category: "Marmelade", heightMm: 24, updatedAt: "2026-03-01T00:00:00Z" },
      { ...shippingLabel, id: 2, name: "alpha", category: null, heightMm: 9, updatedAt: "2026-01-01T00:00:00Z" },
      { ...shippingLabel, id: 3, name: "Gamma", category: "Cables", heightMm: 12, updatedAt: "2026-06-01T00:00:00Z" },
    ];

    function rowNames() {
      return screen
        .getAllByRole("row")
        .slice(1)
        .map((row) => within(row).getAllByRole("link")[0].textContent);
    }

    beforeEach(() => {
      localStorage.setItem("tapeory.templatesView", "list");
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(rows)));
    });

    it("sorts by a column on click and flips direction on a second click", async () => {
      renderPage();
      await screen.findByRole("link", { name: "Gamma" });

      fireEvent.click(screen.getByRole("button", { name: "Name" }));
      expect(rowNames()).toEqual(["alpha", "Beta", "Gamma"]);
      expect(screen.getByRole("columnheader", { name: "Name" })).toHaveAttribute("aria-sort", "ascending");
      expect(screen.getByTestId("location")).toHaveTextContent("?sort=name");

      fireEvent.click(screen.getByRole("button", { name: "Name" }));
      expect(rowNames()).toEqual(["Gamma", "Beta", "alpha"]);
      expect(screen.getByRole("columnheader", { name: "Name" })).toHaveAttribute("aria-sort", "descending");
    });

    it("starts date sorting newest first and marks only the sorted column", async () => {
      renderPage();
      await screen.findByRole("link", { name: "Gamma" });

      fireEvent.click(screen.getByRole("button", { name: "Updated" }));
      expect(rowNames()).toEqual(["Gamma", "Beta", "alpha"]);
      expect(screen.getByRole("columnheader", { name: "Name" })).not.toHaveAttribute("aria-sort");
    });

    it("restores the sort from the URL, e.g. by size", async () => {
      renderPage("/templates?sort=size&dir=desc");
      await screen.findByRole("link", { name: "Gamma" });

      expect(rowNames()).toEqual(["Beta", "Gamma", "alpha"]);
    });

    it("sorts by group with ungrouped templates last", async () => {
      renderPage("/templates?sort=group");
      await screen.findByRole("link", { name: "Gamma" });

      expect(rowNames()).toEqual(["Gamma", "Beta", "alpha"]);
    });

    it("ignores an unknown sort column", async () => {
      renderPage("/templates?sort=bogus");
      await screen.findByRole("link", { name: "Gamma" });

      expect(rowNames()).toEqual(["Beta", "alpha", "Gamma"]);
    });

    it("opens the editor when a row is clicked", async () => {
      renderPage();
      const link = await screen.findByRole("link", { name: "Gamma" });

      fireEvent.click(link.closest("tr")!.querySelector(".data-grid__col--status")!);

      expect(screen.getByTestId("pathname")).toHaveTextContent("/templates/3/edit");
    });

    // The sort bar is display:none at desktop widths, hence hidden: true.
    it("sorts from the compact sort bar used on phones", async () => {
      renderPage();
      await screen.findByRole("link", { name: "Gamma" });

      fireEvent.change(screen.getByRole("combobox", { name: "Sort by", hidden: true }), { target: { value: "updated" } });
      expect(rowNames()).toEqual(["Gamma", "Beta", "alpha"]);

      fireEvent.click(screen.getByRole("button", { name: "Descending", hidden: true }));
      expect(rowNames()).toEqual(["alpha", "Beta", "Gamma"]);
      expect(screen.getByTestId("location")).toHaveTextContent("?sort=updated");

      fireEvent.change(screen.getByRole("combobox", { name: "Sort by", hidden: true }), { target: { value: "" } });
      expect(rowNames()).toEqual(["Beta", "alpha", "Gamma"]);
    });

    it("shows a thumbnail, relative update time and a row count", async () => {
      const { container } = renderPage();
      await screen.findByRole("link", { name: "Gamma" });

      expect(container.querySelector(".data-grid__thumb img")).toHaveAttribute("src", "/api/templates/1/thumbnail?v=3");
      expect(container.querySelector("time")).toHaveAttribute("dateTime", "2026-03-01T00:00:00Z");
      expect(screen.getByText("3 templates")).toBeInTheDocument();
    });

    it("offers edit and print as icon links", async () => {
      renderPage();
      await screen.findByRole("link", { name: "Gamma" });

      expect(screen.getByRole("link", { name: "Edit: Gamma" })).toHaveAttribute("href", "/templates/3/edit");
    });

    it("offers printing as an icon link", async () => {
      renderPage();
      await screen.findByRole("link", { name: "Gamma" });

      const printLinks = screen.getAllByRole("link", { name: "Print" });
      expect(printLinks[0]).toHaveAttribute("href", "/templates/1/print");
      expect(printLinks[0]).not.toHaveTextContent("Print");
    });
  });

  describe("deleting", () => {
    function renderWithNotifications() {
      return render(
        <NotificationsProvider>
          <MemoryRouter initialEntries={["/templates"]}>
            <TemplatesListPage />
          </MemoryRouter>
        </NotificationsProvider>,
      );
    }

    const cableLabel = { ...shippingLabel, id: 6, name: "Cable Label", category: null };

    function stubApi(onDelete: () => Response) {
      const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) =>
        init?.method === "DELETE" ? onDelete() : jsonResponse([shippingLabel, cableLabel]),
      );
      vi.stubGlobal("fetch", fetchMock);
      return fetchMock;
    }

    function deleteCalls(fetchMock: ReturnType<typeof stubApi>) {
      return fetchMock.mock.calls.filter(([, init]) => init?.method === "DELETE").map(([url]) => url.toString());
    }

    it("deletes a template after confirming, and removes its card", async () => {
      const fetchMock = stubApi(() => ({ ok: true, status: 204 }) as Response);
      vi.stubGlobal("confirm", vi.fn(() => true));
      renderWithNotifications();

      fireEvent.click(await screen.findByRole("button", { name: "Delete: Shipping Label" }));

      expect(await screen.findByText("Template “Shipping Label” deleted.")).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Shipping Label" })).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Cable Label" })).toBeInTheDocument();
      expect(deleteCalls(fetchMock)).toEqual(["/api/templates/5"]);
      expect(confirm).toHaveBeenCalledWith(expect.stringContaining("stay in the print history"));
    });

    it("keeps the template when the confirmation is cancelled", async () => {
      const fetchMock = stubApi(() => ({ ok: true, status: 204 }) as Response);
      vi.stubGlobal("confirm", vi.fn(() => false));
      renderWithNotifications();

      fireEvent.click(await screen.findByRole("button", { name: "Delete: Shipping Label" }));

      expect(deleteCalls(fetchMock)).toEqual([]);
      expect(screen.getByRole("link", { name: "Shipping Label" })).toBeInTheDocument();
    });

    it("can delete from the list view too", async () => {
      localStorage.setItem("tapeory.templatesView", "list");
      const fetchMock = stubApi(() => ({ ok: true, status: 204 }) as Response);
      vi.stubGlobal("confirm", vi.fn(() => true));
      renderWithNotifications();

      fireEvent.click(await screen.findByRole("button", { name: "Delete: Cable Label" }));

      await waitFor(() => expect(screen.queryByRole("link", { name: "Cable Label" })).not.toBeInTheDocument());
      expect(deleteCalls(fetchMock)).toEqual(["/api/templates/6"]);
    });

    it("shows the server's reason and keeps the template when deleting fails", async () => {
      stubApi(() => jsonResponse({ title: "Not Found", status: 404 }, false, 404));
      vi.stubGlobal("confirm", vi.fn(() => true));
      renderWithNotifications();

      fireEvent.click(await screen.findByRole("button", { name: "Delete: Shipping Label" }));

      expect(await screen.findByText("Not Found")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Shipping Label" })).toBeInTheDocument();
    });
  });
});
