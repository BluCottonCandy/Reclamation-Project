using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared.Input;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.Holotape;

/// <summary>
/// Original, client-only terminal arcade shooter. No world entities, network traffic,
/// external assets, or save data. Uses the existing arcade key bindings.
/// </summary>
public sealed class RedMenaceControl : Control, IHolotapeGame
{
    public event Action? OnQuit;
    public event Action<string>? OnStatus;
    private static readonly string[] Arena = BuildArena();
    private static string[] BuildArena()
    {
        var cells = new char[20][];
        for (var y = 0; y < cells.Length; y++) cells[y] = new string('#', 24).ToCharArray();
        // Nine chambers connected by narrow corridors; all perimeter walls stay sealed.
        foreach (var top in new[] {1, 8, 15})
        foreach (var left in new[] {1, 9, 17})
            for (var y = top; y < Math.Min(top + 5, 19); y++)
            for (var x = left; x < left + 6; x++) cells[y][x] = '.';
        foreach (var y in new[] {3, 10, 17}) for (var x = 1; x < 23; x++) cells[y][x] = '.';
        for (var y=5;y<=8;y++) cells[y][20]='.';
        for (var y=12;y<=15;y++) cells[y][4]='.';
        // Different room silhouettes while preserving the central corridors and pickup locations.
        var roomIndex=0;
        foreach(var top in new[]{1,8,15})foreach(var left in new[]{1,9,17})
        {
            var corners=roomIndex++%4;
            cells[top+1][left+(corners==0?1:5)]='#';
            if(corners==1)cells[top][left+1]='#';
            if(corners==2 && top+4<19)cells[top+4][left+2]='#';
            if(corners==3)cells[top+1][left+2]='#';
            // Sealed exterior windows: visible sky, never a route out of the arena.
            if(top==1)for(var x=left+1;x<left+5;x++)cells[0][x]='W';
            if(left==17)for(var y=top+1;y<Math.Min(top+4,19);y++)cells[y][23]='W';
        }
        return cells.Select(row => new string(row)).ToArray();
    }
    // Original pixel art: red robots with gold star insignia, and a blond vault-suit portrait.
    private static readonly string[] Robot =
    {
        "....rrr.....", "...rrrrr....", "..rryrrrr...", "..kkkkkkk...",
        "..kwwkwwk...", "..kkkkkkk...", "...kkkkk....", ".rrrrrrrrr..",
        "rrrgrygrrrr.", "rrrgyyygrrr.", "kkrrryrrrrkk", "..rrrrrrr...",
        "..rrr.rrr...", "..rrr.rrr...", ".kkkk.kkkk.."
    };
    private static readonly string[] Portrait =
    {
        "...yyyyy....", "..yyyyyyy...", "..ysssyyy...", "..ssssssy...",
        "..skssks....", "..ssssss....", "...skks.....", "....ss......",
        "..bbbybbb...", ".bbbbybbbb..", ".bbbbybbbb..", ".ssbbybbss..",
        "...bbybb....", "...bb.bb....", "...kk.kk...."
    };
    private enum Weapon { Pistol, Shotgun, Launcher, DualPistols }
    private enum PickupKind { Health, Ammo, Armor, Weapon }
    private sealed class Pickup(Vector2 position, int room, PickupKind kind, Weapon weapon = Weapon.Pistol)
    {
        public readonly Vector2 Position = position;
        public readonly int Room = room;
        public readonly PickupKind Kind = kind;
        public readonly Weapon Weapon = weapon;
        public bool Taken;
    }
    private enum EnemyKind { Scout, Guard, Marksman, Brute, Commander }
    private sealed class RobotEnemy(Vector2 position, int room, bool elite = false, EnemyKind kind = EnemyKind.Guard)
    {
        public Vector2 Position = position;
        public readonly int Room = room;
        public readonly bool Elite = elite;
        public readonly EnemyKind Kind=kind;
        public int Health = kind==EnemyKind.Brute?10:elite?8:kind==EnemyKind.Scout?2:3;
        public float Attack = 1.5f;
        public float Speed => Kind switch {EnemyKind.Scout=>.95f,EnemyKind.Marksman=>.25f,EnemyKind.Brute=>.4f,EnemyKind.Commander=>.65f,_=>.55f};
        public float Range => Kind switch {EnemyKind.Scout=>2.5f,EnemyKind.Marksman=>7,EnemyKind.Brute=>1.2f,_=>5};
        public int Damage => Kind switch {EnemyKind.Scout=>4,EnemyKind.Marksman=>7,EnemyKind.Brute=>15,EnemyKind.Commander=>10,_=>5};
    }
    private static readonly int[] RoomOrder = {0,1,2,5,4,3,6,7,8};
    private static readonly (int X,int Y)[] Gates = {(7,3),(15,3),(20,6),(15,10),(7,10),(4,13),(7,17),(15,17)};
    private readonly List<Pickup> _pickups = new();
    private readonly bool[] _owned = new bool[4];
    private readonly int[] _rounds = new int[4];
    private readonly int[] _reserve = new int[4];
    private static readonly int[] Capacity = {0,10,5,50};
    private static readonly int[] Magazine = {0,5,1,20};
    private Weapon _weapon;
    private int _unlockedRoom;
    private float _reload;
    private const float ReloadDuration = .9f;
    private readonly Random _random = new();
    private readonly List<RobotEnemy> _robots = new();
    private readonly HashSet<BoundKeyFunction> _held = new();
    private Vector2 _position;
    private float _angle;
    private int _health;
    private int _armor;

