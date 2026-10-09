using System.Linq;
using Content.Server.Chat.Managers;
using System.Text.Json;
using System.IO;
using Content.Server.GameTicking;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Utility;
using Content.Shared.CCVar;
using Robust.Server;
using Robust.Server.Player;
using Robust.Server.ServerStatus;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.ServerUpdates;

/// <summary>
/// Responsible for restarting the server for update, when not disruptive.
/// </summary>
public sealed class ServerUpdateManager
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IWatchdogApi _watchdog = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IBaseServer _server = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IResourceManager _resources = default!;

    private RoundEndDeploymentLease? _deployment;
    private TimeSpan _deploymentEarliestShutdown;
    private bool _deploymentShutdown;
    private static readonly ResPath DeploymentStatePath = new("/reclamation-update.json");

    [ViewVariables]
    private bool _updateOnRoundEnd;

    private TimeSpan? _restartTime;

    /// <summary>
    /// True when the watchdog has signalled a new build is staged and ready to deploy.
    /// Set by <see cref="WatchdogOnUpdateReceived"/>.
    /// </summary>
    public bool UpdatePending => _updateOnRoundEnd || _deployment != null;

    public void Initialize()
    {
        _watchdog.UpdateReceived += WatchdogOnUpdateReceived;
        _playerManager.PlayerStatusChanged += PlayerManagerOnPlayerStatusChanged;
    }

    public void Update()
    {
        if (_deployment != null)
        {
            if (!_deployment.IsActive(_gameTiming.RealTime))
                CancelDeployment(_deployment.Ticket);
            else if (!_deploymentShutdown && _gameTiming.RealTime >= _deploymentEarliestShutdown &&
                     _entities.System<GameTicker>().RunLevel != GameRunLevel.InRound)
                FinishDeployment();
        }
        if (_restartTime != null && _restartTime < _gameTiming.RealTime)
        {
            DoShutdown();
        }
    }

    /// <summary>
    /// Notify that the round just ended, which is a great time to restart if necessary!
    /// </summary>
    /// <returns>True if the server is going to restart.</returns>
    public bool RoundEnded()
    {
        if (_deploymentShutdown)
            return true;
        if (_deployment != null)
        {
            if (!_deployment.IsActive(_gameTiming.RealTime))
                CancelDeployment(_deployment.Ticket);
            else if (_entities.System<GameTicker>().RunLevel != GameRunLevel.InRound && FinishDeployment())
                return true;
        }
        if (_updateOnRoundEnd)
        {
            DoShutdown();
            return true;
        }

        return false;
    }

    public void QueueDeployment(string ticket)
    {
        if (_deploymentShutdown)
            throw new InvalidOperationException("Deployment shutdown is already in progress.");
        if (_updateOnRoundEnd)
            throw new InvalidOperationException("A watchdog update is already pending.");
        if (_deployment != null)
        {
            if (_deployment.Ticket != ticket)
                throw new InvalidOperationException("Another deployment is already pending.");
            _deployment.Renew(_gameTiming.RealTime);
            return;
        }
        var lease = new RoundEndDeploymentLease(ticket, _gameTiming.RealTime);
        // A failed acknowledgement must never arm a shutdown request.
        WriteDeploymentState(ticket, "pending");
        _deployment = lease;
        _deploymentEarliestShutdown = _gameTiming.RealTime + TimeSpan.FromSeconds(15);
        _chatManager.DispatchServerAnnouncement("A server update is ready. It will be installed after this round ends. The current round will continue normally.");
        _chatManager.SendAdminAnnouncement("OXY deployment queued for the round boundary.");
    }

    public void CancelDeployment(string ticket)
    {
        if (_deployment?.Ticket != ticket)
            return;
        var resumeRound = _deploymentShutdown;
        _deployment = null;
        _deploymentShutdown = false;
        _chatManager.DispatchServerAnnouncement("The pending server update was cancelled or its workflow stopped. The server will continue normally.");
        try
        {
            WriteDeploymentState(ticket, "cancelled");
        }
        catch (Exception)
        {
            Logger.ErrorS("server.update", "Could not write cancelled deployment state; the shutdown request was still cleared.");
        }
        if (resumeRound && _entities.System<GameTicker>().RunLevel == GameRunLevel.PostRound)
            _entities.System<GameTicker>().RestartRound();
    }

    private void WriteDeploymentState(string ticket, string state)
    {
        var json = JsonSerializer.Serialize(new { ticket, state });
        if (_resources.UserData.RootDir is not { } root)
        {
            _resources.UserData.WriteAllText(DeploymentStatePath, json);
            return;
        }
        // Rename within the same data directory so readers never observe a partial JSON receipt.
        var temporary = Path.Combine(root, "reclamation-update.json.tmp");
        File.WriteAllText(temporary, json);
        File.Move(temporary, Path.Combine(root, "reclamation-update.json"), overwrite: true);
    }

    private bool FinishDeployment()
    {
        if (_deploymentShutdown)
            return true;
        if (_deployment == null || !_deployment.IsActive(_gameTiming.RealTime))
            return false;
        var ticket = _deployment.Ticket;
        try
        {
            // Require a matching receipt and an offline server before moving any game files.
            WriteDeploymentState(ticket, "ready");
        }
        catch (Exception)
        {
            CancelDeployment(ticket);
            return false;
        }
        _deploymentShutdown = true;
        _chatManager.DispatchServerAnnouncement("Updating Server. Please reconnect shortly.");
        var reason = JsonSerializer.Serialize(new { reason = "Updating Server", redial = true });
        foreach (var session in _playerManager.Sessions.ToArray())
            session.Channel.Disconnect(reason);
        // Hold at the round boundary. The workflow performs an intentional panel stop,
        // rather than letting Pterodactyl interpret a process exit as a crash and restart it.
        // If the workflow disappears, the lease expires and releases this hold.
        return true;
    }

    private void PlayerManagerOnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        switch (e.NewStatus)
        {
            case SessionStatus.Connecting:
                _restartTime = null;
                break;
            case SessionStatus.Disconnected:
                ServerEmptyUpdateRestartCheck();
                break;
        }
        _cfg.SetCVar("nf14.respawn.time", GetNewRespawnTime(_playerManager.PlayerCount));
    }

    private void WatchdogOnUpdateReceived()
    {
        _chatManager.DispatchServerAnnouncement(Loc.GetString("server-updates-received"));
        // #Misfits Add - Also notify admins specifically in admin chat so it's not missed
        _chatManager.SendAdminAnnouncement(Loc.GetString("misfits-server-update-pending-admin"));
        _updateOnRoundEnd = true;
        ServerEmptyUpdateRestartCheck();
    }

    /// <summary>
    ///     Checks whether there are still players on the server,
    /// and if not starts a timer to automatically reboot the server if an update is available.
    /// </summary>
    private void ServerEmptyUpdateRestartCheck()
    {
        // Can't simple check the current connected player count since that doesn't update
        // before PlayerStatusChanged gets fired.
        // So in the disconnect handler we'd still see a single player otherwise.
        var playersOnline = _playerManager.Sessions.Any(p => p.Status != SessionStatus.Disconnected);
        if (playersOnline || !_updateOnRoundEnd)
        {
            // Still somebody online.
            return;
        }

        if (_restartTime != null)
        {
            // Do nothing because I guess we already have a timer running..?
            return;
        }

        var restartDelay = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.UpdateRestartDelay));
        _restartTime = restartDelay + _gameTiming.RealTime;
    }

    private void DoShutdown()
    {
        _server.Shutdown(Loc.GetString("server-updates-shutdown"));
    }

    float GetNewRespawnTime(int playerCount) =>
        playerCount switch
        {
            > 50 => 1200.0f,
            > 30 => 900.0f,
            > 20 => 600.0f,
            <= 20 => 300.0f,
        };
}
