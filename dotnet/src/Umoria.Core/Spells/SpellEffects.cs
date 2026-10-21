using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Spells;

// M9: the non-damaging src/spells.cpp effects that monster AI and combat
// reach directly -- spellTeleportAwayMonster() (monster teleport spells,
// thieves vanishing), spellTeleportPlayerTo() (teleport-to), and the
// aggravate and experience-drain attacks (spellAggravateMonsters(),
// spellLoseEXP()). The rest of spells.cpp's utility/status group is M10.
public static class SpellEffects
{
    // src/spells.cpp:432 spellAggravateMonsters() -- get all the monsters on
    // the level pissed off. -RAK-
    public static bool AggravateMonsters(PlayerWorld w, int affectDistance)
    {
        var aggravated = false;
        var monsters = w.Monsters.Monsters;

        for (var id = w.Monsters.NextFreeMonsterId - 1; id >= MonsterConfig.MonMinIndexId; id--)
        {
            ref var monster = ref monsters[id];
            monster.SleepCount = 0;

            if (monster.DistanceFromPlayer <= affectDistance && monster.Speed < 2)
            {
                monster.Speed++;
                aggravated = true;
            }
        }

        if (aggravated)
        {
            PlayerCore.PrintMessage(w, "You hear a sudden stirring in the distance!");
        }

        return aggravated;
    }

    // src/spells.cpp:1631 spellTeleportAwayMonster()
    public static void TeleportAwayMonster(PlayerWorld w, int monsterId, int distanceFromPlayer)
    {
        var dg = w.Dg;
        var counter = 0;

        ref var monster = ref w.Monsters.Monsters[monsterId];
        Coord coord;

        do
        {
            do
            {
                coord = new Coord(
                    monster.Pos.Y + (RandomNumber.Next(2 * distanceFromPlayer + 1) - (distanceFromPlayer + 1)),
                    monster.Pos.X + (RandomNumber.Next(2 * distanceFromPlayer + 1) - (distanceFromPlayer + 1)));
            } while (!CoordHelpers.InBounds(dg, coord));

            counter++;
            if (counter > 9)
            {
                counter = 0;
                distanceFromPlayer += 5;
            }
        } while (dg.Floor[coord.Y, coord.X].FeatureId >= TileConstants.MinClosedSpace || dg.Floor[coord.Y, coord.X].CreatureId != 0);

        w.Monsters.MoveCreatureRecord(monster.Pos, coord);
        ScreenRendering.LiteSpot(dg, monster.Pos);

        monster.Pos = coord;

        // this is necessary, because the creature is
        // not currently visible in its new position.
        monster.Lit = false;
        monster.DistanceFromPlayer = (byte)CoordHelpers.DistanceBetween(w.Py.Pos, coord);

        MonsterAi.UpdateVisibility(w, monsterId);
    }

    // src/spells.cpp:1665 spellTeleportPlayerTo() -- teleport player to
    // spell casting creature -RAK-
    public static void TeleportPlayerTo(PlayerWorld w, Coord coord)
    {
        var dg = w.Dg;
        var py = w.Py;

        var distance = 1;
        var counter = 0;

        Coord rndCoord;

        do
        {
            rndCoord = new Coord(
                coord.Y + (RandomNumber.Next(2 * distance + 1) - (distance + 1)),
                coord.X + (RandomNumber.Next(2 * distance + 1) - (distance + 1)));
            counter++;
            if (counter > 9)
            {
                counter = 0;
                distance++;
            }
        } while (!CoordHelpers.InBounds(dg, rndCoord) || dg.Floor[rndCoord.Y, rndCoord.X].FeatureId >= TileConstants.MinClosedSpace ||
                 dg.Floor[rndCoord.Y, rndCoord.X].CreatureId >= 2);

        w.Monsters.MoveCreatureRecord(py.Pos, rndCoord);

        for (var y = py.Pos.Y - 1; y <= py.Pos.Y + 1; y++)
        {
            for (var x = py.Pos.X - 1; x <= py.Pos.X + 1; x++)
            {
                dg.Floor[y, x].TemporaryLight = false;
                ScreenRendering.LiteSpot(dg, new Coord(y, x));
            }
        }

        ScreenRendering.LiteSpot(dg, py.Pos);

        py.Pos = rndCoord;

        DungeonLighting.ResetView(py, dg, w.Treasure, w.Game.Options);

        // light creatures
        MonsterAi.UpdateMonsters(w, false);
    }

    // src/spells.cpp:2166 spellLoseEXP()
    //
    // playerCalculateAllowedSpellsCount()'s "forgotten spells" messages go
    // through PlayerSpells' display-only PrintMessage (see M7/M8); the one
    // in-game caller here, the lose-experience attack, has already zeroed
    // game.command_count with its own message, so that is exact.
    public static void LoseExp(PlayerWorld w, int adjustment)
    {
        var py = w.Py;

        if (adjustment > py.Misc.Exp)
        {
            py.Misc.Exp = 0;
        }
        else
        {
            py.Misc.Exp -= adjustment;
        }
        PlayerCore.DisplayCharacterExperience(w);

        var exp = 0;
        while ((int)(py.BaseExpLevels[exp] * py.Misc.ExperienceFactor / 100) <= py.Misc.Exp)
        {
            exp++;
        }

        // increment exp once more, because level 1 exp is stored in player_base_exp_levels[0]
        exp++;

        if (py.Misc.Level != exp)
        {
            py.Misc.Level = (ushort)exp;

            PlayerStatLogic.CalculateHitPoints(py);

            var spellType = DataPlayerData.Classes[py.Misc.ClassId].ClassToUseMageSpells;

            if (spellType == SpellConfig.SpellTypeMage)
            {
                PlayerSpells.CalculateAllowedSpellsCount(py, PlayerConfig.Attr.Int);
                PlayerSpells.GainMana(py, PlayerConfig.Attr.Int);
            }
            else if (spellType == SpellConfig.SpellTypePriest)
            {
                PlayerSpells.CalculateAllowedSpellsCount(py, PlayerConfig.Attr.Wis);
                PlayerSpells.GainMana(py, PlayerConfig.Attr.Wis);
            }
            // (printCharacterLevel(), printCharacterTitle() -- display)
        }
    }
}
