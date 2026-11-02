using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
namespace Content.Client._Misfits.Holotape;
/// <summary>Original pixel glyphs inside the reused Pip-Boy app tiles.</summary>
public sealed class ComputerAppIcon(string app) : Control
{
    protected override void Draw(DrawingHandleScreen handle)
    {
        var glyph=app switch
        {
            "command"=>new[]{"........",".g......","..g.....",".g......","....ggg.","........","........","........"},
            "notes"=>new[]{".gggggg.",".g....g.",".g.gggg.",".g....g.",".g.gggg.",".g....g.",".gggggg.","........"},
            "database"=>new[]{"..gggg..",".g....g.",".gggggg.",".g....g.",".gggggg.",".g....g.","..gggg..","........"},
            "lock"=>new[]{"..gggg..",".gg..gg.",".g....g.","gggggggg","ggg..ggg","ggg..ggg","gggggggg","........"},
            "map"=>new[]{"ggg.gggg","g.g.g..g","g.gggg.g","g....g.g","gg.ggggg",".g.g...g","gggggggg","........"},
            "settings"=>new[]{"..gggg..",".gg..gg.","gg.gg.gg","g.g..g.g","g.g..g.g","gg.gg.gg",".gg..gg.","..gggg.."},
            "messenger" or "public"=>new[]{"gggggggg","g......g","g.gggg.g","g......g","gggggggg","..gg....",".gg.....","........"},
            "vault carnage"=>new[]{"........","..ggggg.","..g..gg.","..gggg..","...gg...","...gg...","..ggg...","........"},
            "atomic defense"=>new[]{"...g....","..ggg...","..ggg...","..ggg...",".ggggg..","..ggg...",".g.g.g..","........"},
            "zetan siege"=>new[]{"..g..g..",".gggggg.","gg.gg.gg","gggggggg","g.gggg.g","..g..g..",".g....g.","........"},
            "robot revolt"=>new[]{"..gggg..",".gg..gg.",".gggggg.","...gg...","gggggggg","g.gggg.g","..g..g..",".gg..gg."},
            "wasteland quest"=>new[]{".gggggg.",".g.g..g.",".g.gggg.",".g.g..g.",".g.gggg.",".g.g..g.",".gggggg.","........"},
            _=>new[]{"...gg...","...gg...","..gggg..",".g.gg.g.","...gg...","..g..g..",".gg..gg.","gggggggg"}
        };
        var scale=Size/8;
        for(var y=0;y<8;y++)for(var x=0;x<8;x++)if(glyph[y][x]=='g')
            handle.DrawRect(UIBox2.FromDimensions(new Vector2(x*scale.X,y*scale.Y),scale),Color.FromHex("#71e871"));
    }
}
