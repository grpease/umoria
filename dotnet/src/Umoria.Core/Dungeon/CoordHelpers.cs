using Umoria.Core.Model.Generated;

namespace Umoria.Core.Dungeon;

// Port of the coord* free functions from src/dungeon.cpp.
public static class CoordHelpers
{
    public static bool InBounds(Model.Generated.Dungeon dg, Coord coord)
    {
        var y = coord.Y > 0 && coord.Y < dg.Height - 1;
        var x = coord.X > 0 && coord.X < dg.Width - 1;

        return y && x;
    }

    public static int DistanceBetween(Coord from, Coord to)
    {
        var dy = from.Y - to.Y;
        if (dy < 0)
        {
            dy = -dy;
        }

        var dx = from.X - to.X;
        if (dx < 0)
        {
            dx = -dx;
        }

        var a = (dy + dx) << 1;
        var b = dy > dx ? dx : dy;

        return (a - b) >> 1;
    }

    // Checks points north, south, east, and west for a wall. Caller must
    // ensure coord is InBounds (0 < y < dg.Height-1, 0 < x < dg.Width-1).
    public static int WallsNextTo(Model.Generated.Dungeon dg, Coord coord)
    {
        var walls = 0;

        if (dg.Floor[coord.Y - 1, coord.X].FeatureId >= TileConstants.MinCaveWall)
        {
            walls++;
        }

        if (dg.Floor[coord.Y + 1, coord.X].FeatureId >= TileConstants.MinCaveWall)
        {
            walls++;
        }

        if (dg.Floor[coord.Y, coord.X - 1].FeatureId >= TileConstants.MinCaveWall)
        {
            walls++;
        }

        if (dg.Floor[coord.Y, coord.X + 1].FeatureId >= TileConstants.MinCaveWall)
        {
            walls++;
        }

        return walls;
    }

    // Checks all adjacent spots for corridors, failing if a door is
    // already present there. Caller must ensure coord is InBounds.
    public static int CorridorWallsNextTo(Model.Generated.Dungeon dg, TreasureList treasure, Coord coord)
    {
        var walls = 0;

        for (var y = coord.Y - 1; y <= coord.Y + 1; y++)
        {
            for (var x = coord.X - 1; x <= coord.X + 1; x++)
            {
                var tile = dg.Floor[y, x];

                if (tile.FeatureId == TileConstants.TileCorrFloor &&
                    (tile.TreasureId == 0 || treasure[tile.TreasureId].CategoryId < TreasureCategory.TvMinDoors))
                {
                    walls++;
                }
            }
        }

        return walls;
    }
}
