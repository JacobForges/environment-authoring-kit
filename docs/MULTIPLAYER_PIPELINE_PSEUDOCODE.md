# Multiplayer × world-generation pipeline (pseudocode)

Free stack only: **Netcode for GameObjects + UGS Multiplayer (Relay + Lobby) + Vivox**.  
No ngrok for game traffic. Mac listen-server host; lobby Quick Join (no manual codes).

## Constants (free tier + Mac M4 Air)

```
LOBBY_NAME           = "JacobsPortfolioDemo"
MAX_PLAYERS_LOBBY    = 24          // session cap (cloud + listen-server)
MAX_PLAYERS_FREE_CCU = 50          // Unity Relay billing ceiling — do not use on this Mac
RELAY_MAX_CONNECTIONS = MAX_PLAYERS_LOBBY - 1   // excludes host
VIVOX_CHANNEL        = lobby.Id    // same as Lobby id
```

## End-to-end pipeline (tool + video + optional live session)

```text
PHASE 0 — PLAN (existing)
  wizard OR hand-edit JSON
    → CaveBuildPlannerBrief.json
    → CaveBuildActiveSessionConfig.json
  OUT: layout truth, tile scope, agentInvokes flag

PHASE 1 — GENERATE WORLD (existing, editor-only)
  Hub → Build (surface + queued cave 122 steps)
  WHILE build_running:
    record editor timelapse PNGs (DemoAutoRecorder, smart timelapse)
    publish CaveBuildLiveRunStatus.md
  ON step_121_finish:
    write CaveBuildQualityReport.json
    write completion artifacts
    → NEW: write CaveBuildWorldSessionManifest.json
        { seed, sceneName, buildUtc, qualityScore, buildAcceptable,
          surfaceTileCount, portalId, spawnPoints[] }

PHASE 2 — POST-BUILD PLAY CAPTURE (existing)
  PostBuildFinalizeGate.TryOfferPlayModeRecording()
  IF user_enters_play_mode:
    hold recap compose
    record Game view playthrough
  ON exit_play_mode:
    export generation prefab (CaveBuildGenerationPrefabExporter)
    save MainScene
    → NEW: stamp manifest.playthroughRecorded = true
    ReleaseComposeHoldAndFinalize() → recap compose

PHASE 3 — VIDEO (existing)
  DemoAutoRecorder → timelapse + timeline JSON
  RecapDashboardGate (optional) → compose silent → Personal Voice
  OUT: DemoCapture/<ts>/DemoRecapPresentation.mp4

PHASE 4 — PLAYABLE BUILD (portfolio)
  File → Build Settings → MainScene only
  Product Name: "Jacob's Portfolio Demo"
  OUT: JacobsPortfolioDemo.app

PHASE 5 — LIVE MULTIPLAYER (NEW, runtime in .app or editor playtest)
  // Runs AFTER world exists in scene — does not block generation
  ON app_start OR menu "Play Online":
    await UnityServices.InitializeAsync()
    await AuthenticationService.SignInAnonymouslyAsync()

  IF role == HOST (Jacob):
    allocation = Relay.CreateAllocationAsync(RELAY_MAX_CONNECTIONS)
    relayJoinCode = Relay.GetJoinCodeAsync(allocation)   // internal only
    lobby = Lobby.CreateLobbyAsync(
      name = LOBBY_NAME,
      maxPlayers = MAX_PLAYERS_LOBBY,
      isPrivate = false,
      data = { "relayJoinCode": relayJoinCode, "worldSeed": manifest.seed }
    )
    transport.SetRelayServerData(allocation)
    NetworkManager.StartHost()
    Vivox.JoinGroupChannelAsync(lobby.Id, AudioOnly)
    UI.show("Hosting · 1/24 · voice on")

  IF role == CLIENT (testers, automated):
    lobbies = Lobby.QueryLobbiesAsync(filter: name == LOBBY_NAME, orderBy: Latest)
    IF lobbies.empty:
      UI.show("Jacob isn't hosting right now")
      RETURN
    lobby = Lobby.JoinLobbyAsync(lobbies[0].Id)
    relayJoinCode = lobby.Data["relayJoinCode"]
    joinAllocation = Relay.JoinAllocationAsync(relayJoinCode)
    transport.SetRelayServerData(joinAllocation)
    NetworkManager.StartClient()
    Vivox.JoinGroupChannelAsync(lobby.Id, AudioOnly)

  ON NetworkManager.OnClientConnected:
    server_spawn_player_at_next_spawn_point(clientId)
    // spawn points from manifest or PlayerSpawnPoint children

  ON lobby_full OR connectedPlayers >= MAX_PLAYERS_LOBBY:
    reject_new_joins()

  ON host_disconnect:
    lobby_closes()
    clients_show("Session ended")

PHASE 6 — SHIP BUNDLE (portfolio folder, not git)
  Jacobs-Portfolio-Demo/
    play/JacobsPortfolioDemo.app
    proof/DemoRecapPresentation.mp4
    proof/CaveBuildWorldSessionManifest.json
    README.md
```

## Where hooks land in existing C# (editor)

```text
CaveBuildPostBuildFinalizeGate.RunFinalizeChain(playthroughRecorded):
  AFTER prefab export + scene save:
    CaveBuildWorldSessionManifest.Write(scene, roll.seed, quality)
    // do NOT start multiplayer here — editor host is wrong surface

LavaTubeCaveBuildPipeline (step 121 / finish):
  CaveBuildWorldSessionManifest.Write(...)

EnvironmentKitHubWindow (new foldout, later):
  toggle "Enable portfolio multiplayer in builds"
  slider maxPlayers capped at 24
```

## Network spawn (runtime, MainScene)

```text
ON server_started:
  FOR each existing world object (terrain, cave, portals):
    // already in scene from pipeline — NO network spawn for static world
  network_spawn ONLY:
    - DemoPlayer prefab (per client)
    - optional: synced enemies (v2 — defer for portfolio v1)

PlayerSpawnPoint[0] = host
PlayerSpawnPoint[1..11] = clients
```

## What we defer (v2)

```text
- Synced combat / enemy AI authority
- Host migration when Jacob leaves
- Dedicated Linux headless server
- 50-player Relay cap (free tier; not Mac realistic)
```

## Free-tier guardrails

```text
ON lobby_create:
  ASSERT maxPlayers <= 50
  RECOMMEND maxPlayers <= 24 per session

Unity Dashboard org:
  NO payment method → hard stop if free tier exceeded
  Budget alerts at 75% / 100% Relay CCU + Vivox PCU
```

See [MULTIPLAYER_FREE_SETUP.md](./MULTIPLAYER_FREE_SETUP.md) for package install + Dashboard steps.
