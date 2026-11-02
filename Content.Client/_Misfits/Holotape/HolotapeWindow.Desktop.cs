using System;
using System.Linq;
using System.Numerics;
using Content.Client.PDA;
using Content.Client._Misfits.WastelandMap;
using Content.Shared._Reclamation.Bunkers;
using Content.Shared.StatusIcon;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Misfits.Holotape;

public sealed partial class HolotapeWindow
{
    public event Action<ComputerMessage>? OnComputerMessage;
    private bool _desktopEnabled;
    private bool _desktopStarted;
    private string _desktopApp="home";
    private ComputerDesktopState? _desktopState;
    private readonly BoxContainer _desktopHeader=new(){Orientation=BoxContainer.LayoutOrientation.Horizontal,SeparationOverride=8,Visible=false};
    private readonly BoxContainer _desktopHome=new(){Orientation=BoxContainer.LayoutOrientation.Vertical,VerticalExpand=true,Visible=false};
    private readonly GridContainer _desktopApps=new(){Columns=2,HorizontalExpand=true};
    private readonly BoxContainer _desktopBody=new(){Orientation=BoxContainer.LayoutOrientation.Vertical,VerticalExpand=true,Visible=false};
    private readonly BoxContainer _desktopLock=new(){Orientation=BoxContainer.LayoutOrientation.Vertical,VerticalExpand=true,Visible=false};
    private readonly Button _desktopName=new(){HorizontalExpand=true};
    private readonly AnimatedTextureRect _desktopOwnIcon=new(){MinSize=new Vector2(48,48),MaxSize=new Vector2(48,48)};
    private readonly Label _desktopTitle=new(){Text="COMPUTER",VerticalAlignment=VAlignment.Center};
    private readonly Button _desktopHomeButton=new(){Text="HOME"};
    private readonly Button _desktopCloseApp=new(){Text="X"};
    private readonly ScrollContainer _desktopScroll=new(){VerticalExpand=true,HorizontalExpand=true,HScrollEnabled=false};
    private readonly BoxContainer _desktopMessages=new(){Orientation=BoxContainer.LayoutOrientation.Vertical,HorizontalExpand=true,SeparationOverride=6};
    private int _desktopScrollFrames;
    private MapViewerControl? _desktopMap;
    private string? _desktopMapTexture;

