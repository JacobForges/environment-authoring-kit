using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public enum EnvironmentKitAiProvider
    {
        Cursor = 0,
        GoogleGemini = 1,
        AnthropicClaude = 2,
        OpenAICompatible = 3,
        OpenRouter = 4,
        LocalOllama = 5,
        LocalLmStudio = 6,
        CustomEndpoint = 7,
    }

    public sealed class CaveBuildCursorSettings : ScriptableObject
    {
        public const string AssetPath =
            "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/CaveBuildCursorSettings.asset";

        const string PrefApiKey = "CaveBuild_CursorApiKey";
        const string PrefGoogleApiKey = "CaveBuild_GoogleApiKey";
        const string PrefAnthropicApiKey = "CaveBuild_AnthropicApiKey";
        const string PrefOpenAiApiKey = "CaveBuild_OpenAiApiKey";
        const string PrefOpenRouterApiKey = "CaveBuild_OpenRouterApiKey";
        const string PrefCustomApiKey = "CaveBuild_CustomApiKey";
        const string PrefHubRoot = "CaveBuild_HubRoot";
        const string PrefModelId = "CaveBuild_CursorModelId";
        const string PrefAiProvider = "CaveBuild_AiProvider";
        const string PrefAutoInvoke = "CaveBuild_AutoInvokeOnDud";
        const string PrefAutoInvokeEveryBuild = "CaveBuild_AutoInvokeEveryBuild";
        const string PrefAutoRebuildAfterAgent = "CaveBuild_AutoRebuildAfterAgent";
        const string PrefAutoInvokeEachMeatPass = "CaveBuild_AutoInvokeEachMeatPass";
        const string PrefAutoInvokePreBuild = "CaveBuild_AutoInvokePreBuildWorkflow";
        const string PrefEnforcePreBuildGate = "CaveBuild_EnforcePreBuildGate";
        const string PrefPreBuildReloopUntilPass = "CaveBuild_PreBuildReloopUntilPass";
        const string PrefMaxPreBuildReloopAttempts = "CaveBuild_MaxPreBuildReloopAttempts";
        const string PrefAutoInvokeTerrain = "CaveBuild_AutoInvokeTerrainAfterSurface";
        const string PrefAutoRebuildSurface = "CaveBuild_AutoRebuildSurfaceAfterTerrainAgent";
        const string PrefWatchTerrainGrade = "CaveBuild_WatchTerrainGradeJson";
        const string PrefStabilizationMode = "CaveBuild_StabilizationMode";
        const string PrefQueuedStepTimeoutSeconds = "CaveBuild_QueuedStepTimeoutSeconds";

        [Tooltip("Unity project root (Hub repo) for Cursor SDK local cwd.")]
        public string hubProjectRoot = "";

        [Tooltip("Optional full path to node binary (e.g. /usr/local/bin/node). Leave empty to auto-detect.")]
        public string nodeExecutablePath = "";

        [Tooltip("Optional folder containing node/npx (e.g. /usr/local/bin).")]
        public string nodeBinDirectory = "";

        public string modelId = "auto";

        [Header("AI provider")]
        [Tooltip("Active model provider shown in Hub. Cave grader automation currently runs on Cursor SDK.")]
        public EnvironmentKitAiProvider aiProvider = EnvironmentKitAiProvider.Cursor;

        [Tooltip("OpenAI-compatible base URL (for external/local providers).")]
        public string openAiCompatibleBaseUrl = "http://localhost:11434/v1";

        [Tooltip("Gemini model id used for external tool routing display (optional).")]
        public string googleModelId = "gemini-2.5-flash";

        [Tooltip("Anthropic model id used for external tool routing display (optional).")]
        public string anthropicModelId = "claude-3-7-sonnet-latest";

        [Tooltip("OpenAI-compatible model id for local/cloud OpenAI endpoints.")]
        public string openAiModelId = "gpt-4.1-mini";

        [Tooltip("OpenRouter model id (optional).")]
        public string openRouterModelId = "openai/gpt-4.1-mini";

        [Tooltip("Ollama model id, e.g. qwen2.5-coder:14b.")]
        public string ollamaModelId = "qwen2.5-coder:14b";

        [Tooltip("LM Studio model id (optional).")]
        public string lmStudioModelId = "local-model";

        [Tooltip("Custom provider label shown in Hub.")]
        public string customProviderLabel = "Custom Endpoint";

        [Tooltip("Allow non-Cursor providers to apply file edits from structured JSON output.")]
        public bool allowExternalProviderEdits;

        [Tooltip("When external edit execution is enabled, preview edits only and do not write files.")]
        public bool externalProviderEditsDryRun = true;

        [Tooltip("Run post-build Cursor workflow when the build is below Ship (95+). Skipped automatically when Ship target is met.")]
        public bool autoInvokeAfterEveryBuild = false;

        [Tooltip("Also run on dud builds when autoInvokeAfterEveryBuild is off.")]
        public bool autoInvokeOnDud = true;

        [Tooltip("After Cursor agent exits OK, wait for script compile then run Build Complete Cave once (applies code fixes to the scene).")]
        public bool autoRebuildAfterAgentSuccess = false;

        [Tooltip("During meat loop, invoke Cursor after each pass once quality + companion JSON files are written.")]
        public bool autoInvokeEachMeatLoopPass = true;

        [Tooltip("When on, meat loop never starts Cursor per pass. Off = automated AI fixes during terrain/cave meat.")]
        public bool suppressMeatLoopCursorInvokes = false;

        [Tooltip("When pre-build ladder fails, export workflow JSON and start Cursor (research → plan → compile → readiness ladder).")]
        public bool autoInvokePreBuildWorkflow = false;

        [Tooltip("Block Build Complete Cave until pre-build ladder score is acceptable (skipped for layout prototype).")]
        public bool enforcePreBuildGate = true;

        [Tooltip(
            "Automated FullWorld: on pre-build failure, apply local fixes and re-run the gate until score ≥ 88 " +
            "(no advisory bypass).")]
        public bool preBuildReloopUntilPass = true;

        [Tooltip("Max local pre-build gate retries before deferring to Cursor pre-build workflow.")]
        public int maxPreBuildReloopAttempts = 12;

        [Tooltip(
            "During validate, skip blocking research-cache-sync / hillshade / catalog tsx when ResearchCache is already on disk.")]
        public bool skipResearchNetworkSyncWhenCachePresent = true;

        [Tooltip("After pre-build Cursor workflow succeeds, automatically run cave geometry.")]
        public bool autoContinueAfterPreBuildCursor = true;

        [Tooltip("Spread full cave builds across paced queue phases (reduces editor overload / crashes).")]
        public bool usePhasedCaveBuild = true;

        [Tooltip("Scene-view LIVE BUILD banner only (non-intrusive). Camera framing/selection ping stay off in stabilization mode.")]
        public bool showLiveScenePlacement = true;

        [Header("AAA ladder (incremental + contracts)")]
        [Tooltip("Skip ladder rungs whose output artifacts already exist for this seed (invalidate downstream only).")]
        public bool useIncrementalLadder = true;

        [Tooltip("On rung failure, write CaveBuildConstrainedRepairBrief.json limiting bot to ResearchCache entries.")]
        public bool constrainBotToResearchOnFailure = true;

        [Tooltip("After Build Complete Cave, schedule Play Mode route + combat bots (runs on next Enter Play Mode).")]
        public bool autoRunPlaytestBotAfterBuild = false;

        [Header("Batch builds")]
        [Tooltip("When on, Build Complete Cave queues N jobs with auto-incrementing seeds (also available via batch menu).")]
        public bool enableBatchMode;

        [Tooltip("Number of complete cave builds per batch run.")]
        public int batchJobCount = 3;

        [Tooltip("Seconds between batch jobs (main-thread paced sleep).")]
        public float batchDelaySeconds = 0.5f;

        [Tooltip("Added to seed for each subsequent batch job.")]
        public int batchSeedIncrement = 10007;

        [Header("Post-build research phase")]
        [Tooltip("Run dedicated research queue steps after post-meat (catalog + enrichment JSON).")]
        public bool runPostBuildResearchPhase = true;

        [Tooltip("When research phase runs and strict target is not met, invoke Cursor research rung.")]
        public bool invokeCursorOnResearchPhase = true;

        [Header("Terrain grader")]
        [Tooltip("After surface world build + terrain AI phases, auto-start terrain Cursor workflow when grade is below target.")]
        public bool autoInvokeTerrainAfterSurfaceBuild = true;

        [Tooltip("After terrain Cursor workflow succeeds, rebuild surface world to apply C# fixes.")]
        public bool autoRebuildSurfaceAfterTerrainAgent = false;

        [Tooltip("Log hint to run npm run watch-terrain-grade when terrain JSON is written (CLI watcher).")]
        public bool suggestTerrainGradeWatcher = true;

        [Header("Stabilization guardrails")]
        [Tooltip("When on, enforce one automation loop at a time (disable per-pass invokes + auto-rebuild while stabilizing).")]
        public bool stabilizationMode = true;

        [Tooltip("Fail fast if any queued macro step runs longer than this many seconds (0 disables watchdog).")]
        public float queuedStepTimeoutSeconds = 180f;

        [Tooltip(
            "FullWorld: defer underground cave meat loop until above-ground terrain grading finishes. " +
            "Off runs cave meat as soon as the queue reaches step 48.")]
        public bool waitForTerrainBeforeCaveMeat = true;

        [Tooltip(
            "Max seconds to wait at cave meat step 48 for terrain (0 = 900s default). " +
            "After timeout, cave meat proceeds with a warning.")]
        public float caveMeatTerrainWaitTimeoutSeconds = 900f;

        [Header("Autonomous fix loop")]
        [Tooltip("After build, run phase prompts + Next Steps / DO NOT lists + Cursor until Ship (95+) or max iterations.")]
        public bool enableAutonomousUntilShip = true;

        [Tooltip("Max Cursor fix iterations after a full build (each exports fresh prompts + re-grades).")]
        public int maxAutonomousIterations = 8;

        [Tooltip("Seconds between autonomous iterations (reduces editor overload).")]
        public float autonomousIterationCooldownSeconds = 4f;

        [Tooltip("Log agent completion instead of Cursor Agent modal (keeps start + finish dialogs only).")]
        public bool suppressMidBuildDialogs = true;

        [Tooltip("Show one optional dialog when autonomous loop starts another iteration.")]
        public bool showReloopDialog;

        [Header("Generation prefab export")]
        [Tooltip(
            "After the full build + Cursor autonomous loop finish (not during meat loop or mid-pipeline), " +
            "save EnvironmentRoot (surface + cave) as a prefab under Assets/EnvironmentKit/Generated/Prefabs/.")]
        public bool exportGenerationPrefabWhenFinished = true;

        [Header("Reliable FullWorld run")]
        [Tooltip("Run speed/quality/creative enhancement hooks during FullWorld builds.")]
        public bool enableEnhancementPhases = true;

        [Tooltip("DEM elevation-grid supersample target (64–256). 128 = balanced; 256 = slower, sharper mouth.")]
        public int demSupersampleTargetDim = 128;

        [Tooltip("When on, FullWorld build prompts to run preflight checklist first.")]
        public bool requirePreflightBeforeFullWorldBuild = true;

        [Header("Hardware budget (GPU RAM)")]
        [Tooltip("MacBook Air 16GB — smaller terrain heightmaps/textures, fewer GPU-heavy features; uses more paced CPU steps.")]
        public EnvironmentKitHardwareBudget.Preset hardwareBudget = EnvironmentKitHardwareBudget.Preset.Default;

        [Header("Editor queue pacing")]
        [Tooltip("Base gap for light queue items (seconds). Normal/heavy scale from this.")]
        public float editorQueueLightBase = CaveBuildActionPacing.LightDelaySeconds;

        [Tooltip("Pre-delay multiplier for normal items (× light base).")]
        public float editorQueueNormalMultiplier = 3f;

        [Tooltip("Pre-delay multiplier for heavy items (× light base).")]
        public float editorQueueHeavyMultiplier = 12f;

        [Tooltip("Extra load multiplier per item already waiting in queue.")]
        public float editorQueueLoadStepPerItem = 0.08f;

        [Tooltip("Cap on load-based delay scaling.")]
        public float editorQueueMaxLoadMultiplier = 2.5f;

        [Tooltip("Light/normal items to run back-to-back per batch before the batch cooldown sleep (1–2 keeps the editor responsive).")]
        public int editorQueueBatchSize = 1;

        [Tooltip(
            "Echo paced build info/warnings to Unity Console. Off (default) keeps Console errors-only during builds; Pipeline Console + Hub still show all messages.")]
        public bool mirrorPacedBuildLogsToConsole;

        public readonly struct QueuePacing
        {
            public readonly float lightBase;
            public readonly float normalPreMult;
            public readonly float heavyPreMult;
            public readonly float cooldownLightMult;
            public readonly float cooldownNormalMult;
            public readonly float cooldownHeavyMult;
            public readonly float loadStepPerQueuedItem;
            public readonly float maxLoadMultiplier;
            public readonly float loadBoostHeavyActive;
            public readonly float loadBoostAfterHeavy;
            public readonly float loadBoostCompiling;
            public readonly float loadBoostHeavyScheduled;
            public readonly int batchSize;

            public QueuePacing(
                float lightBase,
                float normalPreMult,
                float heavyPreMult,
                float cooldownLightMult,
                float cooldownNormalMult,
                float cooldownHeavyMult,
                float loadStepPerQueuedItem,
                float maxLoadMultiplier,
                float loadBoostHeavyActive,
                float loadBoostAfterHeavy,
                float loadBoostCompiling,
                float loadBoostHeavyScheduled,
                int batchSize)
            {
                this.lightBase = lightBase;
                this.normalPreMult = normalPreMult;
                this.heavyPreMult = heavyPreMult;
                this.cooldownLightMult = cooldownLightMult;
                this.cooldownNormalMult = cooldownNormalMult;
                this.cooldownHeavyMult = cooldownHeavyMult;
                this.loadStepPerQueuedItem = loadStepPerQueuedItem;
                this.maxLoadMultiplier = maxLoadMultiplier;
                this.loadBoostHeavyActive = loadBoostHeavyActive;
                this.loadBoostAfterHeavy = loadBoostAfterHeavy;
                this.loadBoostCompiling = loadBoostCompiling;
                this.loadBoostHeavyScheduled = loadBoostHeavyScheduled;
                this.batchSize = batchSize;
            }

            public float NormalDelayAtIdle => lightBase * normalPreMult;
            public float HeavyDelayAtIdle => lightBase * heavyPreMult;
        }

        public static QueuePacing ResolveQueuePacing()
        {
            var s = LoadOrCreate();
            s.LoadFromPrefs();
            return new QueuePacing(
                Mathf.Max(0.02f, s.editorQueueLightBase),
                Mathf.Max(1f, s.editorQueueNormalMultiplier),
                Mathf.Max(2f, s.editorQueueHeavyMultiplier),
                1f,
                3f,
                8f,
                Mathf.Max(0f, s.editorQueueLoadStepPerItem),
                Mathf.Max(1.1f, s.editorQueueMaxLoadMultiplier),
                0.15f,
                0.12f,
                0.25f,
                0.1f,
                Mathf.Clamp(s.editorQueueBatchSize, 1, 8));
        }

        /// <summary>Idle normal delay (for log messages).</summary>
        public static PacingSeconds ResolvePacingSeconds()
        {
            var q = ResolveQueuePacing();
            return new PacingSeconds(
                q.lightBase,
                q.NormalDelayAtIdle,
                q.HeavyDelayAtIdle,
                q.lightBase,
                q.lightBase * 3f,
                q.lightBase * 8f);
        }

        public readonly struct PacingSeconds
        {
            public readonly float lightDelay;
            public readonly float normalDelay;
            public readonly float heavyDelay;
            public readonly float cooldownLight;
            public readonly float cooldownNormal;
            public readonly float cooldownHeavy;

            public PacingSeconds(
                float lightDelay,
                float normalDelay,
                float heavyDelay,
                float cooldownLight,
                float cooldownNormal,
                float cooldownHeavy)
            {
                this.lightDelay = lightDelay;
                this.normalDelay = normalDelay;
                this.heavyDelay = heavyDelay;
                this.cooldownLight = cooldownLight;
                this.cooldownNormal = cooldownNormal;
                this.cooldownHeavy = cooldownHeavy;
            }
        }

        public static CaveBuildCursorSettings LoadOrCreate()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CaveBuildCursorSettings>(AssetPath);
            if (settings != null)
                return settings;

            settings = CreateInstance<CaveBuildCursorSettings>();
            var dir = System.IO.Path.GetDirectoryName(AssetPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                if (!AssetDatabase.IsValidFolder("Packages/com.cursor.environment-authoring-kit/Editor/Blockout"))
                    AssetDatabase.CreateFolder("Packages/com.cursor.environment-authoring-kit/Editor", "Blockout");
            }

            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        public string GetApiKey() =>
            EditorPrefs.GetString(PrefApiKey, string.Empty);

        public void SetApiKey(string key) =>
            EditorPrefs.SetString(PrefApiKey, key ?? string.Empty);

        public string GetApiKey(EnvironmentKitAiProvider provider) =>
            provider switch
            {
                EnvironmentKitAiProvider.Cursor => EditorPrefs.GetString(PrefApiKey, string.Empty),
                EnvironmentKitAiProvider.GoogleGemini => EditorPrefs.GetString(PrefGoogleApiKey, string.Empty),
                EnvironmentKitAiProvider.AnthropicClaude => EditorPrefs.GetString(PrefAnthropicApiKey, string.Empty),
                EnvironmentKitAiProvider.OpenAICompatible => EditorPrefs.GetString(PrefOpenAiApiKey, string.Empty),
                EnvironmentKitAiProvider.OpenRouter => EditorPrefs.GetString(PrefOpenRouterApiKey, string.Empty),
                EnvironmentKitAiProvider.CustomEndpoint => EditorPrefs.GetString(PrefCustomApiKey, string.Empty),
                _ => string.Empty,
            };

        public void SetApiKey(EnvironmentKitAiProvider provider, string key)
        {
            var value = key ?? string.Empty;
            switch (provider)
            {
                case EnvironmentKitAiProvider.Cursor:
                    EditorPrefs.SetString(PrefApiKey, value);
                    break;
                case EnvironmentKitAiProvider.GoogleGemini:
                    EditorPrefs.SetString(PrefGoogleApiKey, value);
                    break;
                case EnvironmentKitAiProvider.AnthropicClaude:
                    EditorPrefs.SetString(PrefAnthropicApiKey, value);
                    break;
                case EnvironmentKitAiProvider.OpenAICompatible:
                    EditorPrefs.SetString(PrefOpenAiApiKey, value);
                    break;
                case EnvironmentKitAiProvider.OpenRouter:
                    EditorPrefs.SetString(PrefOpenRouterApiKey, value);
                    break;
                case EnvironmentKitAiProvider.CustomEndpoint:
                    EditorPrefs.SetString(PrefCustomApiKey, value);
                    break;
            }
        }

        public void LoadFromPrefs()
        {
            hubProjectRoot = EditorPrefs.GetString(PrefHubRoot, hubProjectRoot);
            modelId = EditorPrefs.GetString(PrefModelId, modelId);
            aiProvider = (EnvironmentKitAiProvider)EditorPrefs.GetInt(PrefAiProvider, (int)aiProvider);
            openAiCompatibleBaseUrl = EditorPrefs.GetString("CaveBuild_OpenAiBaseUrl", openAiCompatibleBaseUrl);
            googleModelId = EditorPrefs.GetString("CaveBuild_GoogleModelId", googleModelId);
            anthropicModelId = EditorPrefs.GetString("CaveBuild_AnthropicModelId", anthropicModelId);
            openAiModelId = EditorPrefs.GetString("CaveBuild_OpenAiModelId", openAiModelId);
            openRouterModelId = EditorPrefs.GetString("CaveBuild_OpenRouterModelId", openRouterModelId);
            ollamaModelId = EditorPrefs.GetString("CaveBuild_OllamaModelId", ollamaModelId);
            lmStudioModelId = EditorPrefs.GetString("CaveBuild_LmStudioModelId", lmStudioModelId);
            customProviderLabel = EditorPrefs.GetString("CaveBuild_CustomProviderLabel", customProviderLabel);
            allowExternalProviderEdits = EditorPrefs.GetBool(
                "CaveBuild_AllowExternalProviderEdits",
                allowExternalProviderEdits);
            externalProviderEditsDryRun = EditorPrefs.GetBool(
                "CaveBuild_ExternalProviderEditsDryRun",
                externalProviderEditsDryRun);
            autoInvokeOnDud = EditorPrefs.GetBool(PrefAutoInvoke, autoInvokeOnDud);
            autoInvokeAfterEveryBuild = EditorPrefs.GetBool(PrefAutoInvokeEveryBuild, autoInvokeAfterEveryBuild);
            autoRebuildAfterAgentSuccess =
                EditorPrefs.GetBool(PrefAutoRebuildAfterAgent, autoRebuildAfterAgentSuccess);
            autoInvokeEachMeatLoopPass =
                EditorPrefs.GetBool(PrefAutoInvokeEachMeatPass, autoInvokeEachMeatLoopPass);
            suppressMeatLoopCursorInvokes =
                EditorPrefs.GetBool("CaveBuild_SuppressMeatLoopCursor", suppressMeatLoopCursorInvokes);
            autoInvokePreBuildWorkflow =
                EditorPrefs.GetBool(PrefAutoInvokePreBuild, autoInvokePreBuildWorkflow);
            enforcePreBuildGate = EditorPrefs.GetBool(PrefEnforcePreBuildGate, enforcePreBuildGate);
            preBuildReloopUntilPass = EditorPrefs.GetBool(
                PrefPreBuildReloopUntilPass, preBuildReloopUntilPass);
            maxPreBuildReloopAttempts = EditorPrefs.GetInt(
                PrefMaxPreBuildReloopAttempts, maxPreBuildReloopAttempts);
            autoContinueAfterPreBuildCursor =
                EditorPrefs.GetBool("CaveBuild_AutoContinueAfterPreBuild", autoContinueAfterPreBuildCursor);
            usePhasedCaveBuild = EditorPrefs.GetBool("CaveBuild_UsePhasedCaveBuild", usePhasedCaveBuild);
            showLiveScenePlacement = EditorPrefs.GetBool(
                "CaveBuild_ShowLiveScenePlacement",
                showLiveScenePlacement);
            useIncrementalLadder = EditorPrefs.GetBool("CaveBuild_UseIncrementalLadder", useIncrementalLadder);
            constrainBotToResearchOnFailure = EditorPrefs.GetBool(
                "CaveBuild_ConstrainBotResearch", constrainBotToResearchOnFailure);
            autoRunPlaytestBotAfterBuild = EditorPrefs.GetBool(
                "CaveBuild_AutoRunPlaytestBotAfterBuild", autoRunPlaytestBotAfterBuild);
            enableBatchMode = EditorPrefs.GetBool("CaveBuild_EnableBatchMode", enableBatchMode);
            batchJobCount = EditorPrefs.GetInt("CaveBuild_BatchJobCount", batchJobCount);
            batchDelaySeconds = EditorPrefs.GetFloat("CaveBuild_BatchDelaySeconds", batchDelaySeconds);
            batchSeedIncrement = EditorPrefs.GetInt("CaveBuild_BatchSeedIncrement", batchSeedIncrement);
            runPostBuildResearchPhase = EditorPrefs.GetBool(
                "CaveBuild_RunPostBuildResearchPhase", runPostBuildResearchPhase);
            invokeCursorOnResearchPhase = EditorPrefs.GetBool(
                "CaveBuild_InvokeCursorOnResearchPhase", invokeCursorOnResearchPhase);
            autoInvokeTerrainAfterSurfaceBuild = EditorPrefs.GetBool(
                PrefAutoInvokeTerrain, autoInvokeTerrainAfterSurfaceBuild);
            autoRebuildSurfaceAfterTerrainAgent = EditorPrefs.GetBool(
                PrefAutoRebuildSurface, autoRebuildSurfaceAfterTerrainAgent);
            suggestTerrainGradeWatcher = EditorPrefs.GetBool(
                PrefWatchTerrainGrade, suggestTerrainGradeWatcher);
            stabilizationMode = EditorPrefs.GetBool(PrefStabilizationMode, stabilizationMode);
            queuedStepTimeoutSeconds = EditorPrefs.GetFloat(PrefQueuedStepTimeoutSeconds, queuedStepTimeoutSeconds);
            enableAutonomousUntilShip = EditorPrefs.GetBool(
                "CaveBuild_EnableAutonomousUntilShip", enableAutonomousUntilShip);
            maxAutonomousIterations = EditorPrefs.GetInt(
                "CaveBuild_MaxAutonomousIterations", maxAutonomousIterations);
            autonomousIterationCooldownSeconds = EditorPrefs.GetFloat(
                "CaveBuild_AutonomousCooldown", autonomousIterationCooldownSeconds);
            suppressMidBuildDialogs = EditorPrefs.GetBool(
                "CaveBuild_SuppressMidBuildDialogs", suppressMidBuildDialogs);
            showReloopDialog = EditorPrefs.GetBool("CaveBuild_ShowReloopDialog", showReloopDialog);
            exportGenerationPrefabWhenFinished = EditorPrefs.GetBool(
                "CaveBuild_ExportGenerationPrefab", exportGenerationPrefabWhenFinished);
            editorQueueLightBase = EditorPrefs.GetFloat(
                "CaveBuild_EditorQueueLightBase_v3", editorQueueLightBase);
            editorQueueNormalMultiplier = EditorPrefs.GetFloat(
                "CaveBuild_EditorQueueNormalMult_v3", editorQueueNormalMultiplier);
            editorQueueHeavyMultiplier = EditorPrefs.GetFloat(
                "CaveBuild_EditorQueueHeavyMult_v3", editorQueueHeavyMultiplier);
            editorQueueLoadStepPerItem = EditorPrefs.GetFloat(
                "CaveBuild_EditorQueueLoadStep_v3", editorQueueLoadStepPerItem);
            editorQueueMaxLoadMultiplier = EditorPrefs.GetFloat(
                "CaveBuild_EditorQueueMaxLoad_v3", editorQueueMaxLoadMultiplier);
            editorQueueBatchSize = EditorPrefs.GetInt("CaveBuild_EditorQueueBatch_v3", editorQueueBatchSize);
            hardwareBudget = (EnvironmentKitHardwareBudget.Preset)EditorPrefs.GetInt(
                "CaveBuild_HardwareBudget",
                (int)hardwareBudget);
            enableEnhancementPhases = EditorPrefs.GetBool(
                "CaveBuild_EnableEnhancementPhases", enableEnhancementPhases);
            demSupersampleTargetDim = EditorPrefs.GetInt(
                "CaveBuild_DemSupersampleDim", demSupersampleTargetDim);
            requirePreflightBeforeFullWorldBuild = EditorPrefs.GetBool(
                "CaveBuild_RequirePreflight", requirePreflightBeforeFullWorldBuild);

            if (stabilizationMode)
            {
                autoRebuildAfterAgentSuccess = false;
                autoInvokeEachMeatLoopPass = false;
                autoInvokeTerrainAfterSurfaceBuild = false;
                autoInvokeAfterEveryBuild = false;
            }
        }

        public void SaveToPrefs()
        {
            EditorPrefs.SetString(PrefHubRoot, hubProjectRoot ?? string.Empty);
            EditorPrefs.SetString(PrefModelId, modelId ?? "auto");
            EditorPrefs.SetInt(PrefAiProvider, (int)aiProvider);
            EditorPrefs.SetString("CaveBuild_OpenAiBaseUrl", openAiCompatibleBaseUrl ?? string.Empty);
            EditorPrefs.SetString("CaveBuild_GoogleModelId", googleModelId ?? string.Empty);
            EditorPrefs.SetString("CaveBuild_AnthropicModelId", anthropicModelId ?? string.Empty);
            EditorPrefs.SetString("CaveBuild_OpenAiModelId", openAiModelId ?? string.Empty);
            EditorPrefs.SetString("CaveBuild_OpenRouterModelId", openRouterModelId ?? string.Empty);
            EditorPrefs.SetString("CaveBuild_OllamaModelId", ollamaModelId ?? string.Empty);
            EditorPrefs.SetString("CaveBuild_LmStudioModelId", lmStudioModelId ?? string.Empty);
            EditorPrefs.SetString("CaveBuild_CustomProviderLabel", customProviderLabel ?? string.Empty);
            EditorPrefs.SetBool("CaveBuild_AllowExternalProviderEdits", allowExternalProviderEdits);
            EditorPrefs.SetBool("CaveBuild_ExternalProviderEditsDryRun", externalProviderEditsDryRun);
            EditorPrefs.SetBool(PrefAutoInvoke, autoInvokeOnDud);
            EditorPrefs.SetBool(PrefAutoInvokeEveryBuild, autoInvokeAfterEveryBuild);
            EditorPrefs.SetBool(PrefAutoRebuildAfterAgent, autoRebuildAfterAgentSuccess);
            EditorPrefs.SetBool(PrefAutoInvokeEachMeatPass, autoInvokeEachMeatLoopPass);
            EditorPrefs.SetBool("CaveBuild_SuppressMeatLoopCursor", suppressMeatLoopCursorInvokes);
            EditorPrefs.SetBool(PrefAutoInvokePreBuild, autoInvokePreBuildWorkflow);
            EditorPrefs.SetBool(PrefEnforcePreBuildGate, enforcePreBuildGate);
            EditorPrefs.SetBool(PrefPreBuildReloopUntilPass, preBuildReloopUntilPass);
            EditorPrefs.SetInt(PrefMaxPreBuildReloopAttempts, maxPreBuildReloopAttempts);
            EditorPrefs.SetBool(
                "CaveBuild_SkipResearchNetworkSyncWhenCachePresent",
                skipResearchNetworkSyncWhenCachePresent);
            EditorPrefs.SetBool("CaveBuild_AutoContinueAfterPreBuild", autoContinueAfterPreBuildCursor);
            EditorPrefs.SetBool("CaveBuild_UsePhasedCaveBuild", usePhasedCaveBuild);
            EditorPrefs.SetBool("CaveBuild_ShowLiveScenePlacement", showLiveScenePlacement);
            EditorPrefs.SetBool("CaveBuild_UseIncrementalLadder", useIncrementalLadder);
            EditorPrefs.SetBool("CaveBuild_ConstrainBotResearch", constrainBotToResearchOnFailure);
            EditorPrefs.SetBool("CaveBuild_AutoRunPlaytestBotAfterBuild", autoRunPlaytestBotAfterBuild);
            EditorPrefs.SetBool("CaveBuild_EnableBatchMode", enableBatchMode);
            EditorPrefs.SetInt("CaveBuild_BatchJobCount", batchJobCount);
            EditorPrefs.SetFloat("CaveBuild_BatchDelaySeconds", batchDelaySeconds);
            EditorPrefs.SetInt("CaveBuild_BatchSeedIncrement", batchSeedIncrement);
            EditorPrefs.SetBool("CaveBuild_RunPostBuildResearchPhase", runPostBuildResearchPhase);
            EditorPrefs.SetBool("CaveBuild_InvokeCursorOnResearchPhase", invokeCursorOnResearchPhase);
            EditorPrefs.SetBool(PrefAutoInvokeTerrain, autoInvokeTerrainAfterSurfaceBuild);
            EditorPrefs.SetBool(PrefAutoRebuildSurface, autoRebuildSurfaceAfterTerrainAgent);
            EditorPrefs.SetBool(PrefWatchTerrainGrade, suggestTerrainGradeWatcher);
            EditorPrefs.SetBool(PrefStabilizationMode, stabilizationMode);
            EditorPrefs.SetFloat(PrefQueuedStepTimeoutSeconds, queuedStepTimeoutSeconds);
            EditorPrefs.SetBool("CaveBuild_EnableAutonomousUntilShip", enableAutonomousUntilShip);
            EditorPrefs.SetInt("CaveBuild_MaxAutonomousIterations", maxAutonomousIterations);
            EditorPrefs.SetFloat("CaveBuild_AutonomousCooldown", autonomousIterationCooldownSeconds);
            EditorPrefs.SetBool("CaveBuild_SuppressMidBuildDialogs", suppressMidBuildDialogs);
            EditorPrefs.SetBool("CaveBuild_ShowReloopDialog", showReloopDialog);
            EditorPrefs.SetBool("CaveBuild_ExportGenerationPrefab", exportGenerationPrefabWhenFinished);
            EditorPrefs.SetFloat("CaveBuild_EditorQueueLightBase_v3", editorQueueLightBase);
            EditorPrefs.SetFloat("CaveBuild_EditorQueueNormalMult_v3", editorQueueNormalMultiplier);
            EditorPrefs.SetFloat("CaveBuild_EditorQueueHeavyMult_v3", editorQueueHeavyMultiplier);
            EditorPrefs.SetFloat("CaveBuild_EditorQueueLoadStep_v3", editorQueueLoadStepPerItem);
            EditorPrefs.SetFloat("CaveBuild_EditorQueueMaxLoad_v3", editorQueueMaxLoadMultiplier);
            EditorPrefs.SetInt("CaveBuild_EditorQueueBatch_v3", editorQueueBatchSize);
            EditorPrefs.SetInt("CaveBuild_HardwareBudget", (int)hardwareBudget);
            EditorPrefs.SetBool("CaveBuild_EnableEnhancementPhases", enableEnhancementPhases);
            EditorPrefs.SetInt("CaveBuild_DemSupersampleDim", demSupersampleTargetDim);
            EditorPrefs.SetBool("CaveBuild_RequirePreflight", requirePreflightBeforeFullWorldBuild);
        }

        public static string ResolveApiKey()
        {
            var env = System.Environment.GetEnvironmentVariable("CURSOR_API_KEY");
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();

            var prefs = LoadOrCreate().GetApiKey();
            if (!string.IsNullOrWhiteSpace(prefs))
                return prefs.Trim();

            return TryReadApiKeyFromDotEnv();
        }

        public static string DotEnvPath =>
            System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    ResolveHubRoot(),
                    "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/.env"));

        public static string TryReadApiKeyFromDotEnv()
        {
            var path = DotEnvPath;
            if (!System.IO.File.Exists(path))
                return string.Empty;

            foreach (var line in System.IO.File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#") || !trimmed.StartsWith("CURSOR_API_KEY="))
                    continue;

                var value = trimmed.Substring("CURSOR_API_KEY=".Length).Trim();
                if (value.Length >= 2 &&
                    ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                     (value.StartsWith("'") && value.EndsWith("'"))))
                    value = value.Substring(1, value.Length - 2);

                return value;
            }

            return string.Empty;
        }

        public static string TryReadEnvValueFromDotEnv(string key)
        {
            var path = DotEnvPath;
            if (!System.IO.File.Exists(path))
                return string.Empty;

            var prefix = key + "=";
            foreach (var line in System.IO.File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#") || !trimmed.StartsWith(prefix))
                    continue;

                var value = trimmed.Substring(prefix.Length).Trim();
                if (value.Length >= 2 &&
                    ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                     (value.StartsWith("'") && value.EndsWith("'"))))
                    value = value.Substring(1, value.Length - 2);

                return value;
            }

            return string.Empty;
        }

        public static string ResolveModelId()
        {
            var env = System.Environment.GetEnvironmentVariable("CAVE_CURSOR_MODEL");
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();

            var fromDotEnv = TryReadEnvValueFromDotEnv("CAVE_CURSOR_MODEL");
            if (!string.IsNullOrWhiteSpace(fromDotEnv))
                return fromDotEnv.Trim();

            var settings = LoadOrCreate();
            settings.LoadFromPrefs();
            return string.IsNullOrWhiteSpace(settings.modelId) ? "auto" : settings.modelId.Trim();
        }

        public static EnvironmentKitAiProvider ResolveActiveProvider()
        {
            var settings = LoadOrCreate();
            settings.LoadFromPrefs();
            return settings.aiProvider;
        }

        public static bool ProviderNeedsApiKey(EnvironmentKitAiProvider provider) =>
            provider is EnvironmentKitAiProvider.Cursor or
                EnvironmentKitAiProvider.GoogleGemini or
                EnvironmentKitAiProvider.AnthropicClaude or
                EnvironmentKitAiProvider.OpenAICompatible or
                EnvironmentKitAiProvider.OpenRouter or
                EnvironmentKitAiProvider.CustomEndpoint;

        public static string ResolveActiveApiKey()
        {
            var settings = LoadOrCreate();
            settings.LoadFromPrefs();
            var provider = settings.aiProvider;
            if (!ProviderNeedsApiKey(provider))
                return string.Empty;

            // Backward compatibility: Cursor provider still reads env first.
            if (provider == EnvironmentKitAiProvider.Cursor)
                return ResolveApiKey();

            var envVar = provider switch
            {
                EnvironmentKitAiProvider.GoogleGemini => "GOOGLE_API_KEY",
                EnvironmentKitAiProvider.AnthropicClaude => "ANTHROPIC_API_KEY",
                EnvironmentKitAiProvider.OpenAICompatible => "OPENAI_API_KEY",
                EnvironmentKitAiProvider.OpenRouter => "OPENROUTER_API_KEY",
                EnvironmentKitAiProvider.CustomEndpoint => "CUSTOM_API_KEY",
                _ => string.Empty,
            };

            var env = string.IsNullOrEmpty(envVar) ? string.Empty : System.Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();

            return settings.GetApiKey(provider).Trim();
        }

        public static bool HasCredentialsForActiveProvider()
        {
            var provider = ResolveActiveProvider();
            return !ProviderNeedsApiKey(provider) || !string.IsNullOrWhiteSpace(ResolveActiveApiKey());
        }

        public static string CursorWorkflowCredentialHint() => GraderCredentialHint();

        public static string GraderCredentialHint()
        {
            var provider = ResolveActiveProvider();
            return provider switch
            {
                EnvironmentKitAiProvider.Cursor =>
                    "Add API key under Hub → Settings for the active provider (or switch to Ollama / LM Studio for local grading).",
                EnvironmentKitAiProvider.GoogleGemini =>
                    "Set GOOGLE_API_KEY in Hub Settings or environment.",
                EnvironmentKitAiProvider.AnthropicClaude =>
                    "Set ANTHROPIC_API_KEY in Hub Settings or environment.",
                EnvironmentKitAiProvider.OpenAICompatible =>
                    "Set OPENAI_API_KEY (or compatible base URL + key) in Hub Settings.",
                EnvironmentKitAiProvider.OpenRouter =>
                    "Set OPENROUTER_API_KEY in Hub Settings or environment.",
                EnvironmentKitAiProvider.LocalOllama =>
                    "Run Ollama locally (ollama serve) with model " +
                    $"{LoadOrCreate().ollamaModelId} — no cloud API key required.",
                EnvironmentKitAiProvider.LocalLmStudio =>
                    "Run LM Studio local server on http://localhost:1234/v1 — no cloud API key required.",
                EnvironmentKitAiProvider.CustomEndpoint =>
                    "Set CUSTOM_API_KEY and base URL for your endpoint in Hub Settings.",
                _ => "Configure AI provider credentials in Hub Settings.",
            };
        }

        public static string ResolveActiveModelId()
        {
            var settings = LoadOrCreate();
            settings.LoadFromPrefs();
            return settings.aiProvider switch
            {
                EnvironmentKitAiProvider.Cursor => ResolveModelId(),
                EnvironmentKitAiProvider.GoogleGemini => settings.googleModelId,
                EnvironmentKitAiProvider.AnthropicClaude => settings.anthropicModelId,
                EnvironmentKitAiProvider.OpenAICompatible => settings.openAiModelId,
                EnvironmentKitAiProvider.OpenRouter => settings.openRouterModelId,
                EnvironmentKitAiProvider.LocalOllama => settings.ollamaModelId,
                EnvironmentKitAiProvider.LocalLmStudio => settings.lmStudioModelId,
                EnvironmentKitAiProvider.CustomEndpoint => settings.openAiModelId,
                _ => settings.modelId,
            };
        }

        public static string ResolveActiveBaseUrl()
        {
            var settings = LoadOrCreate();
            settings.LoadFromPrefs();
            return settings.aiProvider switch
            {
                EnvironmentKitAiProvider.LocalOllama => "http://localhost:11434/v1",
                EnvironmentKitAiProvider.LocalLmStudio => "http://localhost:1234/v1",
                EnvironmentKitAiProvider.OpenAICompatible => settings.openAiCompatibleBaseUrl,
                EnvironmentKitAiProvider.CustomEndpoint => settings.openAiCompatibleBaseUrl,
                _ => string.Empty,
            };
        }

        public static void SyncApiKeyFromDotEnvToEditorPrefs()
        {
            var key = TryReadApiKeyFromDotEnv();
            if (string.IsNullOrWhiteSpace(key))
            {
                UnityEngine.Debug.LogWarning(
                    $"[CaveCursor] No CURSOR_API_KEY in {DotEnvPath}. Copy .env.example to .env.");
                return;
            }

            var settings = LoadOrCreate();
            settings.SetApiKey(key);

            var model = TryReadEnvValueFromDotEnv("CAVE_CURSOR_MODEL");
            if (!string.IsNullOrWhiteSpace(model))
                settings.modelId = model.Trim();

            var hub = TryReadEnvValueFromDotEnv("HUB_ROOT");
            if (!string.IsNullOrWhiteSpace(hub))
                settings.hubProjectRoot = hub.Trim();

            settings.suppressMeatLoopCursorInvokes = false;
            settings.stabilizationMode = true;
            settings.autoInvokeEachMeatLoopPass = false;
            settings.autoInvokeTerrainAfterSurfaceBuild = false;
            settings.autoRebuildAfterAgentSuccess = false;
            settings.autoRebuildSurfaceAfterTerrainAgent = false;
            settings.SaveToPrefs();
            EditorUtility.SetDirty(settings);
            UnityEngine.Debug.Log(
                "[CaveCursor] Synced from .env: API key (stabilization mode ON, manual terrain/meat invokes)" +
                (string.IsNullOrWhiteSpace(model) ? "" : $", model={model}") +
                (string.IsNullOrWhiteSpace(hub) ? "" : $", hub={hub}") +
                " (Unity auto-invoke uses these).");
        }

        public static bool DefersPostBuildCursorToAutonomousLoop()
        {
            var s = LoadOrCreate();
            s.LoadFromPrefs();
            return s.enableAutonomousUntilShip;
        }

        public static string ResolveHubRoot()
        {
            var settings = LoadOrCreate();
            settings.LoadFromPrefs();
            if (!string.IsNullOrWhiteSpace(settings.hubProjectRoot))
                return settings.hubProjectRoot.TrimEnd('/');

            return System.IO.Path.GetDirectoryName(Application.dataPath);
        }
    }
}
