// Injected into mixamo.com while you're logged in — no Bearer paste needed.
(async () => {
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
  const _fetch = window.fetch.bind(window);
  if (!bearer) {
    window.fetch = function (input, init) {
      const h = init && init.headers;
      let auth = h && (h.Authorization || h.authorization);
      if (!auth && h && typeof h.get === "function") auth = h.get("Authorization");
      if (auth && String(auth).includes("eyJ")) window.__mixamoBearer = auth;
      return _fetch(input, init);
    };
    alert("Click Download on Remy once (FBX Unity, With Skin), then run mixamoContinue() in Console.");
    window.mixamoContinue = async () => {
      window.fetch = _fetch;
      bearer = window.__mixamoBearer;
      if (!bearer) {
        alert("No token yet — click Download first.");
        return;
      }
      await runBatch(bearer, list);
    };
    return "Waiting for one Download click, then type: mixamoContinue()";
  }
  await runBatch(bearer, list);

  async function runBatch(bearer, list) {
    const headers = {
      Accept: "application/json",
      "Content-Type": "application/json",
      "X-Api-Key": "mixamo2",
      "X-Requested-With": "XMLHttpRequest",
      Authorization: bearer.startsWith("Bearer ") ? bearer : "Bearer " + bearer,
    };
    const wait = (ms) => new Promise((r) => setTimeout(r, ms));
    async function monitor(characterId) {
      const url = `https://www.mixamo.com/api/v1/characters/${characterId}/monitor`;
      for (let i = 0; i < 90; i++) {
        const res = await fetch(url, { headers });
        const json = await res.json();
        if (json.status === "completed" && json.job_result) return json.job_result;
        if (json.status === "failed") throw new Error("failed");
        await wait(2000);
      }
      throw new Error("timeout");
    }
    const tab = window.open("about:blank", "_blank");
    for (const c of list) {
      console.log("Export", c.slot);
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
        console.warn("FAIL", c.slot, res.status);
        continue;
      }
      const job = await res.json();
      const fbxUrl =
        job.job_result || (job.status === "processing" ? await monitor(c.id) : null);
      if (fbxUrl) {
        tab.location.href = fbxUrl;
        await wait(18000);
      }
    }
    alert("Downloads started — check Downloads folder. Move/rename to Mixamo/Characters/");
  }
})();
