using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hub.Competition;
using EnvironmentAuthoringKit.World;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Hub.Multiplayer
{
    /// <summary>Title screen: Solo, Host (you), Play Online (Quick Join). No join codes.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class PortfolioMainMenuController : MonoBehaviour
    {
        [SerializeField] Sprite _backgroundSprite;
        [SerializeField] PortfolioUiTheme _theme;
        [SerializeField] PortfolioSessionOrchestrator _orchestrator;
        [SerializeField] PortfolioPlayerGate _playerGate;
        [SerializeField] PortfolioNetworkSpawnCoordinator _spawnCoordinator;
        [SerializeField] PortfolioSessionHud _sessionHud;
        [SerializeField] PortfolioMenuSceneCamera _menuCamera;

        GameObject _menuRoot;
        TMP_Text _status;
        Image _connectionDot;
        readonly List<PortfolioMenuButton> _buttons = new();
        PortfolioMenuButton _deleteAgentButton;
        PortfolioMenuButton _switchAgentButton;
        PortfolioMenuButton _editCharacterButton;
        PortfolioMenuLobbyChatPanel _menuLobbyChat;
        bool _busy;
        string _contentStatusLine = "Checking for updates…";
        HubContentUpdateResult _contentUpdateResult;

        void Awake()
        {
            CacheComponentRefs();
            if (Application.isPlaying)
                EnterTitleMenuMode();
        }

        void Start()
        {
            if (!Application.isPlaying || _menuRoot != null)
                return;

            _playerGate?.LockScenePlayer();
            EnterTitleMenuMode();
            _sessionHud?.Hide();

            DeepTrainAcademyIntro.Show(transform, () =>
                HubPlayerLoginShell.Show(transform, OnLoginShellComplete));
        }

        void OnLoginShellComplete()
        {
            if (_menuRoot == null)
            {
                if (_theme == null)
                    _theme = Resources.Load<PortfolioUiTheme>("PortfolioUiTheme");
                PortfolioUiKit.SetTheme(_theme);
                BuildMenu();
                _menuRoot.SetActive(false);
            }

            void OpenMainMenu()
            {
                if (_menuRoot != null)
                    _menuRoot.SetActive(true);
                OnMainMenuShown();
            }

            _ = RunContentUpdateCheckAsync();

            void AfterWelcome()
            {
                var profile = CompetitionProfileStore.Current;
                if (profile != null && profile.playerCharacterComplete)
                {
                    OpenMainMenu();
                    return;
                }

                PortfolioPlayerCharacterCreator.Show(transform, OpenMainMenu);
            }

            PortfolioWelcomeScreen.Show(transform, AfterWelcome);
        }

        void EnterTitleMenuMode()
        {
            WorldUiBootstrapGate.SuppressDuringTitleMenu = true;
            WorldEconomyHud.SetHudVisible(false);
            CompetitionMenuInterop.HideGameplayChat();
            _menuCamera?.ShowForTitleMenu();
        }

        void ExitTitleMenuMode()
        {
            WorldUiBootstrapGate.SuppressDuringTitleMenu = false;
            WorldPlayerSetupRuntime.WireScenePlayer();
            WorldEconomyHud.SetHudVisible(true);
            _menuCamera?.HideForGameplay();
        }

        void CacheComponentRefs()
        {
            if (_orchestrator == null)
                _orchestrator = GetComponent<PortfolioSessionOrchestrator>();
            if (_playerGate == null)
                _playerGate = GetComponent<PortfolioPlayerGate>();
            if (_spawnCoordinator == null)
                _spawnCoordinator = GetComponent<PortfolioNetworkSpawnCoordinator>();
            if (_sessionHud == null)
                _sessionHud = GetComponent<PortfolioSessionHud>();
            if (_menuCamera == null)
                _menuCamera = GetComponent<PortfolioMenuSceneCamera>();
        }

        static bool IsNetworkWired()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.NetworkConfig != null && nm.NetworkConfig.PlayerPrefab != null;
        }

        void BuildMenu()
        {
            var canvas = PortfolioUiKit.EnsureMenuCanvas(transform);
            _menuRoot = canvas.gameObject;

            PortfolioUiKit.BuildMenuBackdrop(
                canvas.transform,
                _backgroundSprite != null ? _backgroundSprite : _theme?.panelBackdrop);

            var panel = PortfolioUiKit.BuildMainPanel(canvas.transform);

            PortfolioUiKit.Badge(panel.transform, HubGameBranding.MenuBadge);
            var badge = panel.transform.Find("Badge");
            if (badge != null)
            {
                PortfolioUiKit.Place(
                    badge.GetComponent<RectTransform>(),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(28f, -22f),
                    new Vector2(340f, 32f));
            }

            var title = PortfolioUiKit.Headline(panel.transform, HubGameBranding.DisplayTitle, 44, FontStyles.Bold);
            PortfolioUiKit.Place(
                title.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0.52f, 1f),
                new Vector2(0f, 1f),
                new Vector2(28f, -72f),
                new Vector2(0f, 52f));

            var subtitle = PortfolioUiKit.Body(panel.transform, HubGameBranding.DisplaySubtitle, 22);
            subtitle.color = PortfolioUiKit.AccentGold;
            PortfolioUiKit.Place(
                subtitle.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0.52f, 1f),
                new Vector2(0f, 1f),
                new Vector2(28f, -128f),
                new Vector2(0f, 30f));

            PlaceBullet(panel.transform, 0, "Solo — explore the generated cave locally.");
            PlaceBullet(
                panel.transform,
                1,
                $"Play Online — cloud server, up to {PortfolioMultiplayerConfig.MaxPlayersSession} players.");
            PlaceBullet(
                panel.transform,
                2,
                $"Host Session — your Mac hosts, up to {PortfolioMultiplayerConfig.MaxPlayersSession} players.");
            PlaceBullet(panel.transform, 3, "Verified lobby chat — humans with confirmed email.");

            _menuLobbyChat = PortfolioMenuLobbyChatPanel.Attach(panel.transform);
            _menuLobbyChat.BindMenuLifecycle(transform);

            var buttonsRoot = new GameObject("Buttons", typeof(RectTransform), typeof(VerticalLayoutGroup));
            buttonsRoot.transform.SetParent(panel.transform, false);
            var buttonsRt = buttonsRoot.GetComponent<RectTransform>();
            PortfolioUiKit.Place(
                buttonsRt,
                new Vector2(0.54f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-12f, 0f),
                new Vector2(-40f, -48f));

            var vlg = buttonsRoot.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 14f;
            vlg.padding = new RectOffset(0, 0, 24, 24);
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            _buttons.Add(AddModeButton(buttonsRoot.transform, "Play Solo", "Local play · no cloud account", true, OnSoloClicked));
            _buttons.Add(AddModeButton(
                buttonsRoot.transform,
                "Play Online",
                $"Join cloud server · up to {PortfolioMultiplayerConfig.MaxPlayersSession} · free Relay",
                false,
                OnJoinClicked));
            _buttons.Add(AddModeButton(
                buttonsRoot.transform,
                "Host Session",
                $"Listen server on this Mac · up to {PortfolioMultiplayerConfig.MaxPlayersSession} · laptop must stay on",
                false,
                OnHostClicked));
            _switchAgentButton = AddModeButton(
                buttonsRoot.transform,
                "Switch Agent",
                "Pick active agent from saved roster",
                false,
                OnSwitchAgentClicked);
            _deleteAgentButton = AddModeButton(
                buttonsRoot.transform,
                "Delete Saved Agent",
                "Wipe agent data · re-run onboarding on next Solo",
                false,
                OnDeleteSavedAgentClicked);
            _editCharacterButton = AddModeButton(
                buttonsRoot.transform,
                "Edit Explorer",
                "Change name, age, hair, body, and trail kit",
                false,
                OnEditCharacterClicked);
            RefreshAgentButtons();
            RefreshCharacterButton();

            var footer = PortfolioUiKit.Status(panel.transform);
            _status = footer;
            PortfolioUiKit.Place(
                footer.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 22f),
                new Vector2(-56f, 48f));

            _connectionDot = PortfolioUiKit.ConnectionDot(panel.transform, IsNetworkWired());
            PortfolioUiKit.Place(
                _connectionDot.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0.5f, 0.5f),
                new Vector2(28f, 34f),
                new Vector2(12f, 12f));

            RefreshFooterStatus();
        }

        void PlaceBullet(Transform panel, int index, string line)
        {
            var y = -178f - index * 34f;
            var tmp = PortfolioUiKit.Body(panel, "•  " + line, 18);
            tmp.color = PortfolioUiKit.TextMuted;
            PortfolioUiKit.Place(
                tmp.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0.52f, 1f),
                new Vector2(0f, 1f),
                new Vector2(28f, y),
                new Vector2(0f, 28f));
        }

        PortfolioMenuButton AddModeButton(Transform parent, string label, string hint, bool primary, Action onClick)
        {
            var result = PortfolioUiKit.MenuButton(parent, label, hint, primary);
            result.Button.onClick.AddListener(() => onClick());
            var le = result.Button.gameObject.AddComponent<LayoutElement>();
            le.minHeight = primary ? 72f : 64f;
            le.preferredHeight = primary ? 72f : 64f;
            return result;
        }

        void RefreshFooterStatus()
        {
            if (_status == null)
                return;

            var nmReady = IsNetworkWired();
            if (_connectionDot != null)
                _connectionDot.color = nmReady ? PortfolioUiKit.AccentEmerald : new Color(0.9f, 0.55f, 0.15f, 1f);

            var account = HubPlayerEmailAuth.IdentityLine();
            var chatNote = PortfolioMenuLobbyChat.CanUse ? "lobby chat on" : "lobby chat locked";
            var player = CompetitionProfileStore.Current;
            var playerNote = player != null && player.playerCharacterComplete && !string.IsNullOrWhiteSpace(player.playerName)
                ? player.playerName.Trim()
                : null;
            var who = playerNote != null ? $"{playerNote} · {account}" : account;
            var contentNote = string.IsNullOrWhiteSpace(_contentStatusLine) ? string.Empty : _contentStatusLine + " · ";
            _status.text = nmReady
                ? $"{contentNote}{who} · {chatNote} · up to {PortfolioMultiplayerConfig.MaxPlayersSession} players"
                : $"{contentNote}Network not ready · {who}";
        }

        async Task RunContentUpdateCheckAsync()
        {
            _contentStatusLine = "Checking for updates…";
            RefreshFooterStatus();

            try
            {
                _contentUpdateResult = await HubContentUpdater.CheckOnBootAsync();
                _contentStatusLine = HubContentUpdater.StatusLine(_contentUpdateResult);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HubContent] Update check — " + ex.Message);
                _contentStatusLine = "Update check failed — using local content";
            }

            RefreshFooterStatus();
        }

        void SetButtonsInteractable(bool on)
        {
            foreach (var b in _buttons)
                b.SetInteractable(on);
        }

        void OnSoloClicked()
        {
            if (_busy)
                return;

            if (!CompetitionMenuInterop.TryEnterSoloGameplay())
            {
                SetStatus("Solo failed — competition bridge not found. Check Console.");
                return;
            }
        }

        /// <summary>After competition onboarding — wormhole already handled; unlock only.</summary>
        public void EnterSoloGameplay()
        {
            DismissMenuForGameplay();
            _playerGate?.UnlockSoloPlay();
            _sessionHud?.ShowSolo();
            CompetitionMenuInterop.ShowGameplayChat();
            CompetitionMenuInterop.SpawnAgentNearPlayer();
        }

        void OnSwitchAgentClicked()
        {
            if (_busy)
                return;

            if (!CompetitionMenuInterop.HasSavedAgent)
            {
                SetStatus("No saved agents — Play Solo to create one.");
                RefreshAgentButtons();
                return;
            }

            CompetitionMenuInterop.ShowAgentRoster();
            SetStatus("Agent roster open — pick who to play.");
        }

        void OnDeleteSavedAgentClicked()
        {
            if (_busy)
                return;

            if (!CompetitionMenuInterop.HasSavedAgent)
            {
                SetStatus("No saved agent on disk.");
                RefreshAgentButtons();
                return;
            }

            if (!CompetitionMenuInterop.TryDeleteSavedAgent())
            {
                SetStatus("Delete failed — competition bridge not found. Check Console.");
                return;
            }

            RefreshAgentButtons();
            SetStatus("Saved agent deleted — Play Solo to run onboarding.");
        }

        void OnEditCharacterClicked()
        {
            if (_busy)
                return;

            if (_menuRoot != null)
                _menuRoot.SetActive(false);

            PortfolioPlayerCharacterCreator.ShowEdit(transform, () =>
            {
                if (_menuRoot != null)
                    _menuRoot.SetActive(true);
                OnMainMenuShown();
            });
        }

        void RefreshCharacterButton()
        {
            var complete = CompetitionProfileStore.Current?.playerCharacterComplete == true;
            if (_editCharacterButton.Button == null)
                return;

            _editCharacterButton.Button.gameObject.SetActive(complete);
            _editCharacterButton.SetInteractable(complete);
            _editCharacterButton.SetHint(complete
                ? "Re-open explorer profile and cosmetics"
                : "Finish character creation first");
        }

        void RefreshAgentButtons()
        {
            var hasAgent = CompetitionMenuInterop.HasSavedAgent;

            if (_switchAgentButton.Button != null)
            {
                _switchAgentButton.SetInteractable(hasAgent);
                _switchAgentButton.SetHint(hasAgent
                    ? "Choose active agent before Solo / Host / Online"
                    : "No saved agents yet");
            }

            if (_deleteAgentButton.Button == null)
                return;

            _deleteAgentButton.SetInteractable(hasAgent);
            _deleteAgentButton.SetHint(hasAgent
                ? "Wipe agent data · re-run onboarding on next Solo"
                : "No saved agent on disk");
        }

        async void OnHostClicked()
        {
            if (_busy)
                return;
            if (!EnsureNetworkReady())
                return;
            if (!EnsureAgentReady())
                return;
            if (!EnsureOrchestratorReady())
                return;
            if (!EnsureClientPlayMode())
                return;

            _busy = true;
            SetButtonsInteractable(false);
            PortfolioSessionLoadingOverlay.Show(transform, "Hosting — Relay + voice…");
            HideMenuForSessionLoading();
            SetStatus("Hosting — Relay + voice…");
            var sessionEntered = false;
            try
            {
                var center = GameObject.Find("PlayerSpawnPoint")?.transform.position ?? Vector3.zero;
                _spawnCoordinator?.EnsureRuntimeSpawnRing(center);
                await _orchestrator.HostPortfolioSessionAsync();
                _playerGate?.HideScenePlayerForNetwork();
                CompetitionMenuInterop.SpawnAgentNearPlayer();
                sessionEntered = true;
                StartCoroutine(FinishSessionEntryLoading(requireAgentSpawn: true, hosting: true));
            }
            catch (Exception ex)
            {
                PortfolioSessionLoadingOverlay.Hide();
                RestoreMenuAfterFailedSession();
                SetStatus("Host failed — " + FormatSessionError(ex));
                _playerGate?.LockScenePlayer();
                SetButtonsInteractable(true);
            }
            finally
            {
                _busy = false;
                if (!sessionEntered && PortfolioSessionLoadingOverlay.IsVisible)
                    PortfolioSessionLoadingOverlay.Hide();
            }
        }

        async void OnJoinClicked()
        {
            if (_busy)
                return;
            if (!EnsureNetworkReady())
                return;
            if (!EnsureAgentReady())
                return;
            if (!EnsureOrchestratorReady())
                return;
            if (!EnsureClientPlayMode())
                return;

            _busy = true;
            SetButtonsInteractable(false);
            PortfolioSessionLoadingOverlay.Show(transform, "Joining cloud session…");
            HideMenuForSessionLoading();
            SetStatus("Joining cloud session (free Relay tier)…");
            var sessionEntered = false;
            try
            {
                var joined = await _orchestrator.QuickJoinOrShowOfflineAsync();
                if (!joined)
                {
                    PortfolioSessionLoadingOverlay.Hide();
                    RestoreMenuAfterFailedSession();
                    SetStatus(
                        "No live session — start dedicated server (Game → Play Mode → Start Dedicated Cloud Server), "
                        + "or press Host Session on this Mac.");
                    _playerGate?.LockScenePlayer();
                    SetButtonsInteractable(true);
                    return;
                }

                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
                {
                    _playerGate?.HideScenePlayerForNetwork();
                    var count = NetworkManager.Singleton.ConnectedClientsIds.Count;
                    var dedicatedCloud = _orchestrator.JoinedDedicatedCloud;
                    CompetitionMenuInterop.SpawnAgentNearPlayer();
                    sessionEntered = true;
                    StartCoroutine(FinishSessionEntryLoading(
                        requireAgentSpawn: true,
                        hosting: false,
                        clientCount: count,
                        dedicatedCloud: dedicatedCloud));
                }
                else
                {
                    PortfolioSessionLoadingOverlay.Hide();
                    RestoreMenuAfterFailedSession();
                    SetStatus("Could not connect — host may still be starting. Try again in a moment.");
                    _playerGate?.LockScenePlayer();
                    SetButtonsInteractable(true);
                }
            }
            catch (Exception ex)
            {
                PortfolioSessionLoadingOverlay.Hide();
                RestoreMenuAfterFailedSession();
                SetStatus("Join failed — " + FormatSessionError(ex));
                _playerGate?.LockScenePlayer();
                SetButtonsInteractable(true);
            }
            finally
            {
                _busy = false;
                if (!sessionEntered && PortfolioSessionLoadingOverlay.IsVisible)
                    PortfolioSessionLoadingOverlay.Hide();
            }
        }

        bool EnsureOrchestratorReady()
        {
            if (_orchestrator == null)
                CacheComponentRefs();

            if (_orchestrator != null)
                return true;

            SetStatus("Multiplayer session manager missing — run Game → Setup Portfolio Menu.");
            RefreshFooterStatus();
            return false;
        }

        void HideMenuForSessionLoading()
        {
            OnMainMenuHidden();
            if (_menuRoot != null)
                _menuRoot.SetActive(false);
        }

        void RestoreMenuAfterFailedSession()
        {
            EnterTitleMenuMode();
            if (_menuRoot != null)
                _menuRoot.SetActive(true);
            OnMainMenuShown();
        }

        IEnumerator FinishSessionEntryLoading(
            bool requireAgentSpawn,
            bool hosting,
            int clientCount = 1,
            bool dedicatedCloud = false)
        {
            yield return PortfolioSessionLoadingOverlay.WaitUntilGameplayReady(requireAgentSpawn);
            PortfolioSessionLoadingOverlay.Hide();
            CompleteNetworkSessionEntry(hosting, clientCount, dedicatedCloud);
        }

        void CompleteNetworkSessionEntry(bool hosting, int clientCount, bool dedicatedCloud)
        {
            ExitTitleMenuMode();
            if (hosting)
                _sessionHud?.ShowHosting(Mathf.Max(1, clientCount));
            else if (dedicatedCloud)
                _sessionHud?.ShowCloudOnline(Mathf.Max(1, clientCount));
            else
                _sessionHud?.ShowOnline(Mathf.Max(1, clientCount));
            CompetitionMenuInterop.ShowGameplayChat();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        static string FormatSessionError(Exception ex)
        {
            if (ex == null)
                return "try again.";

            if (ex is NullReferenceException)
                return "connection setup failed — run Game → Setup Portfolio Menu, then try again.";

            var msg = ex.Message?.Trim();
            if (!string.IsNullOrEmpty(msg))
            {
                if (msg.IndexOf("object reference not set", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("nullreference", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "connection setup failed — run Game → Setup Portfolio Menu, then try again.";
                }

                return msg;
            }

            var inner = ex.InnerException?.Message?.Trim();
            if (!string.IsNullOrEmpty(inner))
                return inner;

            return "try again.";
        }

        bool EnsureAgentReady()
        {
            if (CompetitionMenuInterop.HasSavedAgent)
                return true;

            SetStatus("Create an agent first — Play Solo and finish onboarding.");
            RefreshFooterStatus();
            return false;
        }

        /// <summary>Dedicated-server Editor/headless builds cannot also join as a client in the same process.</summary>
        bool EnsureClientPlayMode()
        {
            if (!PortfolioDedicatedCloudHost.IsDedicatedProcess)
                return true;

            SetStatus(
                "This instance is the dedicated server — open a second Hub client "
                + "(Editor clone or .app build), then press Play Online.");
            RefreshFooterStatus();
            return false;
        }

        bool EnsureNetworkReady()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                SetStatus("Multiplayer is not set up in this build.");
                RefreshFooterStatus();
                return false;
            }

            if (nm.NetworkConfig == null || nm.NetworkConfig.PlayerPrefab == null)
            {
                SetStatus("Assign PortfolioNetworkPlayer on NetworkManager.");
                RefreshFooterStatus();
                return false;
            }

            return true;
        }

        void OnMainMenuShown()
        {
            RefreshFooterStatus();
            RefreshCharacterButton();
            _menuLobbyChat?.OnMenuShown();
        }

        void OnMainMenuHidden() => _menuLobbyChat?.OnMenuHidden();

        void DismissMenu()
        {
            OnMainMenuHidden();
            if (_menuRoot != null)
                _menuRoot.SetActive(false);
            ExitTitleMenuMode();
            CompetitionMenuInterop.ShowGameplayChat();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void DismissMenuForGameplay()
        {
            OnMainMenuHidden();
            if (_menuRoot != null)
                _menuRoot.SetActive(false);
            ExitTitleMenuMode();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void ReturnToTitleMenuFromGameplay()
        {
            EnterTitleMenuMode();
            _playerGate?.LockScenePlayer();
            _sessionHud?.Hide();
            _busy = false;
            SetButtonsInteractable(true);

            if (_menuRoot == null)
            {
                if (_theme == null)
                    _theme = Resources.Load<PortfolioUiTheme>("PortfolioUiTheme");
                PortfolioUiKit.SetTheme(_theme);
                BuildMenu();
            }

            if (_menuRoot != null)
                _menuRoot.SetActive(false);

            HubPlayerLoginShell.Show(transform, ShowTitleMenuAfterLogout);
        }

        void ShowTitleMenuAfterLogout()
        {
            if (_menuRoot != null)
                _menuRoot.SetActive(true);
            OnMainMenuShown();
            RefreshFooterStatus();
            RefreshAgentButtons();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void SetStatus(string message)
        {
            if (_status != null)
                _status.text = message;
        }
    }
}
