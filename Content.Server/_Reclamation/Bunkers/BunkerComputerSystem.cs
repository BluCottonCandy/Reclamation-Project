using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Content.Server.Access.Systems;
using Content.Server._Misfits.Holotape;
using Content.Server._Misfits.PipBoy;
using Content.Server.UserInterface;
using Content.Shared._Reclamation.Bunkers;
using Content.Shared._Misfits.Holotape;
using Content.Shared._Misfits.PipBoy;
using Content.Shared.Access.Components;
using Content.Shared.DeltaV.NanoChat;
using Content.Shared.DeltaV.CartridgeLoader.Cartridges;
using Content.Shared.GameTicking;
using Content.Shared.StatusIcon;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Reclamation.Bunkers;

/// <summary>Computer desktop adapter reusing Pip-Boy locks, group history, and NanoChat.</summary>
public sealed class BunkerComputerSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui=default!;
    [Dependency] private readonly PipBoyNetworkSystem _network=default!;
    [Dependency] private readonly SharedNanoChatSystem _nano=default!;
    [Dependency] private readonly IdCardSystem _ids=default!;
    [Dependency] private readonly IGameTiming _time=default!;
    [Dependency] private readonly IResourceManager _resources=default!;
    [Dependency] private readonly IPrototypeManager _prototypes=default!;
    [Dependency] private readonly HolotapeSystem _holotapes=default!;
    public sealed class SavedProfile
    {
        public string Description {get;set;}="";
        public string Icon {get;set;}="";
    }
    private sealed class Profile(string key,EntityUid card,uint number,string name,SavedProfile saved)
    {
        public readonly string Key=key;
        public readonly EntityUid Card=card;
        public readonly uint Number=number;
        public readonly string Name=name;
        public readonly SavedProfile Saved=saved;
        public TimeSpan LastSend=TimeSpan.FromSeconds(-60);
        public ComputerProfile View()=>new(Number,Name,Saved.Description,Saved.Icon);
    }
    private Dictionary<string,SavedProfile> _saved=new();
    private readonly Dictionary<string,Profile> _profiles=new();
    private readonly Dictionary<(EntityUid,EntityUid),uint> _selected=new();
    private readonly Dictionary<(EntityUid,EntityUid),string> _signatures=new();
    private static readonly ResPath SavePath=new("/computer_profiles.json");
    private uint _nextNumber=1_000_000;
    private uint? _publicGroup;
    private long _revision;
    private float _elapsed;

    public override void Initialize()
    {
        base.Initialize();
        try{if(_resources.UserData.Exists(SavePath))_saved=JsonSerializer.Deserialize<Dictionary<string,SavedProfile>>(_resources.UserData.ReadAllText(SavePath))??new();}
        catch(Exception ex){Log.Error("Failed to load computer profiles: "+ex.Message);}
        SubscribeLocalEvent<BunkerComputerComponent,ComputerMessage>(OnMessage);
        SubscribeLocalEvent<BunkerComputerComponent,BoundUIOpenedEvent>(OnOpen);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }
    private Profile GetProfile(EntityUid actor)
    {
        var name=Name(actor);
        var account=TryComp<ActorComponent>(actor,out var session)?session.PlayerSession.UserId.UserId.ToString("N"):"entity:"+actor;
        var key=account+":"+name;
        if(_profiles.TryGetValue(key,out var profile))return profile;
        if(!_saved.TryGetValue(key,out var saved))saved=new SavedProfile();
        var card=EntityManager.Spawn();
        EnsureComp<IdCardComponent>(card);EnsureComp<NanoChatCardComponent>(card);
        while(_network.FindCardByNumber(_nextNumber)!=null)_nextNumber++;
        var number=_nextNumber++;_nano.SetNumber(card,number);_ids.TryChangeFullName(card,name);
        profile=new Profile(key,card,number,name,saved);_profiles[key]=profile;return profile;
    }
    private uint PublicGroup()
    {
        if(_publicGroup is {} id && _network.GetGroup(id)!=null)return id;
        var group=_network.CreateGroup("Wasteland public computer channel",0);_publicGroup=group;return group;
    }
    private void OnOpen(EntityUid uid,BunkerComputerComponent comp,BoundUIOpenedEvent args)
    {
        if(args.UiKey.Equals(HolotapeUiKey.Key))Push(uid,args.Actor,true);
    }
    private void OnMessage(EntityUid uid,BunkerComputerComponent comp,ComputerMessage msg)
    {
        var security=EnsureComp<PipBoyNetworkComponent>(uid);
        if(security.IsLocked && msg.Action!=ComputerAction.Unlock && msg.Action!=ComputerAction.Refresh){Push(uid,msg.Actor,true);return;}
        var profile=GetProfile(msg.Actor);
        var text=(msg.Text??"").Trim();
        switch(msg.Action)
        {
            case ComputerAction.Unlock:_network.TryUnlock((uid,security),text);break;
            case ComputerAction.Lock:_network.Lock((uid,security));break;
            case ComputerAction.SetPassword:
                if(text.Length<=32)_network.SetPassword((uid,security),text.Length==0?null:text);
                break;
            case ComputerAction.ProfileSave:
                var iconId=msg.Icon??"";
                if(iconId.Length>0 && (!_prototypes.TryIndex<JobIconPrototype>(iconId,out var icon)||icon.Icon==null))return;
                profile.Saved.Description=text[..Math.Min(160,text.Length)];profile.Saved.Icon=iconId;
                _saved[profile.Key]=profile.Saved;
                try{_resources.UserData.WriteAllText(SavePath,JsonSerializer.Serialize(_saved));}
                catch(Exception ex){Log.Error("Failed to save computer profile: "+ex.Message);}
                break;
            case ComputerAction.SelectChat:_selected[(uid,msg.Actor)]=msg.Number;break;
            case ComputerAction.PublicSend:
            case ComputerAction.DirectSend:
                if(text.Length==0 || _time.CurTime-profile.LastSend<TimeSpan.FromSeconds(.5))return;
                profile.LastSend=_time.CurTime;text=text[..Math.Min(NanoChatMessage.MaxContentLength,text.Length)];
                if(msg.Action==ComputerAction.PublicSend)
                    _network.AddGroupMessage(PublicGroup(),new PipBoyGroupMessage(_time.CurTime,text,profile.Number,Name(msg.Actor)));
                else SendDirect(uid,msg.Actor,profile,msg.Number,text);
                break;
        }
        _revision++;
        if(msg.Action is ComputerAction.Lock or ComputerAction.Unlock or ComputerAction.SetPassword)
            foreach(var actor in _ui.GetActors(uid,HolotapeUiKey.Key).ToArray())_holotapes.RefreshTerminalState(uid,actor);
        RefreshOpen();
    }
    private void SendDirect(EntityUid computer,EntityUid actor,Profile source,uint number,string text)
    {
        var target=_network.FindCardByNumber(number);if(target==null||number==source.Number)return;
        var targetInfo=_network.GetCardDisplayInfo(target.Value);
        _nano.EnsureRecipientExists(source.Card,number,new NanoChatRecipient(number,targetInfo.Name,targetInfo.JobTitle));
        _nano.EnsureRecipientExists(target.Value,source.Number,new NanoChatRecipient(source.Number,source.Name));
        var message=new NanoChatMessage(_time.CurTime,text,source.Number);
        _nano.AddMessage(source.Card,number,message);_nano.AddMessage(target.Value,source.Number,message);
        _selected[(computer,actor)]=number;
    }
    public ComputerDesktopState BuildState(EntityUid uid,EntityUid actor)
    {
        var profile=GetProfile(actor);var security=EnsureComp<PipBoyNetworkComponent>(uid);
        var state=new ComputerDesktopState{Locked=security.IsLocked,HasPassword=security.Password!=null,Own=profile.View()};
        if(state.Locked)return state;
        state.PublicMessages=_network.GetGroup(PublicGroup())!.Messages.ToList();
        state.Profiles=_profiles.Values.Select(p=>p.View()).ToList();
        state.Directory=_network.GetDirectory(profile.Number);
        foreach(var item in _profiles.Values.Where(p=>p.Number!=profile.Number))state.Directory.Add(new PipBoyDirectoryEntry(item.Number,item.Name));
        state.SelectedChat=_selected.GetValueOrDefault((uid,actor));
        if(state.SelectedChat!=0)state.DirectMessages=_nano.GetMessagesForRecipient(profile.Card,state.SelectedChat)?.TakeLast(200).ToList()??new();
        return state;
    }
    private void Push(EntityUid uid,EntityUid actor,bool force=false)
    {
        if(!Exists(actor))return;
        var profile=GetProfile(actor);
        var signature=_revision+":"+(_nano.GetLastMessageTime(profile.Card)?.Ticks??0)+":"+BunkerComputerSecurity.IsLocked(EntityManager,uid);
        if(!force&&_signatures.GetValueOrDefault((uid,actor))==signature)return;
        _signatures[(uid,actor)]=signature;
        // Personal messages are never placed in the terminal's shared/broadcast UI state.
        _ui.ServerSendUiMessage(uid,HolotapeUiKey.Key,new ComputerDesktopUpdate(BuildState(uid,actor)),actor);
    }
    private void RefreshOpen()
    {
        var query=EntityQueryEnumerator<BunkerComputerComponent>();
        while(query.MoveNext(out var uid,out _))foreach(var actor in _ui.GetActors(uid,HolotapeUiKey.Key).ToArray())Push(uid,actor);
    }
    public override void Update(float frameTime)
    {
        _elapsed+=frameTime;if(_elapsed<2)return;_elapsed=0;RefreshOpen();
    }
    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        if(_publicGroup is {} group)_network.RemoveGroup(group);_publicGroup=null;
        foreach(var profile in _profiles.Values)if(Exists(profile.Card))QueueDel(profile.Card);
        _profiles.Clear();_selected.Clear();_signatures.Clear();_nextNumber=1_000_000;_revision++;
    }
}
