using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Spells;

// M10: src/spells.cpp's detection and area-light group -- treasure, object,
// trap, secret door, invisible/evil/all monster detection, light and darken
// area, and magic mapping. None of these damage anything; they mark tiles
// (field_mark/permanent_light), light monsters for one redraw, or rewrite
// dark-room floor.
//
// panelPutTile() (the detected monster's glyph), drawDungeonPanel() and
// printMessage(CNIL) (a message-line flush) are display (M14: through
// ScreenRendering / w.Ui); each detect spell's updateMonsters(false) --
// "must unlight every monster just lighted" -- is the state half. The
// detected glyphs stay on screen until the message is flushed (its -more-
// prompt), which is the only moment the player sees them.
public static class DetectionSpells
{
    // src/spells.cpp:158 spellDetectTreasureWithinVicinity() -- detect any
    // treasure on the current panel -RAK-
    public static bool DetectTreasureWithinVicinity(PlayerWorld w)
    {
        return MarkObjectsOnPanel(w, category => category == TreasureCategory.TvGold);
    }

    // src/spells.cpp:179 spellDetectObjectsWithinVicinity() -- detect all
    // objects on the current panel -RAK-
    public static bool DetectObjectsWithinVicinity(PlayerWorld w)
    {
        return MarkObjectsOnPanel(w, category => category < TreasureCategory.TvMaxObject);
    }

    private static bool MarkObjectsOnPanel(PlayerWorld w, Func<byte, bool> matches)
    {
        var dg = w.Dg;
        var detected = false;

        for (var y = dg.Panel.Top; y <= dg.Panel.Bottom; y++)
        {
            for (var x = dg.Panel.Left; x <= dg.Panel.Right; x++)
            {
                var coord = new Coord(y, x);
                ref var tile = ref dg.Floor[y, x];

                if (tile.TreasureId != 0 && matches(w.Treasure[tile.TreasureId].CategoryId) && !DungeonLighting.CaveTileVisible(dg, coord))
                {
                    tile.FieldMark = true;
                    ScreenRendering.LiteSpot(dg, coord);
                    detected = true;
                }
            }
        }

        return detected;
    }

    // src/spells.cpp:200 spellDetectTrapsWithinVicinity() -- locates and
    // displays traps on current panel -RAK-
    public static bool DetectTrapsWithinVicinity(PlayerWorld w)
    {
        var dg = w.Dg;
        var detected = false;

        for (var y = dg.Panel.Top; y <= dg.Panel.Bottom; y++)
        {
            for (var x = dg.Panel.Left; x <= dg.Panel.Right; x++)
            {
                ref var tile = ref dg.Floor[y, x];

                if (tile.TreasureId == 0)
                {
                    continue;
                }

                if (w.Treasure[tile.TreasureId].CategoryId == TreasureCategory.TvInvisTrap)
                {
                    tile.FieldMark = true;
                    w.Objects.TrapChangeVisibility(new Coord(y, x));
                    detected = true;
                }
                else if (w.Treasure[tile.TreasureId].CategoryId == TreasureCategory.TvChest)
                {
                    w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref w.Treasure[tile.TreasureId]);
                }
            }
        }

