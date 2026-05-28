using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Wider third-person spectator for the playtest bot — same follow tech as the player, farther orbit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CavePlaytestBotSpectator : MonoBehaviour
    {
        public const string CameraObjectName = "CavePlaytestBot_SpectatorCamera";

        [SerializeField] Transform target;

        Camera _camera;
        ThirdPersonFollowCamera _follow;
        PlayerCameraRig _playerRig;

        public Camera SpectatorCamera => _camera;

        public static CavePlaytestBotSpectator Ensure(Transform bot, bool enable)
        {
            if (bot == null)
                return null;

            var specRoot = bot.Find(CameraObjectName);
            GameObject camGo;
            if (specRoot != null)
                camGo = specRoot.gameObject;
            else
                camGo = new GameObject(CameraObjectName);

            camGo.transform.SetParent(null);

            var cam = camGo.GetComponent<Camera>();
            if (cam == null)
                cam = camGo.AddComponent<Camera>();

            var spec = camGo.GetComponent<CavePlaytestBotSpectator>();
            if (spec == null)
                spec = camGo.AddComponent<CavePlaytestBotSpectator>();

            spec.target = bot;
            spec._camera = cam;
            cam.depth = 20f;
            cam.enabled = enable;

            var aim = bot.Find(PlayerCameraRig.PivotName);
            if (aim == null)
            {
                var aimGo = new GameObject("BotSpectatorAim");
                aimGo.transform.SetParent(bot, false);
                aimGo.transform.localPosition = new Vector3(0f, 1.35f, 0f);
                aim = aimGo.transform;
            }

            spec._follow = ThirdPersonFollowCamera.Ensure(
                cam,
                bot,
                aim,
                ThirdPersonFollowCamera.FollowMode.AutoTrackTarget,
                ThirdPersonFollowCamera.SpectatorPreset);

            if (spec._follow != null)
            {
                spec._follow.ScaleForCellSize(CavePlaytestBotScale.ResolveCellSizeMeters(bot));
                if (enable)
                    spec._follow.SnapToIdeal();
            }

            var listener = cam.GetComponent<AudioListener>();
            if (listener == null && enable)
                cam.gameObject.AddComponent<AudioListener>();

            var player = ResolvePlayerRoot();
            spec._playerRig = player != null ? PlayerCameraRig.Ensure(player) : null;
            spec._playerRig?.SetPlaytestSpectate(enable, enable ? cam : null);

            spec.enabled = enable;
            return spec;
        }

        static Transform ResolvePlayerRoot()
        {
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null)
                return tagged.transform;

            var controller = Object.FindAnyObjectByType<CharacterController>();
            if (controller != null && controller.GetComponent<CavePlaytestBotAvatar>() == null)
                return controller.transform;

            return null;
        }

        void OnDisable()
        {
            _playerRig?.SetPlaytestSpectate(false, _camera);
        }
    }
}
