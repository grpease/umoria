using System.Runtime.CompilerServices;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Dungeon;

// What the dungeon-level display call sites draw through (M14: Ui.GameUi).
public interface IDungeonRenderer
{
    // dungeonLiteSpot() -- redraw a tile if it is inside the panel
    void LiteSpot(Coord coord);

    // panelPutTile(caveGetTileSymbol(coord), coord)
    void RedrawTile(Coord coord);

    // panelPutTile(ch, coord)
    void PutTile(char ch, Coord coord);

    // drawDungeonPanel()
    void DrawDungeonPanel();
}

// The bridge from the original's dungeon-level display calls to the UI.
// Until M14 these were documented no-ops (dungeonLiteSpot() is pure
// rendering); each call site was kept, so wiring the renderer was a matter
// of giving them somewhere to go. The light and panel code run below
// PlayerWorld with only the Dungeon in hand, so the renderer is looked up
// by Dungeon object; a dungeon nobody attached (every headless run and
// trace test) draws nothing.
public static class ScreenRendering
{
    private static readonly ConditionalWeakTable<Model.Generated.Dungeon, IDungeonRenderer> Renderers = new();

    public static void Attach(Model.Generated.Dungeon dg, IDungeonRenderer renderer)
    {
        Renderers.AddOrUpdate(dg, renderer);
    }

    public static void Detach(Model.Generated.Dungeon dg)
    {
        Renderers.Remove(dg);
    }

    // src/dungeon.cpp:392 dungeonLiteSpot()
    public static void LiteSpot(Model.Generated.Dungeon dg, Coord coord)
    {
        if (Renderers.TryGetValue(dg, out var renderer))
        {
            renderer.LiteSpot(coord);
        }
    }

    // panelPutTile(caveGetTileSymbol(coord), coord) -- the light code's
    // redraws, which (unlike dungeonLiteSpot()) don't check the panel.
    public static void RedrawTile(Model.Generated.Dungeon dg, Coord coord)
    {
        if (Renderers.TryGetValue(dg, out var renderer))
        {
            renderer.RedrawTile(coord);
        }
    }

    // panelPutTile(ch, coord)
    public static void PutTile(Model.Generated.Dungeon dg, char ch, Coord coord)
    {
        if (Renderers.TryGetValue(dg, out var renderer))
        {
            renderer.PutTile(ch, coord);
        }
    }

    // src/ui.cpp:82 drawDungeonPanel()
    public static void DrawDungeonPanel(Model.Generated.Dungeon dg)
    {
        if (Renderers.TryGetValue(dg, out var renderer))
        {
            renderer.DrawDungeonPanel();
        }
    }

    // The display half of printMessage() for callers with no PlayerWorld
    // (M6's character creation messages). The real message line needs the
    // world (Character.PlayerCore.PrintMessage -> GameUi.PrintMessage);
    // creation runs before there is a screen, so this stays a no-op.
    public static void PrintMessage(string message)
    {
    }
}
