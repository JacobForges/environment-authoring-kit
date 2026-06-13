using System;
using System.Threading.Tasks;
using EnvironmentAuthoringKit.World;
using Hub.Competition;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Single graceful application exit — Vivox, NGO, profile/cloud flush, then quit or stop Play Mode.</summary>
public static class HubApplicationQuit
{
    static bool s_QuitInProgress;

    public static void GracefulQuit() => _ = GracefulQuitAsync();

    public static void GracefulQuitSkipLocalSave() => _ = GracefulQuitAsync(skipLocalSave: true);

    public static async Task GracefulQuitAsync(bool skipLocalSave = false)
    {
        if (s_QuitInProgress)
            return;

        s_QuitInProgress = true;
        Debug.Log("[Hub] Graceful quit starting…");

        GameplayPauseMenu.Hide();
        CompetitionChatPanel.ReleaseFocus();
        PlayerControllerLock.ExitUiPointerMode();

        if (!skipLocalSave)
            FlushPendingLocalSaves();

        try
        {
            await PortfolioMenuInterop.TryGracefulNetworkTeardownAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Hub] Network teardown during quit — " + ex.Message);
        }

        if (!skipLocalSave)
            await TryFlushCloudAsync().ConfigureAwait(true);

        ExitProcess();
    }

    static void FlushPendingLocalSaves()
    {
        try
        {
            var profile = CompetitionProfileStore.Current;
            if (profile != null)
                CompetitionProfileStore.Save(profile);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Hub] Profile save during quit — " + ex.Message);
        }

        try
        {
            var persistent = UnityEngine.Object.FindAnyObjectByType<PersistentPlayer>();
            persistent?.SaveData();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Hub] PersistentPlayer save during quit — " + ex.Message);
        }

        try
        {
            foreach (var persistence in UnityEngine.Object.FindObjectsByType<PlayerPersistence>())
                persistence.Save();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Hub] PlayerPersistence save during quit — " + ex.Message);
        }
    }

    static async Task TryFlushCloudAsync()
    {
        try
        {
            var flush = CompetitionCloudPersistence.TryFlushBeforeQuitAsync();
            await Task.WhenAny(flush, Task.Delay(2500)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Hub] Cloud flush during quit — " + ex.Message);
        }
    }

    static void ExitProcess()
    {
#if UNITY_EDITOR
        EditorApplication.ExitPlaymode();
#else
        Application.Quit();
#endif
    }
}
