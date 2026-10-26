import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath, URL } from "node:url";

const pkg = JSON.parse(
  readFileSync(fileURLToPath(new URL("./package.json", import.meta.url)), "utf-8"),
) as { version: string };

/** This version's section of CHANGELOG.md (without its heading), for the "updated" dialog. Empty
 * when the changelog isn't there or has no section for the version. */
function releaseNotes(version: string): string {
  const path = fileURLToPath(new URL("../CHANGELOG.md", import.meta.url));
  if (!existsSync(path)) return "";

  const changelog = readFileSync(path, "utf-8").replace(/\r\n/g, "\n");
  const heading = `## [${version}]`;
  const start = changelog.split("\n").findIndex((line) => line.startsWith(heading));
  if (start < 0) return "";

  const lines = changelog.split("\n").slice(start + 1);
  const end = lines.findIndex((line) => line.startsWith("## ") || line.startsWith("[Unreleased]:"));
  return (end < 0 ? lines : lines.slice(0, end)).join("\n").trim();
}

export default defineConfig({
  plugins: [react()],
  define: {
    __APP_VERSION__: JSON.stringify(pkg.version),
    __RELEASE_NOTES__: JSON.stringify(releaseNotes(pkg.version)),
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
