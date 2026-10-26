import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath, URL } from "node:url";

const pkg = JSON.parse(
  readFileSync(fileURLToPath(new URL("./package.json", import.meta.url)), "utf-8"),
) as { version: string };

/** Every released version's section of CHANGELOG.md, newest first, for the "what's new" dialog
 * after an update. Empty when the changelog isn't there. */
function releaseNotes(): { version: string; date: string; notes: string }[] {
  const path = fileURLToPath(new URL("../CHANGELOG.md", import.meta.url));
  if (!existsSync(path)) return [];

  const changelog = readFileSync(path, "utf-8").replace(/\r\n/g, "\n");
  const sections = [];
  for (const part of changelog.split(/^## /m).slice(1)) {
    const heading = /^\[([^\]]+)\](?: - (\S+))?/.exec(part);
    if (!heading || heading[1] === "Unreleased") continue;

    // The section ends at the next heading, or at the link list after the last one.
    const body = part.slice(part.indexOf("\n") + 1).split(/^\[Unreleased\]:/m)[0];
    sections.push({ version: heading[1], date: heading[2] ?? "", notes: body.trim() });
  }
  return sections;
}

export default defineConfig({
  plugins: [react()],
  define: {
    __APP_VERSION__: JSON.stringify(pkg.version),
    __RELEASE_NOTES__: JSON.stringify(releaseNotes()),
  },
  server: {
    proxy: {
      "/api": {
        target: "http://localhost:5215",
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: true,
  },
});
