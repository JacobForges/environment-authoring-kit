import fs from "fs";
import path from "path";

export function loadFirebasePublicConfig() {
  const candidates = [
    path.join(process.cwd(), "firebase-applet-config.json"),
    path.join(process.cwd(), "..", "firebase-applet-config.json"),
  ];

  for (const p of candidates) {
    if (!fs.existsSync(p)) continue;
    try {
      const raw = JSON.parse(fs.readFileSync(p, "utf8"));
      return {
        enabled: true,
        apiKey: raw.apiKey,
        authDomain: raw.authDomain,
        projectId: raw.projectId,
        storageBucket: raw.storageBucket,
        messagingSenderId: raw.messagingSenderId,
        appId: raw.appId,
      };
    } catch {
      return { enabled: false, error: "Invalid firebase-applet-config.json" };
    }
  }

  return { enabled: false };
}