    private float _time;
    private int _kills;
    private float _shot;
    private float _hurt;
    private bool _ended;
    private readonly float[] _depth = new float[160];

    public RedMenaceControl()
    {
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    public void Restart()
    {
        _held.Clear();
        _position = new Vector2(1.5f, 1.5f);
        _angle = 0;
        _health = 100;
        _armor = 0;
        Array.Clear(_owned); Array.Clear(_rounds); Array.Clear(_reserve);
        _owned[0]=true; _weapon=Weapon.Pistol; _unlockedRoom=0; _reload=0;
        _pickups.Clear();
        _time = 0;
        _kills = 0;
        _shot = _hurt = 0;
        _ended = false;
        _robots.Clear();
        var firstWeapons = new[] {Weapon.Shotgun,Weapon.Launcher,Weapon.DualPistols}.OrderBy(_=>_random.Next()).ToArray();
        for (var room=0;room<RoomOrder.Length;room++)
        {
            var cell=RoomOrder[room];var left=new[]{1,9,17}[cell%3];var top=new[]{1,8,15}[cell/3];
            _robots.Add(new RobotEnemy(new Vector2(left+4.5f,top+1.5f),room,false,(EnemyKind)(room%3)));
            _robots.Add(new RobotEnemy(new Vector2(left+4.5f,top+3.5f),room,true,room%2==0?EnemyKind.Brute:EnemyKind.Commander));
            var weapon=room<3?firstWeapons[room]:(Weapon)_random.Next(1,4);
            _pickups.Add(new Pickup(new Vector2(left+2.5f,top+2.5f),room,PickupKind.Weapon,weapon));
            _pickups.Add(new Pickup(new Vector2(left+1.5f,top+3.5f),room,PickupKind.Health));
            _pickups.Add(new Pickup(new Vector2(left+3.5f,top+3.5f),room,PickupKind.Ammo));
            _pickups.Add(new Pickup(new Vector2(left+.5f,top+2.5f),room,PickupKind.Armor));
        }
        Status();
    }

    private string WeaponName => _weapon switch { Weapon.Shotgun=>"SHOTGUN", Weapon.Launcher=>"MISSILE LAUNCHER", Weapon.DualPistols=>"DUAL PISTOLS", _=>"PISTOL" };
    private void Status() => OnStatus?.Invoke(_ended
        ? (_health <= 0 ? "VAULT CARNAGE - GAME OVER. Space: restart | Escape: quit" : "VAULT CARNAGE - SECTOR LIBERATED! Space: restart | Escape: quit")
        : $"ROOM {_unlockedRoom+1}/9  {WeaponName}  " +
          (_reload>0?"RELOADING":_weapon==Weapon.Pistol?"UNLIMITED":$"{_rounds[(int)_weapon]} + {_reserve[(int)_weapon]} ROUNDS"));

    private void SelectWeapon(Weapon weapon)
    {
        if (!_owned[(int)weapon] || _reload>0) return;
        _weapon=weapon;Status();
    }

    private void BeginReload()
    {
        var i=(int)_weapon;
        if (_reload>0 || _weapon!=Weapon.Pistol && (_reserve[i]<=0 || _rounds[i]>=Magazine[i])) return;
        _reload=ReloadDuration;Status();
    }

    private void FinishReload()
    {
        var i=(int)_weapon;var transfer=Math.Min(Magazine[i]-_rounds[i],_reserve[i]);
        if(_weapon!=Weapon.Pistol){_rounds[i]+=transfer;_reserve[i]-=transfer;}
        _reload=0;Status();
    }

    private void Refill(Weapon weapon)
    {
        var i=(int)weapon;if(i==0)return;
        _rounds[i]=Magazine[i];_reserve[i]=Capacity[i]-Magazine[i];
    }

    private void CollectPickups()
    {
        foreach(var item in _pickups)
        {
            if(item.Taken || item.Room>_unlockedRoom || Vector2.Distance(item.Position,_position)>.65f)continue;
            if(item.Kind==PickupKind.Health && _health>=100)continue;
            if(item.Kind==PickupKind.Armor && _armor>=100)continue;
            if(item.Kind==PickupKind.Ammo && !Enumerable.Range(1,3).Any(i=>_owned[i] && _rounds[i]+_reserve[i]<Capacity[i]))continue;
            item.Taken=true;
            if(item.Kind==PickupKind.Health)_health=Math.Min(100,_health+50);
            else if(item.Kind==PickupKind.Armor)_armor=100;
            else if(item.Kind==PickupKind.Ammo)
            {
                for(var i=1;i<4;i++)if(_owned[i])Refill((Weapon)i);
            }
            else
            {
                _owned[(int)item.Weapon]=true;Refill(item.Weapon);
                if(_reload<=0)_weapon=item.Weapon;
            }
            Status();
        }
    }

    private void TakeHit(int damage)
    {
        var absorbed=Math.Min(_armor,damage/2);
        _armor-=absorbed;
        _health=Math.Max(0,_health-(damage-absorbed));
    }

    private void AdvanceRooms()
    {
        while(_unlockedRoom<8 && _robots.All(r=>r.Room!=_unlockedRoom || r.Health<=0))_unlockedRoom++;
        if(_robots.All(r=>r.Health<=0))_ended=true;
        Status();
    }

    private bool Wall(Vector2 point)
    {
        var x = (int)MathF.Floor(point.X); var y = (int)MathF.Floor(point.Y);
        if(y<0 || y>=Arena.Length || x<0 || x>=Arena[0].Length || Arena[y][x]!='.')return true;
        for(var gate=0;gate<Gates.Length;gate++)if(Gates[gate]==(x,y) && _unlockedRoom<=gate)return true;
        return false;
    }

    private bool Clear(Vector2 from, Vector2 to)
    {
        var steps = Math.Max(1, (int)(Vector2.Distance(from,to) * 16));
        for (var i = 1; i <= steps; i++)
            if (Wall(Vector2.Lerp(from,to,(float)i / steps))) return false;
        return true;
    }

    private void Move(Vector2 delta)
    {
        bool Fits(Vector2 pos) => !Wall(pos + new Vector2(.18f,.18f)) && !Wall(pos + new Vector2(-.18f,.18f))
            && !Wall(pos + new Vector2(.18f,-.18f)) && !Wall(pos - new Vector2(.18f,.18f));
        var next = _position + new Vector2(delta.X,0);
        if (Fits(next)) _position = next;
        next = _position + new Vector2(0,delta.Y);
        if (Fits(next)) _position = next;
    }

    private void Fire()
    {
        if (_ended) { Restart(); return; }
        if (_shot>0 || _reload>0)return;
        var index=(int)_weapon;var cost=_weapon==Weapon.DualPistols?2:1;
        if(_weapon!=Weapon.Pistol && _rounds[index]<cost){BeginReload();return;}
        if(_weapon!=Weapon.Pistol)_rounds[index]-=cost;
        _shot=_weapon switch {Weapon.Shotgun=>.65f,Weapon.Launcher=>.8f,_=>.28f};
        var forward=new Vector2(MathF.Cos(_angle),MathF.Sin(_angle));
        var candidates=_robots.Where(r=>r.Health>0 && r.Room<=_unlockedRoom && Clear(_position,r.Position))
            .Where(r=>Vector2.Dot(Vector2.Normalize(r.Position-_position),forward)>(_weapon==Weapon.Shotgun?.93f:.985f))
            .OrderBy(r=>Vector2.DistanceSquared(r.Position,_position)).ToArray();
        void Damage(RobotEnemy target,int amount){var alive=target.Health>0;target.Health-=amount;if(alive && target.Health<=0)_kills++;}
        if(_weapon==Weapon.Shotgun)
        {
            foreach(var target in candidates.Take(3))Damage(target,Vector2.Distance(target.Position,_position)<4?4:2);
        }
        else if(candidates.FirstOrDefault() is {} hit)
        {
            if(_weapon==Weapon.Launcher)
            {
                foreach(var target in _robots.Where(r=>r.Health>0 && r.Room<=_unlockedRoom && Vector2.Distance(r.Position,hit.Position)<2.5f && Clear(hit.Position,r.Position)).ToArray())Damage(target,6);
            }
            else Damage(hit,_weapon==Weapon.DualPistols?2:1);
        }
        AdvanceRooms();
        if(_weapon!=Weapon.Pistol && _rounds[index]<cost && _reserve[index]>0)BeginReload();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.CloseModals || args.Function == EngineKeyFunctions.TextReleaseFocus)
        {
            args.Handle(); OnQuit?.Invoke(); return;
        }
        if(args.Function==ContentKeyFunctions.ToggleStanding){BeginReload();args.Handle();return;}
        var weaponKeys=new[]{ContentKeyFunctions.Hotbar1,ContentKeyFunctions.Hotbar2,ContentKeyFunctions.Hotbar3,ContentKeyFunctions.Hotbar4};
        for(var i=0;i<weaponKeys.Length;i++)if(args.Function==weaponKeys[i]){SelectWeapon((Weapon)i);args.Handle();return;}
        if (args.Function == ContentKeyFunctions.Arcade1 || args.Function == EngineKeyFunctions.UIClick)
        {
            GrabKeyboardFocus(); Fire(); args.Handle(); return;
        }
        if (args.Function == ContentKeyFunctions.ArcadeUp || args.Function == ContentKeyFunctions.ArcadeDown ||
            args.Function == ContentKeyFunctions.ArcadeLeft || args.Function == ContentKeyFunctions.ArcadeRight)
        {
            _held.Add(args.Function);
            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.MoveUp || args.Function == EngineKeyFunctions.MoveDown ||
                 args.Function == EngineKeyFunctions.MoveLeft || args.Function == EngineKeyFunctions.MoveRight)
        {
            _held.Add(args.Function); // Existing WASD bindings; consume them before world movement.
            args.Handle();
        }
        else
            base.KeyBindDown(args); // Let unrelated UI bindings fall through to the arcade bindings.
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        _held.Remove(args.Function);
        args.Handle();
    }

