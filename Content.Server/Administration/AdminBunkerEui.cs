using System.Threading.Tasks;
using Robust.Shared.GameObjects;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Server._Reclamation.Bunkers;
using Content.Shared._ReclamationProject.Bunkers;
using Content.Shared.Administration;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Eui;

namespace Content.Server.Administration;

public sealed class AdminBunkerEui : BaseEui
{
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly ISharedAdminLogManager _logs = default!;
    private List<AdminBunkerEntry> _entries = new();
    private string _status = "";
    private bool _busy;
    public AdminBunkerEui() { IoCManager.InjectDependencies(this); }
    private PersonalBunkerSystem Bunkers => _entities.System<PersonalBunkerSystem>();
    public override void Opened()
    {
        _admins.OnPermsChanged += PermsChanged;
        Refresh();
    }
    public override void Closed() { _admins.OnPermsChanged -= PermsChanged; }
    private void PermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player && !_admins.HasAdminFlag(Player, AdminFlags.Admin)) Close();
    }
    public override EuiStateBase GetNewState() => new AdminBunkerEuiState(_entries, _status, _busy);
    private async void Refresh()
    {
        if (_busy || IsShutDown || !_admins.HasAdminFlag(Player, AdminFlags.Admin)) return;
        _busy = true; _status = "Loading saved bunkers..."; StateDirty();
        try
        {
            var entries = await Bunkers.GetAdminBunkerList();
            if (IsShutDown || !_admins.HasAdminFlag(Player, AdminFlags.Admin)) return;
            _entries = entries;
            _status = $"{entries.Count} saved bunkers. Use an admin ghost to visit.";
        }
        catch (Exception) { _status = "Could not read the bunker list. Try Refresh."; }
        finally { _busy = false; if (!IsShutDown) StateDirty(); }
    }
    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (IsShutDown) return;
        if (!_admins.HasAdminFlag(Player, AdminFlags.Admin)) { Close(); return; }
        if (_busy) return;
        switch (msg)
        {
            case AdminBunkerRefreshMessage: Refresh(); break;
            case AdminBunkerVisitMessage visit:
                var entry = _entries.Find(e => e.Key == visit.Key);
                if (entry == null) { _status = "Unknown bunker. Refresh the list."; break; }
                if (Bunkers.VisitAdminBunker(Player, entry.Key, out _status))
                    _logs.Add(LogType.Action, $"{Player:actor} visited bunker for {entry.CharacterName} ({entry.Ckey}), key {entry.Key}");
                break;
            case AdminBunkerReturnMessage:
                _status = Player.AttachedEntity is { } ghost && Bunkers.ReturnAdminFromBunker(ghost)
                    ? "Returned to your previous location." : "No valid return location, or you are not an active admin ghost.";
                break;
        }
        StateDirty();
    }
}
