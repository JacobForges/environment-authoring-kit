# Hub Game Test Plan

Automated play-mode smoke tests for Phase 2 gameplay wiring (`Assets/Scripts/`).

## Run tests

1. Enter Play Mode (solo gameplay with agent spawned).
2. Click **Run Game Tests** on the right HUD action rail (below Course).
3. Read pass/fail lines in the chat **System** tab.

Reports:

- `Assets/Scripts/HubGameTestReport.json` (editor Play Mode)
- `{persistentDataPath}/HubGameTestReport.json` (all builds)

## Test matrix

| Area | Test ID | Offline? | Notes |
|------|---------|----------|-------|
| bootstrap | `chat_host_ready` | yes | ChatQueueHost singleton after CompetitionBootstrap |
| chat | `command_parser_help` | yes | AgentCommandParser.IsHelpRequest |
| chat | `command_parser_hold` | pawn | AgentCommandParser.TryApply hold/stop |
| chat | `custom_command_registry` | yes | AgentCustomCommandRegistry add/apply/discard |
| chat | `chat_routing_global` | yes | AgentChatAddress @ prefix detection |
| chat | `command_help_body` | yes | AgentCommandHelp.Body non-empty |
| voice | `voice_self_test` | yes | HubVoiceSelfTest.Evaluate snapshot |
| voice | `transcript_filter_intent` | yes | HubVoiceTranscriptFilter accepts commands |
| voice | `transcript_filter_noise` | yes | HubVoiceTranscriptFilter rejects ambient |
| voice | `owner_voice_store` | yes | HubOwnerVoiceProfileStore API reachable |
| agent | `pawn_behavior_stack` | pawn | AgentBehaviorController + AgentCommandState on pawn |
| agent | `follow_command_ack` | pawn | follow command returns ack |
| agent | `course_builder_ui` | yes | PlayerCourseBuilderUi Ensure/Toggle types |
| train | `intelligence_stash_api` | yes | AgentModelIntelligenceStash HasUncaptured/Commit API |
| train | `adapter_train_runner` | yes | CompetitionAdapterTrainRunner booted |
| multiplayer | `chat_network_relay` | online | CompetitionChatNetworkRelay when connected |
| multiplayer | `challenge_coordinator` | yes | ChallengeBootstrap.Coordinator offline stub |
| hud | `chat_panel_buttons` | yes | CompetitionChatPanel Ensure + chip buttons |
| hud | `compass_action_rail` | yes | CompetitionCompassHud action buttons |
| mission | `mission_parser_lexicon` | yes | AgentMissionParser mission phrases |
| challenge | `challenge_chat_bridge` | yes | ChallengeChatBridge duel/challenge shortcuts |

## Known gaps (need Play Mode / online)

- Multiplayer chat relay end-to-end (host + client)
- Challenge/race invite delivery to remote player
- Voice STT live mic capture (device + tier dependent)
- Train Agent ONNX compile when sample threshold met
- Full agent follow/race locomotion over NavMesh

## Fix backlog

_Re-run **Run Game Tests** to populate from `HubGameTestReport.json` → `fixBacklog`._

## Last run

_Not run yet — click **Run Game Tests** in Play Mode._
