import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { SEEN_VERSION_KEY, UpdatedDialog } from "./UpdatedDialog";
import { parseReleaseNotes } from "./ReleaseNotes";

vi.mock("./releases", () => ({
  RELEASES: [
    { version: "0.8.0", date: "2026-12-01", notes: "### Added\n- Eight" },
    { version: "0.7.1", date: "2026-11-20", notes: "### Fixed\n- Seven one" },
    { version: "0.7.0", date: "2026-11-10", notes: "### Added\n- Seven" },
    { version: "0.6.0", date: "2026-10-26", notes: "### Added\n- Six" },
    { version: "0.5.0", date: "2026-10-01", notes: "### Added\n- Five" },
  ],
}));

function answer(body: object) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { headers: { "Content-Type": "application/json" } })),
  );
}

function updatedTo(version: string, previousVersion: string | null, changesSince = previousVersion) {
  answer({ version, updated: true, previousVersion, changesSince, updatedAt: "2026-12-01T10:00:00Z" });
}

/** The version headings in the notes, or [] when there is a single version without one. */
function shownVersions() {
  return screen.queryAllByRole("heading", { level: 4 }).map((heading) => heading.textContent);
}

describe("UpdatedDialog", () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.unstubAllGlobals());

  it("shows what's new after an update, once", async () => {
    updatedTo("0.8.0", "0.7.1");
    const { unmount } = render(<UpdatedDialog />);

    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("Tapeory 0.8.0 is here");
    expect(dialog).toHaveTextContent("Updated from 0.7.1");
    expect(dialog).toHaveTextContent("Eight");
    expect(dialog).not.toHaveTextContent("Seven one");
    expect(shownVersions()).toEqual([]);
    expect(screen.getByRole("link", { name: "Release notes on GitHub" })).toHaveAttribute(
      "href",
      "https://github.com/Tunefish92/Tapeory/releases/tag/v0.8.0",
    );

    fireEvent.click(screen.getByRole("button", { name: "Got it" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(localStorage.getItem(SEEN_VERSION_KEY)).toBe("0.8.0");

    unmount();
    render(<UpdatedDialog />);
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("after a jump over several versions, shows the changes of each, newest first", async () => {
    updatedTo("0.8.0", "0.6.0");
    render(<UpdatedDialog />);

    const dialog = await screen.findByRole("dialog");
    expect(shownVersions()).toEqual(["0.8.0 · 2026-12-01", "0.7.1 · 2026-11-20", "0.7.0 · 2026-11-10"]);
    expect(dialog).toHaveTextContent("Changes in 3 versions, newest first");
    expect(dialog).not.toHaveTextContent("Six");
    expect(within(dialog).getByRole("link", { name: "Release notes on GitHub" })).toHaveAttribute(
      "href",
      "https://github.com/Tunefish92/Tapeory/releases",
    );
  });

  it("for an unknown previous version, shows the changes since the oldest it can have been", async () => {
    updatedTo("0.7.0", null, "0.5.0");
    render(<UpdatedDialog />);

    expect(await screen.findByRole("dialog")).toHaveTextContent("Tapeory was updated");
    expect(shownVersions()).toEqual(["0.7.0 · 2026-11-10", "0.6.0 · 2026-10-26"]);
  });

  it("includes the versions this browser missed, when it last saw an older one", async () => {
    // The server went 0.6.0 → 0.7.0 → 0.8.0, but this browser was last opened on 0.6.0.
    localStorage.setItem(SEEN_VERSION_KEY, "0.6.0");
    updatedTo("0.8.0", "0.7.0");
    render(<UpdatedDialog />);

    await screen.findByRole("dialog");
    expect(shownVersions()).toEqual(["0.8.0 · 2026-12-01", "0.7.1 · 2026-11-20", "0.7.0 · 2026-11-10"]);
  });

  it("goes by the server when this browser saw a newer version than the one before the update", async () => {
    localStorage.setItem(SEEN_VERSION_KEY, "0.7.1");
    updatedTo("0.8.0", "0.7.0");
    render(<UpdatedDialog />);

    await screen.findByRole("dialog");
    expect(shownVersions()).toEqual(["0.8.0 · 2026-12-01", "0.7.1 · 2026-11-20"]);
  });

  it("closes with Escape", async () => {
    updatedTo("0.8.0", "0.7.1");
    render(<UpdatedDialog />);

    await screen.findByRole("dialog");
    fireEvent.keyDown(window, { key: "Escape" });

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(localStorage.getItem(SEEN_VERSION_KEY)).toBe("0.8.0");
  });

  it("stays away on a new installation", async () => {
    answer({ version: "0.8.0", updated: false, previousVersion: null, changesSince: null, updatedAt: null });
    render(<UpdatedDialog />);

    await waitFor(() => expect(localStorage.getItem(SEEN_VERSION_KEY)).toBe("0.8.0"));
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
