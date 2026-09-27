const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

/** Fired when an API call answers 401: the session ended (signed out elsewhere, disabled, expired). */
export const SESSION_ENDED_EVENT = "tapeory:session-ended";

/** Tapeory's own API, as opposed to anything else the page might fetch. */
function isApiUrl(input: RequestInfo | URL): boolean {
  const raw = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
  const url = new URL(raw, window.location.href);
  return url.origin === window.location.origin && url.pathname.startsWith(API_BASE_URL);
}

/**
 * Wraps fetch once, at startup, for every API call: changes carry the header the server requires
 * with a session (a form on another site can't send it), and a 401 tells the app the session is
 * gone so it can show the sign-in page. Sign-in calls themselves answer failures with 400.
 */
export function installApiFetch() {
  const original = window.fetch.bind(window);

  window.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    if (!isApiUrl(input)) {
      return original(input, init);
    }

    const method = (init?.method ?? (input instanceof Request ? input.method : "GET")).toUpperCase();

    if (method !== "GET" && method !== "HEAD") {
      const headers = new Headers(init?.headers ?? (input instanceof Request ? input.headers : undefined));
      headers.set("X-Requested-With", "Tapeory");
      init = { ...init, headers };
    }

    const response = await original(input, init);

    if (response.status === 401) {
      window.dispatchEvent(new Event(SESSION_ENDED_EVENT));
    }

    return response;
  };
}