        return detected;
    }

    // src/spells.cpp:228 spellDetectSecretDoorssWithinVicinity() -- locates
    // and displays all secret doors on current panel -RAK-
    public static bool DetectSecretDoorsWithinVicinity(PlayerWorld w)
    {
        var dg = w.Dg;
        var detected = false;

        for (var y = dg.Panel.Top; y <= dg.Panel.Bottom; y++)
        {
            for (var x = dg.Panel.Left; x <= dg.Panel.Right; x++)
            {
                var coord = new Coord(y, x);
                ref var tile = ref dg.Floor[y, x];

                if (tile.TreasureId == 0)
                {
                    continue;
                }

                var category = w.Treasure[tile.TreasureId].CategoryId;

                if (category == TreasureCategory.TvSecretDoor)
                {
                    // Secret doors

                    tile.FieldMark = true;
                    w.Objects.TrapChangeVisibility(coord);
                    detected = true;
                }
                else if ((category == TreasureCategory.TvUpStair || category == TreasureCategory.TvDownStair) && !tile.FieldMark)
                {
                    // Staircases

                    tile.FieldMark = true;
                    ScreenRendering.LiteSpot(dg, coord);
                    detected = true;
                }
            }
        }

        return detected;
    }

    // src/spells.cpp:261 spellDetectInvisibleCreaturesWithinVicinity() --
    // locates and displays all invisible creatures on current panel -RAK-
    public static bool DetectInvisibleCreaturesWithinVicinity(PlayerWorld w)
    {
        return LightMonstersOnPanel(w,
            creature => (creature.Movement & MonsterConfig.Move.CmInvisible) != 0,
            "You sense the presence of invisible creatures!");
    }

    // src/spells.cpp:560 spellDetectMonsters() -- display all creatures on
    // the current panel -RAK-
    public static bool DetectMonsters(PlayerWorld w)
    {
        return LightMonstersOnPanel(w,
            creature => (creature.Movement & MonsterConfig.Move.CmInvisible) == 0,
            "You sense the presence of monsters!");
    }

    // src/spells.cpp:1881 spellDetectEvil() -- display evil creatures on
    // current panel -RAK-
    public static bool DetectEvil(PlayerWorld w)
    {
        return LightMonstersOnPanel(w,
            creature => (creature.Defenses & MonsterConfig.Defense.CdEvil) != 0,
            "You sense the presence of evil!");
    }

    private static bool LightMonstersOnPanel(PlayerWorld w, Func<Creature, bool> matches, string message)
    {
        var detected = false;

        for (var id = w.Monsters.NextFreeMonsterId - 1; id >= MonsterConfig.MonMinIndexId; id--)
        {
            ref var monster = ref w.Monsters.Monsters[id];

            if (ScreenPanel.CoordInsidePanel(w.Dg, monster.Pos) && matches(DataCreaturesData.CreaturesList[monster.CreatureId]))
            {
                monster.Lit = true;

                // works correctly even if hallucinating
                ScreenRendering.PutTile(w.Dg, (char)DataCreaturesData.CreaturesList[monster.CreatureId].Sprite, monster.Pos);

                detected = true;
            }
        }

        if (detected)
        {
            PlayerCore.PrintMessage(w, message);
            PlayerCore.FlushMessage(w);

            // must unlight every monster just lighted
            MonsterAi.UpdateMonsters(w, false);
        }

        return detected;
    }

    // src/spells.cpp:291 spellLightArea() -- light an area: -RAK-
    //     1.  If corridor  light immediate area
    //     2.  If room      light entire room plus immediate area.
    public static bool LightArea(PlayerWorld w, Coord coord)
    {
        var dg = w.Dg;

        if (w.Py.Flags.Blind < 1)
        {
            PlayerCore.PrintMessage(w, "You are surrounded by a white light.");
        }

        // NOTE: this is not changed anywhere. A bug or correct? -MRC-
        var lit = true;

        if (dg.Floor[coord.Y, coord.X].PermaLitRoom && dg.CurrentLevel > 0)
        {
            DungeonLighting.LightRoom(dg, w.Treasure, coord);
        }

        // Must always light immediate area, because one might be standing on
        // the edge of a room, or next to a destroyed area, etc.
        for (var y = coord.Y - 1; y <= coord.Y + 1; y++)
        {
            for (var x = coord.X - 1; x <= coord.X + 1; x++)
            {
                dg.Floor[y, x].PermanentLight = true;
                ScreenRendering.LiteSpot(dg, new Coord(y, x));
            }
        }

        return lit;
    }

    // src/spells.cpp:317 spellDarkenArea() -- darken an area, opposite of
    // light area -RAK-
    public static bool DarkenArea(PlayerWorld w, Coord coord)
    {
        var dg = w.Dg;
        var darkened = false;

        if (dg.Floor[coord.Y, coord.X].PermaLitRoom && dg.CurrentLevel > 0)
        {
            const int halfHeight = DungeonConstants.ScreenHeight / 2;
            const int halfWidth = DungeonConstants.ScreenWidth / 2;
            var startRow = (coord.Y / halfHeight) * halfHeight + 1;
            var startCol = (coord.X / halfWidth) * halfWidth + 1;
            var endRow = startRow + halfHeight - 1;
            var endCol = startCol + halfWidth - 1;

            for (var y = startRow; y <= endRow; y++)
            {
                for (var x = startCol; x <= endCol; x++)
                {
                    var spot = new Coord(y, x);
                    ref var tile = ref dg.Floor[y, x];

                    if (tile.PermaLitRoom && tile.FeatureId <= TileConstants.MaxCaveFloor)
                    {
                        tile.PermanentLight = false;
                        tile.FeatureId = TileConstants.TileDarkFloor;

                        ScreenRendering.LiteSpot(dg, spot);

                        if (!DungeonLighting.CaveTileVisible(dg, spot))
                        {
                            darkened = true;
                        }
                    }
                }
            }
        }
        else
        {
            for (var y = coord.Y - 1; y <= coord.Y + 1; y++)
            {
                for (var x = coord.X - 1; x <= coord.X + 1; x++)
                {
                    ref var tile = ref dg.Floor[y, x];

                    if (tile.FeatureId == TileConstants.TileCorrFloor && tile.PermanentLight)
                    {
                        // permanent_light could have been set by star-lite wand, etc
                        tile.PermanentLight = false;
                        darkened = true;
                    }
                }
            }
        }

        if (darkened && w.Py.Flags.Blind < 1)
        {
            PlayerCore.PrintMessage(w, "Darkness surrounds you.");
        }

        return darkened;
    }

    // src/spells.cpp:367 dungeonLightAreaAroundFloorTile()
    private static void LightAreaAroundFloorTile(PlayerWorld w, Coord coord)
    {
        var dg = w.Dg;

        for (var y = coord.Y - 1; y <= coord.Y + 1; y++)
        {
            for (var x = coord.X - 1; x <= coord.X + 1; x++)
            {
                ref var tile = ref dg.Floor[y, x];

                if (tile.FeatureId >= TileConstants.MinCaveWall)
                {
                    tile.PermanentLight = true;
                }
                else if (tile.TreasureId != 0 && w.Treasure[tile.TreasureId].CategoryId >= TreasureCategory.TvMinVisible &&
                         w.Treasure[tile.TreasureId].CategoryId <= TreasureCategory.TvMaxVisible)
                {
                    tile.FieldMark = true;
                }
            }
        }
    }

    // src/spells.cpp:385 spellMapCurrentArea() -- map the current area plus
    // some -RAK-
    public static void MapCurrentArea(PlayerWorld w)
    {
        var dg = w.Dg;

        var rowMin = dg.Panel.Top - RandomNumber.Next(10);
        var rowMax = dg.Panel.Bottom + RandomNumber.Next(10);
        var colMin = dg.Panel.Left - RandomNumber.Next(20);
        var colMax = dg.Panel.Right + RandomNumber.Next(20);

        for (var y = rowMin; y <= rowMax; y++)
        {
            for (var x = colMin; x <= colMax; x++)
            {
                var coord = new Coord(y, x);

                if (CoordHelpers.InBounds(dg, coord) && dg.Floor[y, x].FeatureId <= TileConstants.MaxCaveFloor)
                {
                    LightAreaAroundFloorTile(w, coord);
                }
            }
        }

        ScreenRendering.DrawDungeonPanel(dg);
    }
}
