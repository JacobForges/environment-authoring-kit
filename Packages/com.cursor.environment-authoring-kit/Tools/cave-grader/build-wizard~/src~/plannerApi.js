export function hubFromUrl() {
  return new URLSearchParams(window.location.search).get("hub") || "";
}

export { displayChatContent, streamingDuplicatesAssistant } from "./chatDisplay.js";

export function api(path, init = {}) {
  const hub = hubFromUrl();
  const sep = path.includes("?") ? "&" : "?";
  const url = hub ? `${path}${sep}hub=${encodeURIComponent(hub)}` : path;
  return fetch(url, {
    headers: { "Content-Type": "application/json", ...(init.headers || {}) },
    ...init,
  }).then(async (res) => {
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || res.statusText);
    return data;
  });
}

export function tabApi(tabId, action, init = {}) {
  const path =
    action === "auto-respond/step"
      ? `/api/${tabId}/auto-respond/step`
      : `/api/${tabId}/${action}`;
  return api(path, init);
}

export function tabPulse(tabId) {
  return api(`/api/${tabId}/pulse`);
}
