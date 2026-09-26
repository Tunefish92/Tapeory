import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { BackupCard } from "./BackupCard";

const older = {
  fileName: "tapeory-db-20260920-080000-000.sql",
  sizeBytes: 2048,
  createdAt: "2026-09-20T08:00:00Z",
  beforeRestore: false,
};

const safety = {
  fileName: "tapeory-db-20260925-100000-000-before-restore.sql",
  sizeBytes: 4096,
  createdAt: "2026-09-25T10:00:00Z",
  beforeRestore: true,
};

function jsonResponse(body: unknown, status = 200) {
  return { ok: status < 400, status, json: async () => body } as Response;
}

/** Routes by method + path; each handler may be a list, answered in order. */
function stubApi(handlers: Record<string, Response | Response[]>) {
  const queues = Object.fromEntries(Object.entries(handlers).map(([k, v]) => [k, Array.isArray(v) ? [...v] : v]));
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const key = `${init?.method ?? "GET"} ${typeof input === "string" ? input : input.toString()}`;
    const handler = queues[key];
    if (!handler) throw new Error(`Unexpected fetch: ${key}`);
    return Array.isArray(handler) ? (handler.length > 1 ? handler.shift()! : handler[0]) : handler;
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

describe("BackupCard", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("lists server-side backups with download links and marks automatic ones", async () => {
    stubApi({ "GET /api/backups/database": jsonResponse([safety, older]) });

    render(<BackupCard kind="database" icon={null} />);

    const list = await screen.findByRole("list", { name: "Database backup" });
    const items = within(list).getAllByRole("listitem");
    expect(items).toHaveLength(2);
    expect(within(items[0]).getByText("Before restore")).toBeInTheDocument();
    expect(within(items[1]).queryByText("Before restore")).not.toBeInTheDocument();
    expect(within(items[1]).getByText("2 KB")).toBeInTheDocument();
    expect(within(items[1]).getByRole("link", { name: "Download" })).toHaveAttribute(
      "href",
      `/api/backups/database/${older.fileName}`,
    );
  });

  it("creates a backup and shows it", async () => {
    const fetchMock = stubApi({
      "GET /api/backups/labels": [jsonResponse([]), jsonResponse([{ ...older, fileName: "tapeory-labels-20260920-080000-000.zip" }])],
      "POST /api/backups/labels": jsonResponse(older, 201),
    });

    render(<BackupCard kind="labels" icon={null} />);
    expect(await screen.findByText("No backups yet.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Create backup" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Backup created.");
    expect(await screen.findByRole("list", { name: "Label backup" })).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith("/api/backups/labels", { method: "POST" });
  });

  it("restores only after confirming, and reports restored templates", async () => {
    const labelsBackup = { ...older, fileName: "tapeory-labels-20260920-080000-000.zip" };
    const fetchMock = stubApi({
      "GET /api/backups/labels": jsonResponse([labelsBackup]),
      [`POST /api/backups/labels/${labelsBackup.fileName}/restore`]: jsonResponse({
        restoredFileName: labelsBackup.fileName,
        safetyBackup: safety,
        restoredTemplates: 5,
        replacedTemplates: 3,
      }),
    });

    render(<BackupCard kind="labels" icon={null} />);
    fireEvent.click(await screen.findByRole("button", { name: "Restore" }));

    const confirm = screen.getByRole("group", { name: "Label backup" });
    expect(confirm).toHaveTextContent("Replace all current label templates");
    expect(fetchMock).not.toHaveBeenCalledWith(expect.stringContaining("/restore"), expect.anything());

    fireEvent.click(within(confirm).getByRole("button", { name: "Yes, restore" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Backup restored. Templates restored: 5.");
    expect(screen.queryByRole("group", { name: "Label backup" })).not.toBeInTheDocument();
  });

  it("can cancel a restore", async () => {
    const fetchMock = stubApi({ "GET /api/backups/database": jsonResponse([older]) });

    render(<BackupCard kind="database" icon={null} />);
    fireEvent.click(await screen.findByRole("button", { name: "Restore" }));
    expect(screen.getByRole("group", { name: "Database backup" })).toHaveTextContent("Replace the entire database");

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(screen.queryByRole("group", { name: "Database backup" })).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("deletes a backup after confirming", async () => {
    const fetchMock = stubApi({
      "GET /api/backups/database": [jsonResponse([older]), jsonResponse([])],
      [`DELETE /api/backups/database/${older.fileName}`]: { ok: true, status: 204, json: async () => null } as Response,
    });

    render(<BackupCard kind="database" icon={null} />);
    fireEvent.click(await screen.findByRole("button", { name: /^Delete / }));
    fireEvent.click(screen.getByRole("button", { name: "Yes, delete" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Backup deleted.");
    expect(await screen.findByText("No backups yet.")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(`/api/backups/database/${older.fileName}`, { method: "DELETE" });
  });

  it("shows the server's explanation when an action fails", async () => {
    stubApi({
      "GET /api/backups/database": jsonResponse([]),
      "POST /api/backups/database": jsonResponse({ title: "Conflict", detail: "Another backup or restore is running." }, 409),
    });

    render(<BackupCard kind="database" icon={null} />);
    await screen.findByText("No backups yet.");
    fireEvent.click(screen.getByRole("button", { name: "Create backup" }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Another backup or restore is running."));
  });
});
