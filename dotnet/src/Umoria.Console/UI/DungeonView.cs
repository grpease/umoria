using System.Text;
using Terminal.Gui;
using Umoria.Core.Model.Generated;
using Umoria.Core.Movement;

namespace Umoria.Console.UI;

// Minimal fullscreen view: renders a viewport onto the generated dungeon
// floor and moves the player on direction keys. This is a reimplementation
// against Terminal.Gui, not a port -- curses' screen-region model doesn't
// map 1:1 -- but the movement rules (which tiles are walkable) mirror the
// ported DungeonGenerator's own tile semantics.
public sealed class DungeonView : View
{
    private readonly Dungeon _dg;
    private Coord _player;

    public DungeonView(Dungeon dg, Coord playerStart)
    {
        _dg = dg;
        _player = playerStart;

        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var viewport = Viewport;
        var rows = viewport.Height;
        var cols = viewport.Width;

        // Center the viewport on the player, clamped to the map edges --
        // a simplified stand-in for the original's scrolling Panel_t.
        var top = Math.Clamp(_player.Y - (rows / 2), 0, Math.Max(0, _dg.Height - rows));
        var left = Math.Clamp(_player.X - (cols / 2), 0, Math.Max(0, _dg.Width - cols));

        for (var row = 0; row < rows; row++)
        {
            var mapY = top + row;
            Move(0, row);

            for (var col = 0; col < cols; col++)
            {
                var mapX = left + col;

                if (mapY == _player.Y && mapX == _player.X)
                {
                    AddRune(new Rune('@'));
                    continue;
                }

                if (mapY < 0 || mapY >= _dg.Height || mapX < 0 || mapX >= _dg.Width)
                {
                    AddRune(new Rune(' '));
                    continue;
                }

                AddRune(new Rune(GlyphFor(_dg.Floor[mapY, mapX].FeatureId)));
            }
        }

        return true;
    }

    private static char GlyphFor(byte featureId)
    {
        return featureId switch
        {
            TileConstants.TileNullWall => ' ',
            TileConstants.TileDarkFloor or TileConstants.TileLightFloor => '.',
            TileConstants.TileCorrFloor => '#',
            TileConstants.TileBlockedFloor => '+', // door/rubble, lumped together for this minimal view
            TileConstants.Tmp1Wall or TileConstants.Tmp2Wall => '%',
            TileConstants.TileGraniteWall => '%',
            TileConstants.TileMagmaWall => '%',
            TileConstants.TileQuartzWall => '%',
            TileConstants.TileBoundaryWall => '%',
            _ => '?',
        };
    }

    protected override bool OnKeyDown(Key key)
    {
        var direction = InputMap.DirectionFor(key);
        if (direction is null)
        {
            return base.OnKeyDown(key);
        }

        TryMove(direction.Value);
        SetNeedsDraw();
        return true;
    }

    private void TryMove(int direction)
    {
        var (deltaY, deltaX) = DirectionOffsets.For(direction);
        var target = new Coord(_player.Y + deltaY, _player.X + deltaX);

        if (target.Y <= 0 || target.Y >= _dg.Height - 1 || target.X <= 0 || target.X >= _dg.Width - 1)
        {
            return;
        }

        // Walkable: open floor/corridor, or a door (TileBlockedFloor) --
        // real door-opening/bashing logic is deferred along with the rest
        // of the player-action subsystem.
        var featureId = _dg.Floor[target.Y, target.X].FeatureId;
        if (featureId > TileConstants.MaxCaveFloor)
        {
            return;
        }

        _player = target;
    }
}
