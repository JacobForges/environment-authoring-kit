import { downloadGameDirect } from "itchio-downloader";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const outDir = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../../Assets/_Import/Polyvania",
);

fs.mkdirSync(outDir, { recursive: true });

console.log("Downloading Polyvania .unitypackage to", outDir);
const result = await downloadGameDirect({
  itchGameUrl: "https://emaceart.itch.io/polyvania",
  downloadDirectory: outDir,
  platform: "unitypackage",
  resume: true,
});

console.log(JSON.stringify(result, null, 2));

if (result.status && result.filePath) {
  const src = result.filePath;
  const dest = path.join(outDir, "FREELowPolyTownMassiveCartoonPackVampiricPolyVania.unitypackage");
  if (path.resolve(src) !== path.resolve(dest)) {
    fs.copyFileSync(src, dest);
    console.log("Copied to", dest);
  }
}

process.exit(result.status ? 0 : 1);
