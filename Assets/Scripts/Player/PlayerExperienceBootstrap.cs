using System.Collections;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.World;
using Hub.Competition;
using Unity.Netcode;
using UnityEngine;

/// <summary>Play-mode safety net — avatar, SFX, settings, MMO HUD, and ground snap on the scene player.</summary>
public static class PlayerExperienceBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterSceneLoad()
    {
        if (!Application.isPlaying)
            return;

        var player = ResolvePlayer();
        if (player == null)
            return;

        Wire(player, ground: !WorldUiBootstrapGate.SuppressDuringTitleMenu);
    }

    static GameObject ResolvePlayer()
    {
        var root = WorldPlayerSetupRuntime.ResolvePlayerRoot();
        if (root != null)
            return root;

        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsConnectedClient
            && nm.ConnectedClients.TryGetValue(nm.LocalClientId, out var client)
            && client.PlayerObject != null)
            return client.PlayerObject.gameObject;

        GameObject tagged;
        try
        {
            tagged = GameObject.FindGameObjectWithTag("Player");
        }
        catch (UnityException)
        {
            tagged = null;
        }

        if (tagged != null && WorldPlayerSetupRuntime.IsPlayableCharacter(tagged))
            return tagged;

        foreach (var controller in Object.FindObjectsByType<PlayerController>())
        {
            if (controller != null && WorldPlayerSetupRuntime.IsPlayableCharacter(controller.gameObject))
                return controller.gameObject;
        }

        var demo = GameObject.Find("DemoPlayer");
        if (demo != null && WorldPlayerSetupRuntime.IsPlayableCharacter(demo))
            return demo;

        return null;
    }

    static void Wire(GameObject player, bool ground)
    {
        GameplaySpawnGrounding.EnsurePlayableCollision(player.transform);
        EnsureComponent<PlayerAvatarPresenter>(player);
        EnsureComponent<Hub.PlayerCosmetics.PlayerCosmeticDriver>(player);
        if (player.GetComponent<AudioSource>() == null)
            player.AddComponent<AudioSource>();
        EnsureComponent<PlayerProceduralSfx>(player);
        EnsureComponent<PlayerGameSettingsPanel>(player);

        if (player.GetComponent<MmoHudController>() == null)
            player.AddComponent<MmoHudController>();

        if (!ground)
            return;

        EnsureComponent<PlayerGroundingLatch>(player);
        GameplaySpawnGrounding.EnsureSpawnHold(player.transform);
        PlayerGroundSnap.SnapTransform(player.transform, player.transform.position);
    }

    sealed class PlayerGroundingLatch : MonoBehaviour
    {
        IEnumerator Start()
        {
            if (WorldUiBootstrapGate.SuppressDuringTitleMenu)
                yield break;

            yield return null;
            yield return null;
            yield return GameplaySpawnGrounding.PlaceAboveGroundWhenReady(transform);
        }
    }

    static T EnsureComponent<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
