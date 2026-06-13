using System;
using System.Threading;
using System.Threading.Tasks;
using Hub;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using UnityEngine;

/// <summary>Single UGS init + anonymous sign-in entry — serialized for concurrent callers.</summary>
public static class HubUnityServices
{
    const string PlayerAccountsResourceName = "UnityPlayerAccountSettings";

    static readonly SemaphoreSlim InitGate = new SemaphoreSlim(1, 1);
    static readonly SemaphoreSlim SignInGate = new SemaphoreSlim(1, 1);
    static Task _initTask;
    static Task _signInTask;

    public static bool IsInitialized =>
        UnityServices.State == ServicesInitializationState.Initialized;

    public static bool IsPlayerAccountsReady => TryGetPlayerAccountService(out _);

    public static async Task EnsureInitializedAsync()
    {
        if (IsInitialized)
        {
            await EnsurePlayerAccountsReadyAsync();
            return;
        }

        var existing = Volatile.Read(ref _initTask);
        if (existing != null)
        {
            await existing.ConfigureAwait(false);
            return;
        }

        await InitGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsInitialized)
            {
                await EnsurePlayerAccountsReadyAsync().ConfigureAwait(false);
                return;
            }

            existing = _initTask;
            if (existing != null)
            {
                await existing.ConfigureAwait(false);
                return;
            }

            _initTask = InitializeInternalAsync();
            await _initTask.ConfigureAwait(false);
        }
        catch
        {
            _initTask = null;
            throw;
        }
        finally
        {
            InitGate.Release();
        }
    }

    static async Task InitializeInternalAsync()
    {
        if (!PortfolioVivoxRuntime.HasProjectCredentials())
            PortfolioVivoxInitGuard.EnsureInstalled();

        try
        {
            await UnityServices.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsVivoxUnconfiguredInitFailure(ex))
        {
            PortfolioVivoxInitGuard.LogSkippedOnce();
            if (UnityServices.State != ServicesInitializationState.Initialized)
                throw;
        }

        _ = AuthenticationService.Instance;
        await EnsurePlayerAccountsReadyAsync().ConfigureAwait(false);
    }

    static bool IsVivoxUnconfiguredInitFailure(Exception ex)
    {
        if (PortfolioVivoxRuntime.HasProjectCredentials())
            return false;

        for (var current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message ?? string.Empty;
            if (message.Contains("'server' is null or empty", StringComparison.Ordinal)
                || message.Contains("Unable to initialize Vivox", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Single anonymous sign-in entry — avoids duplicate SignInAnonymouslyAsync races.</summary>
    public static async Task EnsureSignedInAnonymouslyAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        if (AuthenticationService.Instance.IsSignedIn)
            return;

        var existing = Volatile.Read(ref _signInTask);
        if (existing != null)
        {
            await existing.ConfigureAwait(false);
            return;
        }

        await SignInGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (AuthenticationService.Instance.IsSignedIn)
                return;

            existing = _signInTask;
            if (existing != null)
            {
                await existing.ConfigureAwait(false);
                return;
            }

            _signInTask = SignInAnonymouslyInternalAsync();
            await _signInTask.ConfigureAwait(false);
        }
        catch
        {
            _signInTask = null;
            throw;
        }
        finally
        {
            SignInGate.Release();
        }
    }

    /// <summary>Alias for <see cref="EnsureSignedInAnonymouslyAsync"/>.</summary>
    public static Task EnsureSignedInAsync() => EnsureSignedInAnonymouslyAsync();

    static async Task SignInAnonymouslyInternalAsync()
    {
        try
        {
            var auth = AuthenticationService.Instance;
            if (auth.IsSignedIn)
                return;

            await auth.SignInAnonymouslyAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsSignInAlreadyInProgress(ex))
        {
            await WaitUntilSignedInAsync().ConfigureAwait(false);
        }
        finally
        {
            _signInTask = null;
        }
    }

    static bool IsSignInAlreadyInProgress(Exception ex)
    {
        var msg = ex?.Message ?? string.Empty;
        return msg.IndexOf("already signing in", StringComparison.OrdinalIgnoreCase) >= 0
               || msg.IndexOf("invalid state for this operation", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static async Task WaitUntilSignedInAsync()
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            if (AuthenticationService.Instance.IsSignedIn)
                return;

            await Task.Yield();
        }
    }

    /// <summary>Waits briefly for Player Accounts to register after UGS init.</summary>
    public static async Task<bool> EnsurePlayerAccountsReadyAsync()
    {
        if (!IsInitialized)
            return false;

        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (TryGetPlayerAccountService(out _))
                return true;

            await Task.Yield();
        }

        return TryGetPlayerAccountService(out _);
    }

    public static bool TryGetPlayerAccountService(out IPlayerAccountService service)
    {
        service = null;
        if (!IsInitialized)
            return false;

        try
        {
            service = UnityServices.Instance?.GetPlayerAccountService();
            if (service != null)
                return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[HubUnityServices] GetPlayerAccountService " + ex.Message);
        }

        try
        {
            service = PlayerAccountService.Instance;
            return service != null;
        }
        catch (ServicesInitializationException)
        {
            return false;
        }
    }

    public static bool HasPlayerAccountsSettingsAsset =>
        Resources.Load(PlayerAccountsResourceName) != null;

    /// <summary>True when UGS is initialized and LobbyService.Instance is reachable.</summary>
    public static bool TryGetLobbyService(out ILobbyService service)
    {
        service = null;
        if (!IsInitialized)
            return false;

        try
        {
            service = LobbyService.Instance;
            return service != null;
        }
        catch (InvalidOperationException ex)
        {
            Debug.LogWarning("[HubUnityServices] LobbyService not initialized — " + ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[HubUnityServices] LobbyService unavailable — " + ex.Message);
            return false;
        }
    }

    public static string PlayerAccountsSetupMessage()
    {
        if (!IsInitialized)
            return "Still connecting to Unity services — wait a moment, then try again.";

        if (!HasPlayerAccountsSettingsAsset)
        {
            return "Unity Editor still needs the Player Accounts Client ID — run Game → Sync Unity Player Accounts Client ID, "
                   + "then Project Settings → Services → Authentication → Refresh.";
        }

        return "Unity Player Accounts Client ID is saved in this project — exit Play mode, press Play again, "
               + "then tap Unity Account Sign In. (Unity loads Player Accounts once at startup.)";
    }
}
