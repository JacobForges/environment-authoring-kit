// On mixamo.com (logged in): Console → paste this → Enter → THEN click Download once.
(function () {
  const orig = window.fetch.bind(window);
  window.fetch = function (input, init) {
    try {
      const h = init?.headers;
      let auth =
        (h && (h.Authorization || h.authorization)) ||
        (h && typeof h.get === "function" && h.get("Authorization"));
      if (!auth && h && typeof h === "object") {
        for (const [k, v] of Object.entries(h)) {
          if (k.toLowerCase() === "authorization") auth = v;
        }
      }
      if (auth && String(auth).includes("eyJ")) {
        window.__mixamoBearer = auth.startsWith("Bearer ") ? auth : "Bearer " + auth;
        console.log("✅ CAPTURED — copy from next line:");
        console.log(window.__mixamoBearer);
        copy(window.__mixamoBearer);
        console.log("(Also copied to clipboard if copy() works)");
      }
    } catch (e) {}
    return orig(input, init);
  };
  console.log("Hook ready. Click Download on any character (FBX Unity, With Skin).");
})();
