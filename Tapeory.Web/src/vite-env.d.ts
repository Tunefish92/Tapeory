/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

declare const __APP_VERSION__: string;
/** This version's section of CHANGELOG.md, in Markdown; empty if there is none. */
declare const __RELEASE_NOTES__: string;