    private void InitializeDesktop()
    {
        _desktopHeader.AddChild(_desktopHomeButton);_desktopHeader.AddChild(_desktopOwnIcon);_desktopHeader.AddChild(_desktopName);
        _desktopHeader.AddChild(_desktopTitle);_desktopHeader.AddChild(_desktopCloseApp);
        _desktopHome.AddChild(new Label{Text="ROBCO PERSONAL COMPUTER",FontColorOverride=Color.FromHex("#33ff33")});
        _desktopHome.AddChild(_desktopApps);
        _desktopScroll.AddChild(_desktopMessages);
        var layout=(BoxContainer)HeaderLabel.Parent!;
        layout.AddChild(_desktopHeader);_desktopHeader.SetPositionInParent(0);
        layout.AddChild(_desktopHome);layout.AddChild(_desktopBody);layout.AddChild(_desktopLock);
        _desktopHomeButton.OnPressed+=_=>OpenDesktopApp("home");
        _desktopCloseApp.OnPressed+=_=>OpenDesktopApp("home");
        _desktopName.OnPressed+=_=>OpenDesktopApp("profile");
        _desktopOwnIcon.OnKeyBindDown+=args=>{if(args.Function==Robust.Shared.Input.EngineKeyFunctions.UIClick){OpenDesktopApp("profile");args.Handle();}};
        _desktopOwnIcon.MouseFilter=MouseFilterMode.Stop;
        var password=new LineEdit{PlaceHolder="Passcode",HorizontalExpand=true};
        var unlock=new Button{Text="UNLOCK"};
        _desktopLock.AddChild(new Label{Text="TERMINAL LOCKED",FontColorOverride=Color.FromHex("#33ff33")});
        _desktopLock.AddChild(password);_desktopLock.AddChild(unlock);
        void Unlock(){OnComputerMessage?.Invoke(new ComputerMessage(ComputerAction.Unlock,password.Text));password.Text="";}
        password.OnTextEntered+=_=>Unlock();unlock.OnPressed+=_=>Unlock();
        _desktopHome.StyleClasses.Add("PdaContent");_desktopBody.StyleClasses.Add("PdaContent");
    }
    public void UpdateDesktop(ComputerDesktopState state)
    {
        _desktopEnabled=true;_desktopState=state;
        _desktopName.Text=state.Own.Name+"  #"+state.Own.Number;
        SetJobIcon(_desktopOwnIcon,state.Own.Icon);
        if(state.Locked){CloseTerminalGame();_desktopBody.RemoveAllChildren();_desktopApp="home";}
        else if(!_desktopStarted){_desktopStarted=true;_desktopApp="home";}
        RefreshDesktopApps();
        if(!state.Locked && _desktopApp is "public" or "messenger")RenderChat(_desktopApp=="public");
        if(!state.Locked && _desktopApp=="map")UpdateDesktopMap();
        RefreshDesktopLayout();
    }
    public void RefreshDesktopLayout()
    {
        if(!_desktopEnabled)return;
        var locked=_desktopState?.Locked==true;
        _desktopHeader.Visible=!locked;_desktopLock.Visible=locked;
        HeaderLabel.Visible=false;SepTop.Visible=false;
        _desktopHome.Visible=!locked&&_desktopApp=="home";
        _desktopBody.Visible=!locked&&_desktopApp is not ("home" or "command");
        if(locked||_desktopApp!="command")
        {
            DataPanel.Visible=NotesPanel.Visible=LinksPanel.Visible=DatabasePanel.Visible=OverwatchPanel.Visible=false;
            TabBar.Visible=false;
        }
        else TabBar.Visible=true;
    }
    private void RefreshDesktopApps()
    {
        if(!_desktopEnabled)return;
        _desktopApps.RemoveAllChildren();
        void App(string title,string kind,Action action)
        {
            var item=new PdaProgramItem{MinHeight=80};item.ProgramName.Text=title;
            item.Icon.Visible=false;item.Icon.Parent!.Visible=true;
            item.Icon.Parent.AddChild(new ComputerAppIcon(kind){MouseFilter=MouseFilterMode.Ignore,MinSize=new Vector2(44,44),MaxSize=new Vector2(44,44)});
            item.OnPressed+=_=>action();_desktopApps.AddChild(item);
        }
        App("Command Screen","command",()=>OpenDesktopApp("command"));
        App("Terminal Notes","notes",()=>OpenDesktopApp("notes"));
        App("Database","database",()=>OpenDesktopApp("database"));
        if(_gameProgram!=null)App(System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(_gameProgram),_gameProgram,()=>
        {
            OpenDesktopApp("command");_commandInput.Text=_gameProgram??"";RunTerminalCommand();
        });
        App("Map","map",()=>OpenDesktopApp("map"));App("Messenger","messenger",()=>OpenDesktopApp("messenger"));
        App("Public Chat","public",()=>OpenDesktopApp("public"));App("Settings","settings",()=>OpenDesktopApp("settings"));
        App("Lock Screen","lock",()=>
        {
            if(_desktopState?.HasPassword==true)OnComputerMessage?.Invoke(new ComputerMessage(ComputerAction.Lock));
            else OpenDesktopApp("settings");
        });
        RefreshDesktopLayout();
    }
    private void OpenDesktopApp(string app)
    {
        if(_desktopState?.Locked==true)return;
        CloseTerminalGame();_desktopApp=app;_desktopTitle.Text=app.ToUpperInvariant();_desktopBody.RemoveAllChildren();
        switch(app)
        {
            case "command":SwitchToDataTab();_commandInput.GrabKeyboardFocus();break;
            case "notes":_desktopApp="command";SwitchToNotesTab();break;
            case "database":_desktopApp="command";SwitchToDatabaseTab();break;
            case "public":RenderChat(true);break;
            case "messenger":RenderChat(false);break;
            case "profile":RenderProfile(_desktopState?.Own.Number??0);break;
            case "settings":RenderSettings();break;
            case "map":
                _desktopMap=new MapViewerControl{HorizontalExpand=true,VerticalExpand=true,MinHeight=260,ShowPlayerPosition=false};
                _desktopMapTexture=null;_desktopBody.AddChild(_desktopMap);UpdateDesktopMap();break;
        }
        RefreshDesktopLayout();
    }
    private void UpdateDesktopMap()
    {
        if(_desktopMap==null||_desktopState?.Map is not {} map)return;
        if(_desktopMapTexture!=map.MapTexturePath)
        {
            var resources=IoCManager.Resolve<IResourceCache>();
            if(resources.TryGetResource<TextureResource>(new ResPath("/Textures")/new ResPath(map.MapTexturePath),out var texture))
                _desktopMap.SetTexture(texture.Texture,new Box2(map.BoundsLeft,map.BoundsBottom,map.BoundsRight,map.BoundsTop));
            _desktopMapTexture=map.MapTexturePath;
        }
        _desktopMap.SetTrackedBlips(map.TrackedBlips);
    }
    private void RenderSettings()
    {
        _desktopBody.AddChild(new Label{Text="TERMINAL SECURITY"});
        var pin=new LineEdit{PlaceHolder="New passcode (up to 32 characters)"};
        var set=new Button{Text="SET PASSCODE AND LOCK"};set.OnPressed+=_=>{OnComputerMessage?.Invoke(new ComputerMessage(ComputerAction.SetPassword,pin.Text));pin.Text="";};
        var clear=new Button{Text="REMOVE PASSCODE"};clear.OnPressed+=_=>OnComputerMessage?.Invoke(new ComputerMessage(ComputerAction.SetPassword));
        _desktopBody.AddChild(pin);_desktopBody.AddChild(set);_desktopBody.AddChild(clear);
        _desktopBody.AddChild(new Label{Text="Close the computer normally; locking remains in force when reopened."});
    }
    private void RenderChat(bool publicChat)
    {
        if(_desktopState is not {} state)return;
        // Preserve text being typed when history refreshes.
        if(_desktopScroll.Parent==null)
        {
            _desktopBody.RemoveAllChildren();_desktopBody.AddChild(_desktopScroll);
            if(!publicChat)
            {
                var row=new BoxContainer{Orientation=BoxContainer.LayoutOrientation.Horizontal};var number=new LineEdit{PlaceHolder="Pip-Boy / computer number",HorizontalExpand=true};
                var select=new Button{Text="OPEN CHAT"};select.OnPressed+=_=>{if(uint.TryParse(number.Text,out var target))OnComputerMessage?.Invoke(new ComputerMessage(ComputerAction.SelectChat,number:target));};
                row.AddChild(number);row.AddChild(select);_desktopBody.AddChild(row);
                // An empty OptionButton can measure below its textured border height.
                var directory=new OptionButton{HorizontalExpand=true,MinHeight=32};
                directory.AddItem("Select a recipient",0);
                directory.SelectId(0);
                foreach(var person in state.Directory)directory.AddItem(person.Name+" #"+person.Number,(int)person.Number);
                directory.OnItemSelected+=args=>{directory.SelectId(args.Id);if(args.Id>0)OnComputerMessage?.Invoke(new ComputerMessage(ComputerAction.SelectChat,number:(uint)args.Id));};
                _desktopBody.AddChild(directory);
            }
            var input=new LineEdit{PlaceHolder="Message",HorizontalExpand=true};var send=new Button{Text="SEND"};
            void Send(){OnComputerMessage?.Invoke(new ComputerMessage(publicChat?ComputerAction.PublicSend:ComputerAction.DirectSend,input.Text,_desktopState?.SelectedChat??0));input.Text="";}
            input.OnTextEntered+=_=>Send();send.OnPressed+=_=>Send();_desktopBody.AddChild(input);_desktopBody.AddChild(send);
        }
        _desktopMessages.RemoveAllChildren();
        if(publicChat)
        {
            foreach(var message in state.PublicMessages)
            {
                var profile=state.Profiles.FirstOrDefault(p=>p.Number==message.SenderId);
                AddChatLine(message.SenderName,message.Content,profile?.Icon??"",profile?.Description??"",message.SenderId);
            }
        }
        else foreach(var message in state.DirectMessages)
        {
            var profile=state.Profiles.FirstOrDefault(p=>p.Number==message.SenderId);
            var name=profile?.Name??state.Directory.FirstOrDefault(p=>p.Number==message.SenderId).Name??"Unknown";
            AddChatLine(name,message.Content,profile?.Icon??"",profile?.Description??"",message.SenderId);
        }
        _desktopScrollFrames=2;
    }
    private void AddChatLine(string name,string content,string icon,string description,uint number)
    {
        var row=new BoxContainer{Orientation=BoxContainer.LayoutOrientation.Vertical,HorizontalExpand=true};
        var heading=new BoxContainer{Orientation=BoxContainer.LayoutOrientation.Horizontal};
        var image=new AnimatedTextureRect{MinSize=new Vector2(24,24),MaxSize=new Vector2(24,24)};SetJobIcon(image,icon);
        var button=new Button{Text=name,ToolTip=description};button.OnPressed+=_=>{_desktopApp="profile";_desktopBody.RemoveAllChildren();RenderProfile(number);RefreshDesktopLayout();};
        heading.AddChild(image);heading.AddChild(button);row.AddChild(heading);
        var label=new RichTextLabel{HorizontalExpand=true};label.SetMessage(FormattedMessage.FromUnformatted(content));row.AddChild(label);_desktopMessages.AddChild(row);
    }
    private void RenderProfile(uint number)
    {
        if(_desktopState is not {} state)return;
        var profile=number==state.Own.Number?state.Own:state.Profiles.FirstOrDefault(p=>p.Number==number);
        if(profile==null)return;
        _desktopBody.AddChild(new Label{Text="NAME: "+profile.Name+"   #"+profile.Number});
        if(number!=state.Own.Number){var text=new RichTextLabel();text.SetMessage(FormattedMessage.FromUnformatted(profile.Description));_desktopBody.AddChild(text);return;}
        _desktopBody.AddChild(new Label{Text="Your name comes from your character and cannot be edited here."});
        var description=new LineEdit{Text=profile.Description,PlaceHolder="Short description (160 characters)"};_desktopBody.AddChild(description);
        var icons=IoCManager.Resolve<IPrototypeManager>().EnumeratePrototypes<JobIconPrototype>().Where(p=>p.Icon!=null).OrderBy(ProfileIconName).ThenBy(p=>p.ID).ToArray();
        var choose=new OptionButton{HorizontalExpand=true};choose.AddItem("No icon",0);
        for(var i=0;i<icons.Length;i++)choose.AddItem(ProfileIconName(icons[i]),i+1);
        var selected=Array.FindIndex(icons,p=>p.ID==profile.Icon)+1;choose.SelectId(selected);
        var preview=new AnimatedTextureRect{MinSize=new Vector2(64,64),MaxSize=new Vector2(64,64)};SetJobIcon(preview,profile.Icon);
        choose.OnItemSelected+=args=>{selected=args.Id;choose.SelectId(selected);SetJobIcon(preview,selected==0?"":icons[selected-1].ID);};
        _desktopBody.AddChild(choose);_desktopBody.AddChild(preview);
        var save=new Button{Text="SAVE PROFILE"};save.OnPressed+=_=>OnComputerMessage?.Invoke(new ComputerMessage(ComputerAction.ProfileSave,description.Text,icon:selected==0?"":icons[selected-1].ID));_desktopBody.AddChild(save);
    }
    private static string ProfileIconName(JobIconPrototype icon) =>
        Loc.TryGetString("bunker-profile-icon-"+icon.ID.ToLowerInvariant(),out var name)
            ? name : Loc.GetString("bunker-profile-icon-generic");

    private static void SetJobIcon(AnimatedTextureRect control,string id)
    {
        control.DisplayRect.Stretch=TextureRect.StretchMode.KeepAspectCentered;
        var prototypes=IoCManager.Resolve<IPrototypeManager>();
        control.Visible=id.Length>0&&prototypes.TryIndex<JobIconPrototype>(id,out _);
        if(control.Visible)control.SetFromSpriteSpecifier(prototypes.Index<JobIconPrototype>(id).Icon);
    }
}
