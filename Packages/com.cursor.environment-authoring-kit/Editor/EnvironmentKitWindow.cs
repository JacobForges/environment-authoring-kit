using EnvironmentAuthoringKit.Editor.Atmosphere;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.Scatter;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor
{
    public sealed class EnvironmentKitWindow : EditorWindow
    {
        enum Tab
        {
            Scatter,
            Terrain,
            Atmosphere,
            Blockout
        }

        string _description;
        int _seed;
        bool _optimizeOnGenerate = true;
        Tab _tab = Tab.Scatter;

        BiomeCatalog _catalog;
        XROptimizationProfile _xrProfile;
        ScatterProfile _scatterProfile;
        TerrainDressingPreset _terrainPreset;
        AtmospherePreset _atmospherePreset;

        Vector2 _scroll;
        string _lastReport = string.Empty;
        Transform _groundReference;
        bool _placeUnderGround;
        string _caveLavaFolders;
        string _cavePropFolders;
        bool _caveScanAllAssets;

        [MenuItem("Window/Environment Kit")]
        public static void Open()
        {
            var window = GetWindow<EnvironmentKitWindow>("Environment Kit");
            window.minSize = new Vector2(420f, 520f);
            window.Show();
        }

        void OnEnable()
        {
            _description = EnvironmentKitSettings.LastDescription;
            _seed = EnvironmentKitSettings.GenerationSeed;
            _optimizeOnGenerate = EnvironmentKitSettings.OptimizeForVitureOnGenerate;
            _placeUnderGround = EnvironmentKitSettings.PlaceUnderGroundSurface;
            _groundReference = SceneGroundResolver.Resolve().Anchor;
            _caveLavaFolders = EnvironmentKitSettings.CaveLavaPrefabFolders;
            _cavePropFolders = EnvironmentKitSettings.CavePropPrefabFolders;
            _caveScanAllAssets = EnvironmentKitSettings.CaveScanAllAssets;
            TryLoadDefaultAssets();
        }

        void OnDisable()
        {
            ScatterBrush.Active = false;
            BlockoutTool.Active = false;
        }

        void TryLoadDefaultAssets()
        {
            _catalog = LoadAsset<BiomeCatalog>("BiomeCatalog");
            _xrProfile = LoadAsset<XROptimizationProfile>("VitureXRPro");
            _scatterProfile = LoadAsset<ScatterProfile>("ForestScatter");
            _terrainPreset = LoadAsset<TerrainDressingPreset>("ForestTerrain");
            _atmospherePreset = LoadAsset<AtmospherePreset>("ForestOvercast");
        }

        static T LoadAsset<T>(string name) where T : Object
        {
            var path = $"{EnvironmentKitSettings.PresetsFolder}/{name}.asset";
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawGenerateSection();
            EditorGUILayout.Space(8f);
            DrawTabs();
            EditorGUILayout.EndScrollView();
        }

        void DrawGenerateSection()
        {
            EditorGUILayout.LabelField("Generate World", EditorStyles.boldLabel);
            _description = EditorGUILayout.TextArea(_description, GUILayout.MinHeight(48f));
            _seed = EditorGUILayout.IntField("Seed", _seed);
            _optimizeOnGenerate = EditorGUILayout.Toggle("Also optimize for Viture XR Pro", _optimizeOnGenerate);
            _placeUnderGround = EditorGUILayout.Toggle("Place under ground / plane", _placeUnderGround);
            EnvironmentKitSettings.NeverCreateNewTerrain =
                EditorGUILayout.Toggle("Never create new terrain (use my ground only)", EnvironmentKitSettings.NeverCreateNewTerrain);
            _caveLavaFolders = EditorGUILayout.TextField("Cave Lava Prefab Folders (;)", _caveLavaFolders);
            _cavePropFolders = EditorGUILayout.TextField("Cave Prop Prefab Folders (;)", _cavePropFolders);
            _caveScanAllAssets = EditorGUILayout.Toggle("Scan all Assets for cave props (noisy)", _caveScanAllAssets);

            EditorGUILayout.BeginHorizontal();
            _groundReference = (Transform)EditorGUILayout.ObjectField("Ground / Plane", _groundReference, typeof(Transform), true);
            if (GUILayout.Button("Use Selection", GUILayout.Width(100f)))
            {
                if (Selection.activeTransform != null)
                {
                    _groundReference = Selection.activeTransform;
                    SceneGroundResolver.SaveAssignedGround(_groundReference);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_groundReference != null)
                SceneGroundResolver.SaveAssignedGround(_groundReference);

            EditorGUILayout.HelpBox(
                "Uses the ACTIVE scene only (open MainScene first). Assign Ground/Plane or click Use Selection. Does not create a new scene or terrain unless you disable the option above.\n\n" +
                "Cave / FullSystem: same automated pipeline as Build Complete Cave (pre-build gate → 40 stages → post-build Cursor).",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "Prefab sources are user-selectable. Use semicolon-separated folder paths. Keep scan-all-assets OFF for strict/clean generation.",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate World", GUILayout.Height(28f)))
                RunGenerate(clearFirst: false);
            if (GUILayout.Button("Regenerate", GUILayout.Height(28f)))
            {
                if (EditorUtility.DisplayDialog("Regenerate", "Clear generated content and rebuild?", "Regenerate", "Cancel"))
                    RunGenerate(clearFirst: true);
            }
            if (GUILayout.Button("Optimize for XR", GUILayout.Height(28f)))
                RunXrOptimize();
            EditorGUILayout.EndHorizontal();

            _catalog = (BiomeCatalog)EditorGUILayout.ObjectField("Biome Catalog", _catalog, typeof(BiomeCatalog), false);
            _xrProfile = (XROptimizationProfile)EditorGUILayout.ObjectField("XR Profile", _xrProfile, typeof(XROptimizationProfile), false);

            if (!string.IsNullOrEmpty(_lastReport))
                EditorGUILayout.HelpBox(_lastReport, MessageType.Info);

            EditorGUILayout.HelpBox(
                "Examples: misty pine forest at dusk | hilly cave system with tunnels and cave entrance | " +
                "small cave entrance on a hillside, sparse stalactites | large cavern network, dense rocks",
                MessageType.None);

            if (GUILayout.Button("Create Sample Presets"))
                SamplePresetsCreator.CreateAll();
        }

        void DrawTabs()
        {
            _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Scatter", "Terrain", "Atmosphere", "Blockout" });
            EditorGUILayout.Space(4f);

            switch (_tab)
            {
                case Tab.Scatter:
                    DrawScatterTab();
                    break;
                case Tab.Terrain:
                    DrawTerrainTab();
                    break;
                case Tab.Atmosphere:
                    DrawAtmosphereTab();
                    break;
                case Tab.Blockout:
                    DrawBlockoutTab();
                    break;
            }
        }

        void DrawScatterTab()
        {
            _scatterProfile = (ScatterProfile)EditorGUILayout.ObjectField("Scatter Profile", _scatterProfile, typeof(ScatterProfile), false);
            ScatterBrush.Profile = _scatterProfile;
            ScatterBrush.Active = EditorGUILayout.Toggle("Enable Scene Paint", ScatterBrush.Active);

            if (_scatterProfile != null)
            {
                EditorGUILayout.LabelField($"Density: {_scatterProfile.densityPerSquareMeter}/m² | Radius: {_scatterProfile.brushRadius}m");
                EditorGUILayout.HelpBox("Left-drag to paint. Shift+drag to erase.", MessageType.None);
            }
        }

        void DrawTerrainTab()
        {
            _terrainPreset = (TerrainDressingPreset)EditorGUILayout.ObjectField("Terrain Preset", _terrainPreset, typeof(TerrainDressingPreset), false);
            if (GUILayout.Button("Apply to Selected Terrain") && _terrainPreset != null)
            {
                var terrain = Selection.activeGameObject?.GetComponent<UnityEngine.Terrain>();
                if (terrain == null)
                    terrain = Object.FindAnyObjectByType<UnityEngine.Terrain>();
                if (terrain != null)
                    TerrainDressingApplier.Apply(terrain, _terrainPreset);
                else
                    EditorUtility.DisplayDialog("Terrain", "No Terrain found in selection or scene.", "OK");
            }
        }

        void DrawAtmosphereTab()
        {
            _atmospherePreset = (AtmospherePreset)EditorGUILayout.ObjectField("Atmosphere Preset", _atmospherePreset, typeof(AtmospherePreset), false);
            if (GUILayout.Button("Apply Atmosphere") && _atmospherePreset != null)
                AtmosphereApplier.Apply(_atmospherePreset);
        }

        void DrawBlockoutTab()
        {
            BlockoutTool.Active = EditorGUILayout.Toggle("Enable Blockout Placement", BlockoutTool.Active);
            EnvironmentKitSettings.GridSnapSize = EditorGUILayout.FloatField("Grid Snap", EnvironmentKitSettings.GridSnapSize);
            BlockoutSettings.SelectedPrimitive = (BlockoutPrimitiveKind)EditorGUILayout.EnumPopup("Primitive", BlockoutSettings.SelectedPrimitive);
            EditorGUILayout.HelpBox("Click in Scene View to place. Ctrl+Scroll changes grid size.", MessageType.None);
        }

        void RunGenerate(bool clearFirst)
        {
            EnvironmentKitSettings.LastDescription = _description;
            EnvironmentKitSettings.GenerationSeed = _seed;
            EnvironmentKitSettings.OptimizeForVitureOnGenerate = _optimizeOnGenerate;
            EnvironmentKitSettings.PlaceUnderGroundSurface = _placeUnderGround;
            EnvironmentKitSettings.CaveLavaPrefabFolders = _caveLavaFolders;
            EnvironmentKitSettings.CavePropPrefabFolders = _cavePropFolders;
            EnvironmentKitSettings.CaveScanAllAssets = _caveScanAllAssets;
            if (_groundReference != null)
                SceneGroundResolver.SaveAssignedGround(_groundReference);

            if (_catalog == null)
            {
                EditorUtility.DisplayDialog("Environment Kit", "Assign a Biome Catalog or run Create Sample Presets.", "OK");
                return;
            }

            if (clearFirst)
            {
                var ground = SceneGroundResolver.Resolve(_groundReference);
                var root = EnvironmentSceneUtility.GetOrCreateRoot(ground);
                EnvironmentSceneUtility.ClearGeneratedChildren(root.transform);
            }

            EditorUtility.DisplayProgressBar("Environment Kit", "Generating world...", 0.5f);
            try
            {
                var result = WorldGenerator.Generate(_description, _catalog, _seed, _optimizeOnGenerate, _xrProfile);
                _lastReport = result.Message;
                if (!result.Success)
                    EditorUtility.DisplayDialog("Generate World", result.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Repaint();
        }

        void RunXrOptimize()
        {
            if (_xrProfile == null)
            {
                EditorUtility.DisplayDialog("Environment Kit", "Assign an XR Optimization Profile (e.g. VitureXRPro).", "OK");
                return;
            }

            var root = EnvironmentSceneUtility.GetOrCreateRoot().transform;
            var report = XROptimizer.Apply(_xrProfile, root);
            _lastReport = report.Summary;
            Repaint();
        }
    }
}
