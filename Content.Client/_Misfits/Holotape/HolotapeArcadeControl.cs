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

/// <summary>Original compact arcade games sharing input, drawing, and transient session lifecycle.</summary>
public sealed class HolotapeArcadeControl(string program) : Control, IHolotapeGame
{
    public event Action? OnQuit;
    public event Action<string>? OnStatus;
    private sealed class Piece(Vector2 position, Vector2 velocity, int hp=1)
    {
        public Vector2 Position=position;
        public Vector2 Velocity=velocity;
        public Vector2 Target;
        public int Hp=hp;
        public int Level;
        public float Life;
    }
    private readonly Random _random=new();
    private readonly HashSet<BoundKeyFunction> _held=new();
    private readonly List<Piece> _foes=new();
    private readonly List<Piece> _bullets=new();
    private readonly List<Piece> _enemyBullets=new();
    private readonly List<Piece> _bursts=new();
    private readonly List<Vector2> _loot=new();
    private readonly bool[] _cities=new bool[6];
    private Vector2 _player;
    private Vector2 _aim;
    private float _vy,_clock,_spawn,_cooldown,_invulnerable,_statusTime;
    private int _score,_hp,_lives,_round;
    private int _direction=1;
    private bool _ended,_won;
    private static readonly (float X,float Top,float Bottom)[] Ladders={(280,125,175),(40,75,125),(280,25,75)};

    public void Restart()
    {
        CanKeyboardFocus=true;KeyboardFocusOnClick=true;MouseFilter=MouseFilterMode.Stop;RectClipContent=true;
        _held.Clear();_foes.Clear();_bullets.Clear();_enemyBullets.Clear();_bursts.Clear();_loot.Clear();
        _clock=_vy=_cooldown=_invulnerable=_statusTime=0;_spawn=.5f;_hp=100;_lives=3;_round=1;_score=0;_direction=1;_ended=_won=false;
        _player=new Vector2(160,175);_aim=new Vector2(160,75);
        Array.Fill(_cities,true);
        switch(program)
        {
            case "zetan siege": SpawnAliens();break;
            case "robot revolt": _player=new Vector2(160,100);SpawnRobots();break;
            case "vault runner": _player=new Vector2(20,170);for(var i=1;i<17;i++)_loot.Add(new Vector2(i*90,142));break;
            case "red menace": _player=new Vector2(25,175);break;
        }
        Status();
    }