    protected override void KeyboardFocusExited()
    {
        _held.Clear();
        base.KeyboardFocusExited();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree || _ended) return;
        var dt = Math.Min(args.DeltaSeconds,.05f);
        _time += dt;
        if(_reload>0){_reload-=dt;if(_reload<=0)FinishReload();}
        _shot = Math.Max(0,_shot-dt); _hurt = Math.Max(0,_hurt-dt);
        if (!HasKeyboardFocus()) return;
        var turn = (_held.Contains(ContentKeyFunctions.ArcadeRight) || _held.Contains(EngineKeyFunctions.MoveRight) ? 1 : 0)
            - (_held.Contains(ContentKeyFunctions.ArcadeLeft) || _held.Contains(EngineKeyFunctions.MoveLeft) ? 1 : 0);
        _angle += turn * dt * 2.2f;
        var move = (_held.Contains(ContentKeyFunctions.ArcadeUp) || _held.Contains(EngineKeyFunctions.MoveUp) ? 1 : 0)
            - (_held.Contains(ContentKeyFunctions.ArcadeDown) || _held.Contains(EngineKeyFunctions.MoveDown) ? 1 : 0);
        Move(new Vector2(MathF.Cos(_angle),MathF.Sin(_angle)) * (move * dt * 2.3f));
        CollectPickups();
        foreach (var robot in _robots)
        {
            if (robot.Health <= 0 || robot.Room>_unlockedRoom) continue;
            robot.Attack -= dt;
            var distance = Vector2.Distance(_position,robot.Position);
            if (!Clear(robot.Position,_position)) continue;
            if (distance > .7f)
            {
                var next = robot.Position + Vector2.Normalize(_position-robot.Position) * (dt * robot.Speed);
                if (!Wall(next)) robot.Position = next;
            }
            if (distance < robot.Range && robot.Attack <= 0)
            {
                robot.Attack = robot.Kind==EnemyKind.Scout?1.4f:robot.Kind==EnemyKind.Marksman?2.8f:robot.Elite?1.8f:2.3f;
                TakeHit(robot.Damage); _hurt = .15f;
                if (_health == 0) _ended = true;
                Status();
            }
        }
    }

    // A tiny original bitmap font keeps the status bar sharp at any terminal size.
    private static readonly Dictionary<char, string[]> HudFont = new()
    {
        ['0']="111/101/101/101/111".Split('/'), ['1']="010/110/010/010/111".Split('/'),
        ['2']="110/001/010/100/111".Split('/'), ['3']="110/001/010/001/110".Split('/'),
        ['4']="101/101/111/001/001".Split('/'), ['5']="111/100/110/001/110".Split('/'),
        ['6']="011/100/111/101/111".Split('/'), ['7']="111/001/010/010/010".Split('/'),
        ['8']="111/101/111/101/111".Split('/'), ['9']="111/101/111/001/110".Split('/'),
        ['A']="010/101/111/101/101".Split('/'), ['M']="101/111/111/101/101".Split('/'),
        ['O']="111/101/101/101/111".Split('/'), ['H']="101/101/111/101/101".Split('/'),
        ['E']="111/100/110/100/111".Split('/'), ['L']="100/100/100/100/111".Split('/'),
        ['T']="111/010/010/010/010".Split('/'), ['R']="110/101/110/101/101".Split('/'),
        ['S']="011/100/010/001/110".Split('/'), ['K']="101/101/110/101/101".Split('/'),
        ['I']="111/010/010/010/111".Split('/'), ['B']="110/101/110/101/110".Split('/'),
        ['U']="101/101/101/101/111".Split('/'), ['N']="101/111/111/111/101".Split('/'),
        ['F']="111/100/110/100/100".Split('/'),
        ['%']="101/001/010/100/101".Split('/'), ['/']="001/001/010/100/100".Split('/')
    };

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var width = Size.X; var height = Size.Y; var viewHeight = height * .77f;
        var black = Color.FromHex("#010601"); var dark = Color.FromHex("#082008");
        var green = Color.FromHex("#4bd54b"); var bright = Color.FromHex("#a1ffa1");
        void Rect(float x,float y,float w,float h,Color color) => handle.DrawRect(UIBox2.FromDimensions(new Vector2(x,y),new Vector2(w,h)),color);
        void Text(string text, float x, float y, float pixel, Color color)
        {
            foreach (var letter in text)
            {
                if (HudFont.TryGetValue(letter, out var glyph))
                    for (var row=0;row<5;row++) for(var col=0;col<3;col++)
                        if(glyph[row][col]=='1')Rect(x+col*pixel,y+row*pixel,pixel,pixel,color);
                x += pixel*4;
            }
        }
        Rect(0,0,width,viewHeight,black);
        Rect(0,0,width,viewHeight/2,Color.FromHex("#061806"));
        // Simple slow-moving block clouds above the horizon.
        for(var cloud=0;cloud<5;cloud++)
        {
            var x=(cloud*width*.27f+_time*3)%(width+120)-80;
            var y=viewHeight*(.07f+.035f*(cloud%3));
            Rect(x,y,80,10,Color.FromHex("#153515"));Rect(x+15,y-7,42,8,Color.FromHex("#1c421c"));
        }
        for(var line=-3;line<8;line++)handle.DrawLine(new Vector2(width/2,viewHeight/2),new Vector2(line*width/4f,viewHeight),Color.FromHex("#102710"));
        // Perspective floor joints and ceiling beams give each chamber more depth.
        for(var row=1;row<14;row++)
        {
            var y=viewHeight/2+viewHeight/2*(row*row/196f);
            Rect(0,y,width,1,Color.FromHex("#123112"));
            Rect(0,viewHeight-y,width,1,Color.FromHex("#0b200b"));
        }
        const float fov = 1.1f;
        for (var x=0; x<_depth.Length; x++)
        {
            var rayAngle = _angle + ((float)x / _depth.Length-.5f)*fov;
            var dir = new Vector2(MathF.Cos(rayAngle),MathF.Sin(rayAngle));
            var distance=.02f;
            while (distance < 32 && !Wall(_position+dir*distance)) distance += .04f;
            _depth[x] = distance*MathF.Cos(rayAngle-_angle);
            var wallHeight = Math.Min(viewHeight*3,viewHeight / Math.Max(.1f,_depth[x]));
            var top=(viewHeight-wallHeight)/2;
            var hit = _position+dir*distance;
            var fraction=Math.Min(hit.X%1,hit.Y%1);
            var room=((int)hit.X/8+(int)hit.Y/7)%5;
            var window=Arena[Math.Clamp((int)hit.Y,0,Arena.Length-1)][Math.Clamp((int)hit.X,0,Arena[0].Length-1)]=='W';
            var shade=Math.Clamp(1/(1+distance*.11f),.15f,1);
            // Riveted panels, horizontal seams, vents, and luminous control strips.
            for(var band=0;band<24;band++)
            {
                var bandTop=Math.Max(0,top+wallHeight*band/24);
                var bottom=Math.Min(viewHeight,top+wallHeight*(band+1)/24);
                if(bottom<=bandTop)continue;
                var intensity=.25f+room*.055f;
                if(band%6==0 || fraction<.045f)intensity=.10f;
                else if(band is 5 or 18 && fraction>.12f && fraction<.2f)intensity=.65f;
                else if(room==1 && band>8 && band<16 && band%2==0)intensity=.12f;
                else if(room==2 && band==7)intensity=.8f;
                else if(room==3 && band%5==1 && fraction>.3f)intensity=.5f;
                else if(room==4 && band is 10 or 14)intensity=.1f;
                if(window && band>4 && band<19)
                {
                    intensity=(band==11||fraction<.08f)?.09f:.23f;
                    if(band!=11 && MathF.Sin(hit.X*1.7f+hit.Y*.7f+_time*.08f+band*.45f)>.4f)intensity=.45f;
                }
                Rect(x*width/_depth.Length,bandTop,width/_depth.Length+1,bottom-bandTop,
                    new Color(intensity*.17f*shade,intensity*shade,intensity*.17f*shade));
            }
        }
        foreach(var item in _pickups.Where(p=>!p.Taken && p.Room<=_unlockedRoom).OrderByDescending(p=>Vector2.DistanceSquared(p.Position,_position)))
        {
            var offset=item.Position-_position;var bearing=MathF.Atan2(offset.Y,offset.X)-_angle;
            bearing=MathF.Atan2(MathF.Sin(bearing),MathF.Cos(bearing));
            var distance=offset.Length()*MathF.Cos(bearing);
            if(Math.Abs(bearing)>fov || distance<=.15f)continue;
            var size=Math.Min(viewHeight*.7f,viewHeight/distance*.35f);
            var center=width*(.5f+bearing/fov);
            var art=item.Kind switch
            {
                PickupKind.Health=>new[]{".ggggggg.",".gyyyy g.",".gggyggg.",".gyyyyyg.",".gggyggg.",".ggggggg."},
                PickupKind.Armor=>new[]{".ygggggy.","ggkgggkgg","ggggggggg",".ggggggg.",".ggkkkgg.",".ggggggg."},
                PickupKind.Ammo=>new[]{".yyyyyyy.",".gkgkgkg.",".gygygyg.",".gygygyg.",".ggggggg.",".kkkkkkk."},
                _=>item.Weapon switch
                {
                    Weapon.Shotgun=>new[]{".........","yyyyyyyyy","ggggggggg","...gg....","...gg....","........."},
                    Weapon.Launcher=>new[]{"..yyyyyy.","ygggggggy","ygggggggy","..yyyyyy.","...gg....","...kk...."},
                    _=>new[]{"yyyy.yyyy","gggg.gggg","..gg.gg..","..kk.kk..",".........","........."}
                }
            };
            DrawPixels(handle,art,new Vector2(center-size/2,viewHeight/2+size*.7f),size/9,distance,width);
        }
        foreach (var robot in _robots.Where(r=>r.Room<=_unlockedRoom).OrderByDescending(r=>Vector2.DistanceSquared(r.Position,_position)))
        {
            var offset=robot.Position-_position;
            var bearing=MathF.Atan2(offset.Y,offset.X)-_angle;
            bearing=MathF.Atan2(MathF.Sin(bearing),MathF.Cos(bearing));
            if (Math.Abs(bearing)>fov) continue;
            var distance=offset.Length()*MathF.Cos(bearing);
            if(distance<=.1f)continue;
            var size=Math.Min(viewHeight*1.5f,viewHeight/distance)*(robot.Elite?1.2f:1);
            var center=width*(.5f+bearing/fov);
            if(robot.Health<=0)
            {
                var column=Math.Clamp((int)(center/width*_depth.Length),0,_depth.Length-1);
                if(distance<=_depth[column])Rect(center-size*.22f,viewHeight/2+size*.32f,size*.45f,size*.08f,dark);
                continue;
            }
            var sway=MathF.Sin(_time*5+robot.Position.X)*size*.015f;
            var art=robot.Kind switch
            {
                EnemyKind.Scout=>new[]{"...rrr...","..rwwwr..","...kkk...","....r....","..rrrrr..",".r..r..r.","....r....","...r.r...","..r...r..",".kk...kk."},
                EnemyKind.Marksman=>new[]{"..rrrrr.....","..kwwkk.....","...kkk......","..rrrrr.....",".rrryrrrrrrr",".rrryrrrkkkk","..rrrrr.....","..rr.rr.....","..kk.kk....."},
                EnemyKind.Brute=>new[]{"..rrrrrrr...","..kkwwkkk...","...kkkkk....","rrrrrrrrrrrr","rrrgggggrrrr","rrggyggygrrr","rrggyyyggggr","rrrgggggrrrr","kkrrrrrrrkkk","..rrr.rrr...","..rrr.rrr...",".kkkk.kkkk.."},
                EnemyKind.Commander=>new[]{"...yyyyy....","..rrryrrr...","..kwwkwwk...","...kkkkk....",".rrrrrrrrr..","rrryyryyrrr.","rrrryyyrrrr.","kkrrrrrrrkk.","..rrr.rrr...","..kkk.kkk..."},
                _=>Robot
            };
            DrawPixels(handle,art,new Vector2(center-size*.34f+sway,viewHeight/2-size*.47f),size/18,distance,width,robot.Elite);
        }
        // A short dip-and-rise reload; magazines, pump, or missile move with the selected gun.
        var reloadDip=_reload>0?MathF.Sin(MathF.PI*(1-_reload/ReloadDuration)):0;
        var recoil=_shot>.16f?viewHeight*.035f:0;
        var gunY=viewHeight*.76f+recoil+reloadDip*viewHeight*.21f;
        void Pistol(float center)
        {
            var w=width*.105f;var x=center-w/2;
            Rect(x-4,gunY+8,w+8,Math.Max(0,viewHeight-gunY),dark);
            Rect(x,gunY,w,Math.Max(0,viewHeight-gunY),Color.FromHex("#246424"));
            Rect(x+4,gunY+3,w-8,8,green);
            Rect(x+w*.3f,gunY-17,w*.4f,35,Color.FromHex("#113811"));
            Rect(center-2,gunY-20,4,7,bright);
            if(_reload>0)Rect(center-5,gunY+20+reloadDip*20,10,25,green);
            if(_shot>.17f){Rect(center-4,gunY-42,8,25,bright);Rect(center-14,gunY-34,28,9,green);}
        }
        if(_weapon==Weapon.DualPistols){Pistol(width*.35f);Pistol(width*.65f);}
        else if(_weapon==Weapon.Pistol)Pistol(width/2);
        else if(_weapon==Weapon.Shotgun)
        {
            var x=width*.45f;var w=width*.1f;
            Rect(x,gunY-35,w,Math.Max(0,viewHeight-gunY+35),dark);
            Rect(x+3,gunY-35,w*.4f,80,green);Rect(x+w*.55f,gunY-35,w*.35f,80,green);
            Rect(x+2,gunY+12+reloadDip*18,w-4,18,Color.FromHex("#378037"));
            if(_reload>0)Rect(x-12,gunY+18,10,22,bright);
            if(_shot>.4f)Rect(x-10,gunY-48,w+20,18,bright);
        }
        else
        {
            var x=width*.43f;var w=width*.14f;
            Rect(x,gunY-34,w,Math.Max(0,viewHeight-gunY+34),dark);
            Rect(x+4,gunY-34,w-8,70,green);Rect(x+10,gunY-27,w-20,24,black);
            for(var i=0;i<4;i++)Rect(x,gunY+i*12,w,3,bright);
            if(_reload>0)Rect(x+w*.35f,gunY-10+reloadDip*45,w*.3f,50,bright);
            if(_shot>.55f)Rect(x-10,gunY-54,w+20,25,bright);
        }
        Rect(width/2-1,viewHeight/2-4,2,8,green); Rect(width/2-4,viewHeight/2-1,8,2,green);
        // Phosphor scanlines stay subtle enough to keep the scene readable.
        for(var y=1f;y<viewHeight;y+=4)Rect(0,y,width,1,new Color(0,0,0,.13f));

        // Original status-bar layout inspired by classic shooters, in green monochrome.
        var hudHeight=height-viewHeight; var unit=width/320f;
        Rect(0,viewHeight,width,hudHeight,Color.FromHex("#163216"));
        Rect(0,viewHeight,width,2,bright);Rect(0,height-2,width,2,black);
        foreach(var edge in new[]{.17f,.37f,.49f,.62f,.82f})
        {Rect(width*edge,viewHeight+3,2,hudHeight-5,black);Rect(width*edge+2,viewHeight+3,1,hudHeight-5,green);}
        var labelY=viewHeight+hudHeight*.72f;var digitsY=viewHeight+hudHeight*.22f;
        Text(_weapon==Weapon.Pistol?"INF":_rounds[(int)_weapon].ToString("D2"),width*.025f,digitsY,3*unit,bright);Text("AMMO",width*.025f,labelY,unit,green);
        Text(_health+"%",width*.19f,digitsY,3*unit,bright);Text("HEALTH",width*.20f,labelY,unit,green);
        Text("ARMS",width*.39f,viewHeight+7,unit,green);
        for(var i=0;i<4;i++)
            Text((i+1).ToString(),width*.39f+(i%2)*width*.045f,viewHeight+hudHeight*(i<2?.4f:.65f),unit,
                !_owned[i]?dark:(int)_weapon==i?bright:green);
        Rect(width*.505f,viewHeight+5,width*.095f,hudHeight-10,black);
        DrawPixels(handle,Portrait,new Vector2(width*.51f,viewHeight+7),(hudHeight-14)/15,null,width);
        Text(_armor+"%",width*.635f,digitsY,3*unit,bright);Text("ARMOR",width*.65f,labelY,unit,green);
        Text("KILLS",width*.84f,viewHeight+hudHeight*.18f,unit,green);
        Text(_kills+"/"+_robots.Count,width*.84f,viewHeight+hudHeight*.46f,1.8f*unit,bright);
        if(_hurt>0)Rect(0,0,width,viewHeight,new Color(.2f,1,.2f,.12f));
        if(_ended)Rect(0,0,width,viewHeight,new Color(0,0,0,.5f));
    }

    private void DrawPixels(DrawingHandleScreen handle,string[] art,Vector2 origin,float pixel,float? depth,float width,bool elite=false)
    {
        for(var y=0;y<art.Length;y++)for(var x=0;x<art[y].Length;x++)
        {
            // Every sprite uses the same green CRT palette, including insignia and portrait.
            var color=art[y][x] switch
            {
                'r'=>Color.FromHex("#287b28"),'y'=>Color.FromHex("#b6ffb6"),'k'=>Color.FromHex("#020c02"),
                'w'=>Color.FromHex("#d4ffd4"),'g'=>Color.FromHex("#164316"),'s'=>Color.FromHex("#65be65"),
                'b'=>Color.FromHex("#246024"),_=>Color.Transparent
            };
            if(color==Color.Transparent)continue;
            if(elite && art[y][x]=='r')color=Color.FromHex("#95e895");
            var point=origin+new Vector2(x*pixel,y*pixel);
            if(depth.HasValue)
            {
                var column=(int)(point.X/width*_depth.Length);
                if(column<0||column>=_depth.Length||depth.Value>_depth[column])continue;
            }
            handle.DrawRect(UIBox2.FromDimensions(point,new Vector2(pixel+.2f,pixel+.2f)),color);
        }
    }
}
