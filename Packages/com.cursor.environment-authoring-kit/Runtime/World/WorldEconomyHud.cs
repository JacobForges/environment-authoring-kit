using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Lightweight wallet + craft hint overlay (no uGUI dependency).</summary>
    [DisallowMultipleComponent]
    public sealed class WorldEconomyHud : MonoBehaviour
    {
        static WorldEconomyHud _instance;

        [SerializeField] bool showHud = true;

        public static void SetHudVisible(bool visible)
        {
            if (_instance != null)
                _instance.showHud = visible;
        }
        [SerializeField] KeyCode craftKey = KeyCode.G;

        PlayerPersistence _persistence;
        string _lastMessage = string.Empty;
        float _messageUntil;

        void Awake() => _instance = this;

        public static void NotifyCollected(string msg)
        {
            if (_instance != null)
                _instance.ShowMessage(msg);
        }

        void Update()
        {
            _persistence ??= GetComponent<PlayerPersistence>()
                             ?? FindAnyObjectByType<PlayerPersistence>();
            if (_persistence == null)
                return;

            if (Input.GetKeyDown(craftKey))
            {
                if (WorldGemCrafting.TryCraftAllFamilies(_persistence.Card, out var msg))
                {
                    ShowMessage(msg);
                    _persistence.Save();
                }
                else
                    ShowMessage(msg);
            }
        }

        void OnGUI()
        {
            if (!showHud || _persistence?.Card == null)
                return;

            var c = _persistence.Card.currency;
            WorldCurrencyService.Normalize(_persistence.Card);
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperLeft,
            };
            var total = WorldCurrencyService.TotalCopper(c);
            GUILayout.BeginArea(new Rect(12f, 12f, 280f, 120f), style);
            GUILayout.Label("Wallet");
            GUILayout.Label($"Pt {c.platinum}  Au {c.gold}  Ag {c.silver}  Cu {c.copper}");
            GUILayout.Label($"({total} copper total)");
            GUILayout.Label($"[{craftKey}] craft gems (tool→cabochon→cut)");
            if (Time.unscaledTime < _messageUntil && !string.IsNullOrEmpty(_lastMessage))
                GUILayout.Label(_lastMessage);
            GUILayout.EndArea();
        }

        void ShowMessage(string msg)
        {
            _lastMessage = msg;
            _messageUntil = Time.unscaledTime + 4f;
        }
    }
}
