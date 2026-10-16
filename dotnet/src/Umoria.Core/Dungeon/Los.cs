using Umoria.Core.Model.Generated;

namespace Umoria.Core.Dungeon;

// Port of src/dungeon_los.cpp's los() -- Joseph Hall's fast integer-based
// line-of-sight check. Pure function of dg.Floor's FeatureId grid, exactly
// as migration-plan.md's M5 "Verify" section assumed.
//
// look()/lookRay()/lookSee() (same source file) turned out NOT to fit that
// description, despite being listed alongside los() in M5's scope: they're
// an interactive player command, not pure geometry -- lookSee() prompts for
// keypresses, prints messages, and reads Player.pos/flags.blind/flags.image
// (no Player singleton exists until M6), monster.lit and creature sprites
// (display, not placement), and the treasure list's item descriptions. None
// of that is testable or callable yet, so look() is deferred to whichever
// milestone builds player commands/the terminal UI -- see migration-plan.md.
public static class Los
{
    // src/dungeon_los.cpp:25 los()
    //
    // Because this uses plain ints throughout, overflow could occur if
    // deltaX/deltaY exceed 90 -- same caveat as the original.
    public static bool Check(Model.Generated.Dungeon dg, Coord from, Coord to)
    {
        var deltaX = to.X - from.X;
        var deltaY = to.Y - from.Y;

        // Adjacent?
        if (deltaX < 2 && deltaX > -2 && deltaY < 2 && deltaY > -2)
        {
            return true;
        }

        // Handle the cases where deltaX or deltaY == 0.
        if (deltaX == 0)
        {
            if (deltaY < 0)
            {
                (from.Y, to.Y) = (to.Y, from.Y);
            }

            for (var y = from.Y + 1; y < to.Y; y++)
            {
                if (dg.Floor[y, from.X].FeatureId >= TileConstants.MinClosedSpace)
                {
                    return false;
                }
            }

            return true;
        }

        if (deltaY == 0)
        {
            if (deltaX < 0)
            {
                (from.X, to.X) = (to.X, from.X);
            }

            for (var x = from.X + 1; x < to.X; x++)
            {
                if (dg.Floor[from.Y, x].FeatureId >= TileConstants.MinClosedSpace)
                {
                    return false;
                }
            }

            return true;
        }

        // Now, we've eliminated all the degenerate cases. In the
        // computations below, dy (or dx) and slope are multiplied by a
        // scale factor, scale = abs(deltaX * deltaY * 2), so that we can
        // use integer arithmetic.
        var deltaMultiply = deltaX * deltaY;
        var scaleHalf = Math.Abs(deltaMultiply);
        var scale = scaleHalf << 1;
        var xSign = deltaX < 0 ? -1 : 1;
        var ySign = deltaY < 0 ? -1 : 1;

        var absDeltaX = Math.Abs(deltaX);
        var absDeltaY = Math.Abs(deltaY);

        int xx, yy, slope;

        // Travel from one end of the line to the other, oriented along the
        // longer axis.
        if (absDeltaX >= absDeltaY)
        {
            // We start at the border between the first and second tiles,
            // where the y offset = .5 * slope. Remember the scale factor.
            // We have: slope = deltaY / deltaX * 2 * (deltaY * deltaX) = 2 * deltaY * deltaY.
            var dy = deltaY * deltaY;
            slope = dy << 1;
            xx = from.X + xSign;

            // Consider the special case where slope == 1.
            if (dy == scaleHalf)
            {
                yy = from.Y + ySign;
                dy -= scale;
            }
            else
            {
                yy = from.Y;
            }

            while (to.X - xx != 0)
            {
                if (dg.Floor[yy, xx].FeatureId >= TileConstants.MinClosedSpace)
                {
                    return false;
                }

                dy += slope;

                if (dy < scaleHalf)
                {
                    xx += xSign;
                }
                else if (dy > scaleHalf)
                {
                    yy += ySign;
                    if (dg.Floor[yy, xx].FeatureId >= TileConstants.MinClosedSpace)
                    {
                        return false;
                    }

                    xx += xSign;
                    dy -= scale;
                }
                else
                {
                    // This is the case, dy == scaleHalf, where the LOS
                    // exactly meets the corner of a tile.
                    xx += xSign;
                    yy += ySign;
                    dy -= scale;
                }
            }

            return true;
        }

        var dx = deltaX * deltaX;
        slope = dx << 1;

        yy = from.Y + ySign;

        if (dx == scaleHalf)
        {
            xx = from.X + xSign;
            dx -= scale;
        }
        else
        {
            xx = from.X;
        }

        while (to.Y - yy != 0)
        {
            if (dg.Floor[yy, xx].FeatureId >= TileConstants.MinClosedSpace)
            {
                return false;
            }

            dx += slope;

            if (dx < scaleHalf)
            {
                yy += ySign;
            }
            else if (dx > scaleHalf)
            {
                xx += xSign;
                if (dg.Floor[yy, xx].FeatureId >= TileConstants.MinClosedSpace)
                {
                    return false;
                }

                yy += ySign;
                dx -= scale;
            }
            else
            {
                xx += xSign;
                yy += ySign;
                dx -= scale;
            }
        }

        return true;
    }
}
