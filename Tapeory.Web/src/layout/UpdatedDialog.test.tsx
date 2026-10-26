import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { SEEN_VERSION_KEY, UpdatedDialog } from "./UpdatedDialog";
import { parseReleaseNotes } from "./ReleaseNotes";

function answer(body: object) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { headers: { "Content-Type": "application/json" } })),
  );
}

const updated = { version: __APP_VERSION__, updated: true, previousVersion: "0.5.0", updatedAt: "2026-10-26T10:00:00Z" };

describe("UpdatedDialog", () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.unstubAllGlobals());

  it("shows what's new after an update, once", async () => {
    answer(updated);
    const { unmount } = render(<UpdatedDialog />);

    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent(`Tapeory ${__APP_VERSION__} is here`);
    expect(dialog).toHaveTextContent("Updated from 0.5.0");
    expect(screen.getByRole("link", { name: "Release notes on GitHub" })).toHaveAttribute(
      "href",
      `https://github.com/Tunefish92/Tapeory/releases/tag/v${__APP_VERSION__}`,
    );

    fireEvent.click(screen.getByRole("button", { name: "Got it" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(localStorage.getItem(SEEN_VERSION_KEY)).toBe(__APP_VERSION__);

    unmount();
    render(<UpdatedDialog />);
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("closes with Escape", async () => {
    answer({ ...updated, previousVersion: null });
    render(<UpdatedDialog />);

    expect(await screen.findByRole("dialog")).toHaveTextContent("Tapeory was updated");
    fireEvent.keyDown(window, { key: "Escape" });

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(localStorage.getItem(SEEN_VERSION_KEY)).toBe(__APP_VERSION__);
  });

  it("stays away on a new installation", async () => {
    answer({ version: __APP_VERSION__, updated: false, previousVersion: null, updatedAt: null });
    render(<UpdatedDialog />);

    await waitFor(() => expect(localStorage.getItem(SEEN_VERSION_KEY)).toBe(__APP_VERSION__));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("stays away when the server can't be asked", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("offline")));
    render(<UpdatedDialog />);

    await waitFor(() => expect(fetch).toHaveBeenCalled());
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});

describe("parseReleaseNotes", () => {
  it("reads headings, paragraphs and bullets with continuation lines and sub-bullets", () => {
    const blocks = parseReleaseNotes(
      "Bulk printing.\n\n### Added\n- **Bulk print** from a\n  spreadsheet.\n  - Profiles that\n    remember it\n  - JSON\n- `.tapeory` files\n\n### Fixed\n- One fix",
    );

    expect(blocks).toEqual([
      { kind: "paragraph", text: "Bulk printing." },
      { kind: "heading", text: "Added" },
      {
        kind: "list",
        items: [
          { text: "**Bulk print** from a spreadsheet.", children: ["Profiles that remember it", "JSON"] },
          { text: "`.tapeory` files", children: [] },
        ],
      },
      { kind: "heading", text: "Fixed" },
      { kind: "list", items: [{ text: "One fix", children: [] }] },
    ]);
  });
});
