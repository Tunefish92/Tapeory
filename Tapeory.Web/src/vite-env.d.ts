/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

declare const __APP_VERSION__: string;
/** Every released version's section of CHANGELOG.md (Markdown), newest first. */
declare const __RELEASE_NOTES__: { version: string; date: string; notes: string }[];
