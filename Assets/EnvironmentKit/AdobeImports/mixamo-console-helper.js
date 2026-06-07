// Mixamo batch — run on https://www.mixamo.com while logged in.
// 1. Open Characters, search each name, Download FBX For Unity, With Skin
// 2. Save to Assets/EnvironmentKit/AdobeImports/Mixamo/Characters/
const characters = [
  {
    "slot": "NPC_PlayGuide",
    "name": "Remy",
    "id": "037852b5-74da-44aa-878b-eccda13e5139",
    "url": "https://www.mixamo.com/#/?page=1&query=Remy&type=Character"
  },
  {
    "slot": "NPC_PlayMerchant",
    "name": "Sophie",
    "id": "61bcdb20-7b85-4f2e-a109-2a0fbd54af78",
    "url": "https://www.mixamo.com/#/?page=1&query=Sophie&type=Character"
  },
  {
    "slot": "NPC_FoothillRanger",
    "name": "Survivor A Lusth",
    "id": "52dcdacb-b43e-4efc-ab6d-9d2d6e09bc95",
    "url": "https://www.mixamo.com/#/?page=1&query=Survivor&type=Character"
  },
  {
    "slot": "NPC_FoothillHerbalist",
    "name": "Kate",
    "id": "aba68976-90b0-4c6d-9f96-922dd1644be5",
    "url": "https://www.mixamo.com/#/?page=1&query=Kate&type=Character"
  },
  {
    "slot": "NPC_PeakHermit",
    "name": "Romero",
    "id": "576b18a3-2e3e-4f50-b665-cbca337e0757",
    "url": "https://www.mixamo.com/#/?page=1&query=Romero&type=Character"
  },
  {
    "slot": "NPC_PeakOreTrader",
    "name": "Peasant Man",
    "id": "5fb4b535-034a-4011-af3b-2880391547a5",
    "url": "https://www.mixamo.com/#/?page=1&query=Peasant&type=Character"
  },
  {
    "slot": "NPC_PeakGuard_A",
    "name": "Swat Guy",
    "id": "cf73b862-b7ca-40e9-a156-1b95393d232e",
    "url": "https://www.mixamo.com/#/?page=1&query=Swat&type=Character"
  },
  {
    "slot": "NPC_PeakGuard_B",
    "name": "Swat Guy",
    "id": "cf73b862-b7ca-40e9-a156-1b95393d232e",
    "url": "https://www.mixamo.com/#/?page=1&query=Swat&type=Character"
  },
  {
    "slot": "NPC_HorizonWatcher",
    "name": "Mannequin",
    "id": "999a29c0-8fb0-42d4-92c8-5b39ca94bd75",
    "url": "https://www.mixamo.com/#/?page=1&query=Mannequin&type=Character"
  },
  {
    "slot": "NPC_Ambient_A",
    "name": "Amy",
    "id": "24c3eeb4-6c47-419d-a593-f7b2948b74c7",
    "url": "https://www.mixamo.com/#/?page=1&query=Amy&type=Character"
  },
  {
    "slot": "NPC_Ambient_B",
    "name": "Brian",
    "id": "a58c06c4-3307-40e6-a02d-bfcd658bdbff",
    "url": "https://www.mixamo.com/#/?page=1&query=Brian&type=Character"
  },
  {
    "slot": "NPC_Ambient_C",
    "name": "Elizabeth",
    "id": "5a0e290c-92ea-42e4-afea-cb94ba3fab6d",
    "url": "https://www.mixamo.com/#/?page=1&query=Elizabeth&type=Character"
  },
  {
    "slot": "BOSS_Stage",
    "name": "Warrok W Kurniawan",
    "id": "efb06b46-a470-49b2-b7da-a06755d4dba7",
    "url": "https://www.mixamo.com/#/?page=1&query=Warrok&type=Character"
  },
  {
    "slot": "BOSS_Add_1",
    "name": "Mutant",
    "id": "cccc84b6-d072-4972-99da-75c5702e25f6",
    "url": "https://www.mixamo.com/#/?page=1&query=Mutant&type=Character"
  },
  {
    "slot": "BOSS_Add_2",
    "name": "Vampire A Lusth",
    "id": "90815396-6b00-4efc-b670-4c3497dbb605",
    "url": "https://www.mixamo.com/#/?page=1&query=Vampire&type=Character"
  },
  {
    "slot": "ENEMY_Foothill_1",
    "name": "Zombiegirl W Kurniawan",
    "id": "2f8e576f-f69d-453e-830e-969a2f0217ea",
    "url": "https://www.mixamo.com/#/?page=1&query=Zombiegirl&type=Character"
  },
  {
    "slot": "ENEMY_Peak_1",
    "name": "Warzombie F Pedroso",
    "id": "3576fd60-beef-49ec-a3d0-f93231f4fc29",
    "url": "https://www.mixamo.com/#/?page=1&query=Warzombie&type=Character"
  },
  {
    "slot": "ENEMY_Annex_1",
    "name": "Skeletonzombie T Avelange",
    "id": "91d02eaa-1b0a-4d34-b859-01bcd092c713",
    "url": "https://www.mixamo.com/#/?page=1&query=Skeletonzombie&type=Character"
  }
];
console.table(characters);