    private string Title=>program.ToUpperInvariant();
    private void Status()
    {
        var detail=program switch
        {
            "atomic defense"=>$"SETTLEMENTS {_cities.Count(c=>c)}/6 | SURVIVE {Math.Max(0,60-(int)_clock)}s | Arrows/WASD aim, click or Space intercepts",
            "zetan siege"=>$"WAVE {_round}/3 | LIVES {_lives} | Left/right move, Space fires",
            "robot revolt"=>$"WAVE {_round}/5 | HEALTH {_hp} | Move with WASD/arrows, click aims/fires, Space fires toward cursor",
            "vault runner"=>$"LIVES {_lives} | Left/right move, Space jumps; reach the bunker at the far right",
            _=>$"LIVES {_lives} | Left/right move, Up/down climbs, Space jumps; rescue the worker at the top left"
        };
        OnStatus?.Invoke(_ended?$"{Title} - {(_won?"VICTORY":"GAME OVER")} | SCORE {_score} | Space restarts, Escape quits":$"{Title} | SCORE {_score} | {detail}");
    }
    private void End(bool won){_ended=true;_won=won;_held.Clear();Status();}
    private static bool Left(BoundKeyFunction key)=>key==ContentKeyFunctions.ArcadeLeft||key==EngineKeyFunctions.MoveLeft;
    private static bool Right(BoundKeyFunction key)=>key==ContentKeyFunctions.ArcadeRight||key==EngineKeyFunctions.MoveRight;
    private static bool Up(BoundKeyFunction key)=>key==ContentKeyFunctions.ArcadeUp||key==EngineKeyFunctions.MoveUp;
    private static bool Down(BoundKeyFunction key)=>key==ContentKeyFunctions.ArcadeDown||key==EngineKeyFunctions.MoveDown;
    private int XInput=>(_held.Any(Right)?1:0)-(_held.Any(Left)?1:0);
    private int YInput=>(_held.Any(Down)?1:0)-(_held.Any(Up)?1:0);

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if(args.Function==EngineKeyFunctions.CloseModals||args.Function==EngineKeyFunctions.TextReleaseFocus){args.Handle();OnQuit?.Invoke();return;}
        if(Left(args.Function)||Right(args.Function)||Up(args.Function)||Down(args.Function))
        {
            _held.Add(args.Function);args.Handle();
            return;
        }
        if(args.Function==EngineKeyFunctions.UIClick||args.Function==ContentKeyFunctions.Arcade1)
        {
            GrabKeyboardFocus();
            if(args.Function==EngineKeyFunctions.UIClick)_aim=ArenaPoint(args.RelativePixelPosition,PixelSize);
            else _held.Add(args.Function);
            Action();args.Handle();return;
        }
        base.KeyBindDown(args);
    }
    protected override void KeyBindUp(GUIBoundKeyEventArgs args){_held.Remove(args.Function);args.Handle();}
    protected override void KeyboardFocusExited(){_held.Clear();base.KeyboardFocusExited();}

    private void Action()
    {
        if(_ended){Restart();return;}
        if(_cooldown>0)return;
        switch(program)
        {
            case "atomic defense":
                _aim=Vector2.Clamp(_aim,new Vector2(5,10),new Vector2(315,160));
                var missile=new Piece(new Vector2(160,183),Vector2.Normalize(_aim-new Vector2(160,183))*155){Target=_aim};_bullets.Add(missile);_cooldown=.18f;break;
            case "zetan siege":_bullets.Add(new Piece(_player-new Vector2(0,8),new Vector2(0,-150)));_cooldown=.2f;break;
            case "robot revolt":
                var delta=_aim-_player;if(delta.LengthSquared()<1)delta=new Vector2(0,-1);
                _bullets.Add(new Piece(_player,Vector2.Normalize(delta)*180));_cooldown=.15f;break;
            default:
                var floors=program=="red menace"?new[]{25f,75f,125f,175f}:new[]{170f};
                if(floors.Any(f=>Math.Abs(_player.Y-f)<1)){_vy=-170;_cooldown=.15f;}break;
        }
        Status();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if(!VisibleInTree||!HasKeyboardFocus()||_ended)return;
        var dt=Math.Min(args.DeltaSeconds,.04f);_clock+=dt;_cooldown=Math.Max(0,_cooldown-dt);_invulnerable=Math.Max(0,_invulnerable-dt);
        if(_held.Contains(ContentKeyFunctions.Arcade1)&&program is "atomic defense" or "zetan siege" or "robot revolt")Action();
        switch(program)
        {
            case "atomic defense": TickDefense(dt);break;
            case "zetan siege": TickAliens(dt);break;
            case "robot revolt": TickRobots(dt);break;
            case "vault runner": TickPlatforms(dt,false);break;
            case "red menace": TickPlatforms(dt,true);break;
        }
        _statusTime-=dt;if(_statusTime<=0){_statusTime=.2f;Status();}
    }

    private void TickDefense(float dt)
    {
        _aim=Vector2.Clamp(_aim+new Vector2(XInput,YInput)*75*dt,new Vector2(5,10),new Vector2(315,160));
        _spawn-=dt;
        if(_spawn<=0)
        {
            _spawn=Math.Max(.35f,1.15f-_clock*.009f);
            var targets=Enumerable.Range(0,6).Where(i=>_cities[i]).ToArray();if(targets.Length==0){End(false);return;}
            var target=targets[_random.Next(targets.Length)];var start=new Vector2(_random.Next(5,315),0);
            _foes.Add(new Piece(start,Vector2.Normalize(new Vector2(30+target*52,180)-start)*(22+_clock*.25f)){Level=target});
        }
        foreach(var shot in _bullets.ToArray())
        {
            var distance=Vector2.Distance(shot.Position,shot.Target);
            if(distance<=shot.Velocity.Length()*dt){_bullets.Remove(shot);_bursts.Add(new Piece(shot.Target,Vector2.Zero){Life=.9f});}
            else shot.Position+=shot.Velocity*dt;
        }
        foreach(var burst in _bursts.ToArray()){burst.Life-=dt;if(burst.Life<=0)_bursts.Remove(burst);}
        foreach(var foe in _foes.ToArray())
        {
            foe.Position+=foe.Velocity*dt;
            if(_bursts.Any(b=>Vector2.Distance(b.Position,foe.Position)<25*MathF.Sin(b.Life/.9f*MathF.PI))){_foes.Remove(foe);_score+=10;continue;}
            if(foe.Position.Y>=177){_cities[foe.Level]=false;_foes.Remove(foe);}
        }
        if(_cities.All(c=>!c))End(false);else if(_clock>=60)End(true);
    }

    private void SpawnAliens()
    {
        _foes.Clear();_enemyBullets.Clear();
        for(var y=0;y<4;y++)for(var x=0;x<8;x++)_foes.Add(new Piece(new Vector2(30+x*30,25+y*16),Vector2.Zero));
    }
    private void TickAliens(float dt)
    {
        _player.X=Math.Clamp(_player.X+XInput*95*dt,8,312);
        var bounce=_foes.Any(f=>f.Position.X<10||f.Position.X>310);
        if(bounce){_direction=-_direction;foreach(var foe in _foes)foe.Position.Y+=8;}
        foreach(var foe in _foes)foe.Position.X+=_direction*(12+_round*8)*dt;
        _spawn-=dt;if(_spawn<=0&&_foes.Count>0){_spawn=.9f/_round;var from=_foes[_random.Next(_foes.Count)].Position;_enemyBullets.Add(new Piece(from,new Vector2(0,65+_round*15)));}
        TickBullets(dt,false);
        foreach(var shot in _enemyBullets.ToArray())
        {
            shot.Position+=shot.Velocity*dt;
            if(Vector2.Distance(shot.Position,_player)<8){Hurt(1,true);_enemyBullets.Remove(shot);}else if(shot.Position.Y>200)_enemyBullets.Remove(shot);
        }
        if(_foes.Any(f=>f.Position.Y>160))End(false);
        if(_foes.Count==0){if(++_round>3)End(true);else SpawnAliens();}
    }

    private void SpawnRobots()
    {
        for(var i=0;i<4+_round*2;i++)
        {
            var position=i%2==0?new Vector2(_random.Next(10,310),i%4==0?8:192):new Vector2(i%4==1?8:312,_random.Next(10,190));
            _foes.Add(new Piece(position,Vector2.Zero,1+_round/2));
        }
    }
    private void TickRobots(float dt)
    {
        var movement=new Vector2(XInput,YInput);if(movement.LengthSquared()>1)movement=Vector2.Normalize(movement);
        _player=Vector2.Clamp(_player+movement*80*dt,new Vector2(8,12),new Vector2(312,190));
        foreach(var foe in _foes)
        {
            var delta=_player-foe.Position;if(delta.LengthSquared()>1)foe.Position+=Vector2.Normalize(delta)*(17+_round*3)*dt;
            if(Vector2.Distance(foe.Position,_player)<9)Hurt(10,false);
        }
        TickBullets(dt,true);
        if(_foes.Count==0){if(++_round>5)End(true);else{_hp=Math.Min(100,_hp+15);SpawnRobots();}}
    }
    private void TickBullets(float dt,bool arena)
    {
        foreach(var shot in _bullets.ToArray())
        {
            shot.Position+=shot.Velocity*dt;
            var target=_foes.FirstOrDefault(f=>Vector2.Distance(f.Position,shot.Position)<(arena?8:9));
            if(target!=null){_bullets.Remove(shot);if(--target.Hp<=0){_foes.Remove(target);_score+=10;}}
            else if(shot.Position.X<0||shot.Position.X>320||shot.Position.Y<0||shot.Position.Y>200)_bullets.Remove(shot);
        }
    }
    private void Hurt(int damage,bool life)
    {
        if(_invulnerable>0)return;_invulnerable=1;
        if(life){if(--_lives<=0)End(false);}else{_hp=Math.Max(0,_hp-damage);if(_hp==0)End(false);}
    }

    private static bool Pit(float x)=>Enumerable.Range(0,5).Any(i=>x>120+i*290&&x<175+i*290);
    private void TickPlatforms(float dt,bool barrels)
    {
        var previous=_player.Y;
        _player.X=Math.Clamp(_player.X+XInput*85*dt,8,barrels?312:1590);
        var ladder=barrels&&Ladders.Any(l=>Math.Abs(_player.X-l.X)<9&&_player.Y>=l.Top-2&&_player.Y<=l.Bottom+2);
        if(ladder&&YInput!=0){_vy=0;_player.Y+=YInput*60*dt;}
        else
        {
            _vy+=430*dt;_player.Y+=_vy*dt;
            foreach(var floor in barrels?new[]{25f,75f,125f,175f}:new[]{170f})
                if(_vy>=0&&previous<=floor+.1f&&_player.Y>=floor&&(barrels||!Pit(_player.X))){_player.Y=floor;_vy=0;break;}
        }
        if(barrels)
        {
            _spawn-=dt;if(_spawn<=0){_spawn=1.7f;_foes.Add(new Piece(new Vector2(28,21),new Vector2(55,0)));}
            foreach(var foe in _foes.ToArray())
            {
                foe.Position+=foe.Velocity*dt;
                if(foe.Position.X<8||foe.Position.X>312){foe.Position.X=Math.Clamp(foe.Position.X,8,312);foe.Velocity.X=-foe.Velocity.X;foe.Position.Y+=50;}
                if(foe.Position.Y>180)_foes.Remove(foe);
                else if(Vector2.Distance(foe.Position,_player-new Vector2(0,5))<10)PlatformHit(true);
            }
            if(_player.Y<=25.5f&&_player.X<50){_score+=500;End(true);}
        }
        else
        {
            foreach(var treasure in _loot.ToArray())if(Vector2.Distance(treasure,_player-new Vector2(0,6))<12){_loot.Remove(treasure);_score+=25;}
            if(Enumerable.Range(0,5).Any(i=>Math.Abs(_player.X-(220+i*290))<8)&&_player.Y>157)PlatformHit(false);
            if(_player.X>1550){_score+=500;End(true);}
        }
        if(_player.Y>220)PlatformHit(barrels);
    }
    private void PlatformHit(bool barrels)
    {
        if(_invulnerable>0)return;
        Hurt(1,true);_player=new Vector2(barrels?25:20,barrels?175:170);_vy=0;
    }

    private static Vector2 ArenaPoint(Vector2 point, Vector2 pixels) =>
        new(point.X / Math.Max(1, pixels.X) * 320, point.Y / Math.Max(1, pixels.Y) * 200);

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);var sx=PixelSize.X/320f;var sy=PixelSize.Y/200f;
        var green=Color.FromHex("#4acf4a");var light=Color.FromHex("#afffaf");var dim=Color.FromHex("#163916");var black=Color.FromHex("#010701");
        void Box(float x,float y,float w,float h,Color c)=>handle.DrawRect(UIBox2.FromDimensions(new Vector2(x*sx,y*sy),new Vector2(w*sx,h*sy)),c);
        void Line(Vector2 a,Vector2 b,Color c)=>handle.DrawLine(a*new Vector2(sx,sy),b*new Vector2(sx,sy),c);
        void Actor(Vector2 pos,bool alien=false)
        {
            Box(pos.X-6,pos.Y-5,12,9,green);Box(pos.X-3,pos.Y-8,6,4,light);Box(pos.X-4,pos.Y-2,2,2,black);Box(pos.X+2,pos.Y-2,2,2,black);
            Box(pos.X-7,pos.Y+3,3,3,light);Box(pos.X+4,pos.Y+3,3,3,light);
            if(alien){Box(pos.X-5,pos.Y-7,2,3,light);Box(pos.X+3,pos.Y-7,2,3,light);}
        }
        void Hero(Vector2 pos)
        {
            if(_invulnerable>0&&(int)(_clock*12)%2==0)return;
            Box(pos.X-3,pos.Y-11,6,5,light);Box(pos.X-4,pos.Y-6,8,7,green);Box(pos.X-1,pos.Y-6,2,6,light);Box(pos.X-3,pos.Y+1,2,3,light);Box(pos.X+1,pos.Y+1,2,3,light);
        }
        Box(0,0,320,200,black);
        if(program is "vault runner" or "red menace")
        {
            var barrels=program=="red menace";var camera=barrels?0:Math.Clamp(_player.X-80,0,1280);
            if(barrels)
            {
                foreach(var floor in new[]{25,75,125,175}){Box(0,floor+3,320,3,green);for(var x=0;x<320;x+=20)Box(x,floor+7,6,4,dim);}
                foreach(var ladder in Ladders){Box(ladder.X-5,ladder.Top,2,ladder.Bottom-ladder.Top,green);Box(ladder.X+4,ladder.Top,2,ladder.Bottom-ladder.Top,green);for(var y=ladder.Top;y<ladder.Bottom;y+=6)Box(ladder.X-4,y,9,1,light);}
                Actor(new Vector2(28,13),true);Box(20,4,17,4,light);
                foreach(var foe in _foes){Box(foe.Position.X-5,foe.Position.Y-5,10,10,green);Box(foe.Position.X-3,foe.Position.Y-3,6,6,black);Line(foe.Position-new Vector2(4,0),foe.Position+new Vector2(4,0),light);}
            }
            else
            {
                for(var x=0;x<1600;x+=5)if(!Pit(x))Box(x-camera,173,5,27,dim);
                for(var i=0;i<5;i++){Box(216+i*290-camera,160,9,13,light);Box(214+i*290-camera,168,13,5,green);}
                foreach(var item in _loot){Box(item.X-camera-4,item.Y-4,8,8,light);Box(item.X-camera-2,item.Y-2,4,4,dim);}
                Box(1550-camera,125,35,48,dim);Box(1560-camera,135,16,38,green);
            }
            Hero(_player-new Vector2(camera,0));
        }
        else if(program=="atomic defense")
        {
            for(var i=0;i<6;i++)if(_cities[i]){Box(20+i*52,168,21,20,green);Box(25+i*52,158,8,10,dim);for(var y=172;y<184;y+=4)Box(23+i*52,y,15,1,black);}
            Box(148,182,24,8,light);
            foreach(var enemy in _foes){Line(enemy.Position-enemy.Velocity*.25f,enemy.Position,green);Box(enemy.Position.X-1,enemy.Position.Y-1,3,3,light);}
            foreach(var bullet in _bullets)Line(bullet.Position-bullet.Velocity*.07f,bullet.Position,light);
            foreach(var burst in _bursts){var radius=25*MathF.Sin(burst.Life/.9f*MathF.PI);for(var a=0;a<20;a++){var theta=a*MathF.Tau/20;var next=(a+1)*MathF.Tau/20;Line(burst.Position+new Vector2(MathF.Cos(theta),MathF.Sin(theta))*radius,burst.Position+new Vector2(MathF.Cos(next),MathF.Sin(next))*radius,light);}}
            Line(_aim-new Vector2(5,0),_aim+new Vector2(5,0),light);Line(_aim-new Vector2(0,5),_aim+new Vector2(0,5),light);
        }
        else
        {
            foreach(var enemy in _foes)Actor(enemy.Position,true);
            foreach(var bullet in _bullets)Box(bullet.Position.X-1,bullet.Position.Y-3,2,6,light);
            foreach(var bullet in _enemyBullets)Box(bullet.Position.X-1,bullet.Position.Y-2,3,5,green);
            if(program=="zetan siege"){Box(_player.X-8,_player.Y-4,16,8,green);Box(_player.X-2,_player.Y-10,4,8,light);}
            else Hero(_player);
        }
        for(var y=0;y<200;y+=4)Box(0,y,320,1,new Color(0,0,0,.12f));
        if(_ended)Box(0,0,320,200,new Color(0,0,0,.45f));
    }
}
