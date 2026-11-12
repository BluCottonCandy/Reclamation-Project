using System;
using System.Collections.Generic;
using Content.Shared._Misfits.PipBoy;
using Content.Shared.DeltaV.CartridgeLoader.Cartridges;
using Robust.Shared.Serialization;
namespace Content.Shared._Reclamation.Bunkers;
[Serializable,NetSerializable]
public enum ComputerAction:byte {Refresh,PublicSend,DirectSend,SelectChat,ProfileSave,SetPassword,Unlock,Lock}
[Serializable,NetSerializable]
public sealed class ComputerMessage(ComputerAction action,string text="",uint number=0,string icon="") : BoundUserInterfaceMessage
{
    public readonly ComputerAction Action=action;
    public readonly string Text=text;
    public readonly uint Number=number;
    public readonly string Icon=icon;
}
[Serializable,NetSerializable]
public sealed record ComputerProfile(uint Number,string Name,string Description,string Icon);
[Serializable,NetSerializable]
public sealed class ComputerDesktopState
{
    public bool Locked;
    public bool HasPassword;
    public ComputerProfile Own=new(0,"","","");
    public List<ComputerProfile> Profiles=new();
    public List<PipBoyGroupMessage> PublicMessages=new();
    public List<PipBoyDirectoryEntry> Directory=new();
    public List<NanoChatMessage> DirectMessages=new();
    public uint SelectedChat;
}
[Serializable,NetSerializable]
public sealed class ComputerDesktopUpdate(ComputerDesktopState state) : BoundUserInterfaceMessage
{
    public readonly ComputerDesktopState State=state;
}
