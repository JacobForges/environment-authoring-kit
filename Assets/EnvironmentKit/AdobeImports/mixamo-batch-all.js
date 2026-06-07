// Paste ALL of this into Safari/Chrome Console on https://www.mixamo.com (logged in).
// Step 1: Click Download on ANY character once (FBX Unity, With Skin).
// Step 2: Paste this script, press Enter. Step 3: When prompted, click OK — downloads start.
(async function mixamoBatchAll() {
  const chars = [
    { slot: "NPC_PlayGuide", name: "Remy", id: "037852b5-74da-44aa-878b-eccda13e5139" },
    { slot: "NPC_PlayMerchant", name: "Sophie", id: "61bcdb20-7b85-4f2e-a109-2a0fbd54af78" },
    { slot: "NPC_FoothillRanger", name: "Survivor A Lusth", id: "52dcdacb-b43e-4efc-ab6d-9d2d6e09bc95" },
    { slot: "NPC_FoothillHerbalist", name: "Kate", id: "aba68976-90b0-4c6d-9f96-922dd1644be5" },
    { slot: "NPC_PeakHermit", name: "Romero", id: "576b18a3-2e3e-4f50-b665-cbca337e0757" },
    { slot: "NPC_PeakOreTrader", name: "Peasant Man", id: "5fb4b535-034a-4011-af3b-2880391547a5" },
    { slot: "NPC_PeakGuard_A", name: "Swat Guy", id: "cf73b862-b7ca-40e9-a156-1b95393d232e" },
    { slot: "NPC_HorizonWatcher", name: "Mannequin", id: "999a29c0-8fb0-42d4-92c8-5b39ca94bd75" },
    { slot: "NPC_Ambient_A", name: "Amy", id: "24c3eeb4-6c47-419d-a593-f7b2948b74c7" },
    { slot: "NPC_Ambient_B", name: "Brian", id: "a58c06c4-3307-40e6-a02d-bfcd658bdbff" },
    { slot: "NPC_Ambient_C", name: "Elizabeth", id: "5a0e290c-92ea-42e4-afea-cb94ba3fab6d" },
    { slot: "BOSS_Stage", name: "Warrok W Kurniawan", id: "efb06b46-a470-49b2-b7da-a06755d4dba7" },
    { slot: "BOSS_Add_1", name: "Mutant", id: "cccc84b6-d072-4972-99da-75c5702e25f6" },
    { slot: "BOSS_Add_2", name: "Vampire A Lusth", id: "90815396-6b00-4efc-b670-4c3497dbb605" },
    { slot: "ENEMY_Foothill_1", name: "Zombiegirl W Kurniawan", id: "2f8e576f-f69d-453e-830e-969a2f0217ea" },
    { slot: "ENEMY_Peak_1", name: "Warzombie F Pedroso", id: "3576fd60-beef-49ec-a3d0-f93231f4fc29" },
    { slot: "ENEMY_Annex_1", name: "Skeletonzombie T Avelange", id: "91d02eaa-1b0a-4d34-b859-01bcd092c713" },
  ];
  const seen = new Set();
  const list = chars.filter((c) => !seen.has(c.id) && seen.add(c.id));

  let bearer = window.__mixamoBearer;
  if (!bearer) {
    bearer = prompt(
      "Paste Authorization from Network → export → Request Headers (full Bearer eyJ... string):"
    );
  }
  if (!bearer || !bearer.includes("eyJ")) {
    console.error("Need real Bearer eyJ... token (not job_result URL, not …)");
    return;
  }
  if (!bearer.startsWith("Bearer ")) bearer = "Bearer " + bearer.trim();

  const headers = {
    Accept: "application/json",
    "Content-Type": "application/json",
    "X-Api-Key": "mixamo2",
    "X-Requested-With": "XMLHttpRequest",
    Authorization: bearer,
  };

  const wait = (ms) => new Promise((r) => setTimeout(r, ms));

  async function monitor(characterId) {
    const url = `https://www.mixamo.com/api/v1/characters/${characterId}/monitor`;
    for (let i = 0; i < 90; i++) {
      const res = await fetch(url, { headers });
      const json = await res.json();
      if (json.status === "completed" && json.job_result) return json.job_result;
      if (json.status === "failed") throw new Error(json.message || "failed");
      await wait(2000);
    }
    throw new Error("timeout");
  }

  const tab = window.open("about:blank", "_blank");
  for (const c of list) {
    console.log("Export", c.slot, c.name);
    const res = await fetch("https://www.mixamo.com/api/v1/animations/export", {
      method: "POST",
      headers,
      body: JSON.stringify({
        character_id: c.id,
        gms_hash: [],
        preferences: { format: "fbx7_2019", skin: "true", fps: "30", reducekf: "0" },
        product_name: c.name,
        type: "Character",
      }),
    });
    if (!res.ok) {
      console.warn("FAIL export", c.slot, res.status);
      continue;
    }
    const job = await res.json();
    const fbxUrl =
      job.job_result || (job.status === "processing" ? await monitor(c.id) : null);
    if (!fbxUrl) {
      console.warn("FAIL no url", c.slot);
      continue;
    }
    tab.location.href = fbxUrl;
    console.log("Download", c.slot);
    await wait(18000);
  }
  console.log("Done — move FBX from Downloads to Assets/EnvironmentKit/AdobeImports/Mixamo/Characters/ and rename to slot names.");
})();
