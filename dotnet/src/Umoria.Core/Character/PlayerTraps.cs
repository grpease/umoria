using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Character;

// M8: traps. playerStepsOnTrap() and the trap* effects are file-static in
// src/player_move.cpp; disarming and chest traps are src/player_traps.cpp.
//
// Not ported (throw NotSupportedException, never silently skipped):
//   - corroding gas, fire and acid traps: damageCorrodingGas() /
//     damageFire() / damageAcid() destroy pack items (inventoryDamageItem(),
//     M11) and are the same primitives monster breath and spells use -- M9's
//     damage-spell slice.
//   - the town's store entrances (storeEnter(): M13).
// The poison gas trap's damagePoisonedGas() is only playerTakesHit() plus a
// poison timer, so it is ported here.
public static class PlayerTraps
{
    // src/player_move.cpp:214 enum class TrapTypes
    private static class TrapTypes
    {
        public const int OpenPit = 1;
        public const int ArrowPit = 2;
        public const int CoveredPit = 3;
        public const int TrapDoor = 4;
        public const int SleepingGas = 5;
        public const int HiddenObject = 6;
        public const int DartOfStr = 7;
        public const int Teleport = 8;
        public const int Rockfall = 9;
        public const int CorrodingGas = 10;
        public const int SummonMonster = 11;
        public const int FireTrap = 12;
        public const int AcidTrap = 13;
        public const int PoisonGasTrap = 14;
        public const int BlindingGas = 15;
        public const int ConfuseGas = 16;
        public const int SlowDart = 17;
        public const int DartOfCon = 18;
        public const int SecretDoor = 19;
        public const int ScareMonster = 99;
        public const int GeneralStore = 101;
        public const int MagicShop = 106;
    }

    // ---- stepping on a trap (src/player_move.cpp) ------------------------

    private static bool DartHits(PlayerWorld w)
    {
        var m = w.Py.Misc;
        return PlayerMovement.TestBeingHit(w, 125, 0, 0, m.Ac + m.MagicalAc, PlayerConfig.ClassMiscHit);
    }

    private static void TrapOpenPit(PlayerWorld w, string description, int dam)
    {
        PlayerCore.PrintMessage(w, "You fell into a pit!");

        if (w.Py.Flags.FreeFall)
        {
            PlayerCore.PrintMessage(w, "You gently float down.");
            return;
        }

        PlayerCore.TakesHit(w, dam, description);
    }

    private static void TrapArrow(PlayerWorld w, string description, int dam)
    {
        if (DartHits(w))
        {
            PlayerCore.TakesHit(w, dam, description);

            PlayerCore.PrintMessage(w, "An arrow hits you.");
            return;
        }

        PlayerCore.PrintMessage(w, "An arrow barely misses you.");
    }

    private static void TrapCoveredPit(PlayerWorld w, string description, int dam, Coord coord)
    {
        PlayerCore.PrintMessage(w, "You fell into a covered pit.");

        if (w.Py.Flags.FreeFall)
        {
            PlayerCore.PrintMessage(w, "You gently float down.");
        }
        else
        {
            PlayerCore.TakesHit(w, dam, description);
        }

        // (replaces the covered pit with an open one without freeing the
        // covered pit's treasure slot -- same as the original)
        w.Objects.SetTrap(coord, 0);
    }

    private static void TrapDoor(PlayerWorld w, string description, int dam)
    {
        w.Dg.GenerateNewLevel = true;
        w.Dg.CurrentLevel++;

        PlayerCore.PrintMessage(w, "You fell through a trap door!");

        if (w.Py.Flags.FreeFall)
        {
            PlayerCore.PrintMessage(w, "You gently float down.");
        }
        else
        {
            PlayerCore.TakesHit(w, dam, description);
        }

        // (printMessage(CNIL) -- forces the messages to display before the
        // next level is generated; a null message leaves command_count alone)
    }

    private static void TrapSleepingGas(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.Paralysis != 0)
        {
            return;
        }

        PlayerCore.PrintMessage(w, "A strange white mist surrounds you!");

        if (f.FreeAction)
        {
            PlayerCore.PrintMessage(w, "You are unaffected.");
            return;
        }

        f.Paralysis = (short)(f.Paralysis + RandomNumber.Next(10) + 4);
        PlayerCore.PrintMessage(w, "You fall asleep.");
    }

    private static void TrapHiddenObject(PlayerWorld w, Coord coord)
    {
        w.Objects.DeleteObject(coord);

        w.Objects.PlaceRandomObjectAt(coord, false);

        PlayerCore.PrintMessage(w, "Hmmm, there was something under this rock.");
    }

    private static void TrapStatDart(PlayerWorld w, string description, int dam, int stat, bool sustained, string hitMessage)
    {
        if (DartHits(w))
        {
            if (!sustained)
            {
                PlayerStatLogic.StatRandomDecrease(w.Py, stat);

                PlayerCore.TakesHit(w, dam, description);

                PlayerCore.PrintMessage(w, hitMessage);
            }
            else
            {
                PlayerCore.PrintMessage(w, "A small dart hits you.");
            }
        }
        else
        {
            PlayerCore.PrintMessage(w, "A small dart barely misses you.");
        }
    }

    private static void TrapTeleport(PlayerWorld w, Coord coord)
    {
        w.Game.TeleportPlayer = true;

        PlayerCore.PrintMessage(w, "You hit a teleport trap!");

        // Light up the teleport trap, before we teleport away.
        DungeonLighting.MoveCharacterLight(w.Py, w.Dg, w.Treasure, w.Game.Options, coord, coord);
    }

    private static void TrapRockfall(PlayerWorld w, Coord coord, int dam)
    {
        PlayerCore.TakesHit(w, dam, "a falling rock");

        w.Objects.DeleteObject(coord);
        w.Objects.PlaceRubble(coord);

        PlayerCore.PrintMessage(w, "You are hit by falling rock.");
    }

    private static void TrapSummonMonster(PlayerWorld w, Coord coord)
    {
        // Rune disappears.
        w.Objects.DeleteObject(coord);

        var num = 2 + RandomNumber.Next(3);

        for (var i = 0; i < num; i++)
        {
            var location = coord;
            w.Monsters.Summon(ref location, false, w.Py.Pos, w.Py.Flags.Speed);
        }
    }

    // src/inventory.cpp:579 damagePoisonedGas() -- poison gas the idiot. -RAK-
    private static void DamagePoisonedGas(PlayerWorld w, int damage, string creatureName)
    {
        PlayerCore.TakesHit(w, damage, creatureName);

        w.Py.Flags.Poisoned = (short)(w.Py.Flags.Poisoned + 12 + RandomNumber.Next(damage));
    }

    private static void TrapSlowDart(PlayerWorld w, string description, int dam)
    {
        var f = w.Py.Flags;

        if (DartHits(w))
        {
            PlayerCore.TakesHit(w, dam, description);

            PlayerCore.PrintMessage(w, "A small dart hits you!");

            if (f.FreeAction)
            {
                PlayerCore.PrintMessage(w, "You are unaffected.");
            }
            else
            {
                f.Slow = (short)(f.Slow + RandomNumber.Next(20) + 10);
            }
        }
        else
        {
            PlayerCore.PrintMessage(w, "A small dart barely misses you.");
        }
    }

    // src/player_move.cpp:243 playerStepsOnTrap() -- player hit a trap.
    // (Chuckle) -RAK-
    public static void StepsOnTrap(PlayerWorld w, Coord coord)
    {
        var f = w.Py.Flags;

        PlayerCore.EndRunning(w);
        w.Objects.TrapChangeVisibility(coord);

        var item = w.Treasure[w.Dg.Floor[coord.Y, coord.X].TreasureId];

        var damage = DiceRoller.Roll(item.Damage);

        // the death label: itemDescription(item) -- traps are one of its
        // simple, catalog-name-only categories
        string Description() => ItemDescription.Describe(item);

        switch (item.SubCategoryId)
        {
            case TrapTypes.OpenPit:
                TrapOpenPit(w, Description(), damage);
                break;
            case TrapTypes.ArrowPit:
                TrapArrow(w, Description(), damage);
                break;
            case TrapTypes.CoveredPit:
                TrapCoveredPit(w, Description(), damage, coord);
                break;
            case TrapTypes.TrapDoor:
                TrapDoor(w, Description(), damage);
                break;
            case TrapTypes.SleepingGas:
                TrapSleepingGas(w);
                break;
            case TrapTypes.HiddenObject:
                TrapHiddenObject(w, coord);
                break;
            case TrapTypes.DartOfStr:
                TrapStatDart(w, Description(), damage, PlayerConfig.Attr.Str, f.SustainStr, "A small dart weakens you!");
                break;
            case TrapTypes.Teleport:
                TrapTeleport(w, coord);
                break;
            case TrapTypes.Rockfall:
                TrapRockfall(w, coord, damage);
                break;
            case TrapTypes.CorrodingGas:
                throw new NotSupportedException("damageCorrodingGas() destroys pack items -- M9 damage primitives / M11, not yet ported.");
            case TrapTypes.SummonMonster:
                TrapSummonMonster(w, coord);
                break;
            case TrapTypes.FireTrap:
                throw new NotSupportedException("damageFire() destroys pack items -- M9 damage primitives / M11, not yet ported.");
            case TrapTypes.AcidTrap:
                throw new NotSupportedException("damageAcid() destroys pack items -- M9 damage primitives / M11, not yet ported.");
            case TrapTypes.PoisonGasTrap:
                PlayerCore.PrintMessage(w, "A pungent green gas surrounds you!");
                DamagePoisonedGas(w, damage, "a poison gas trap");
                break;
            case TrapTypes.BlindingGas:
                PlayerCore.PrintMessage(w, "A black gas surrounds you!");
                f.Blind = (short)(f.Blind + RandomNumber.Next(50) + 50);
                break;
            case TrapTypes.ConfuseGas:
                PlayerCore.PrintMessage(w, "A gas of scintillating colors surrounds you!");
                f.Confused = (short)(f.Confused + RandomNumber.Next(15) + 15);
                break;
            case TrapTypes.SlowDart:
                TrapSlowDart(w, Description(), damage);
                break;
            case TrapTypes.DartOfCon:
                TrapStatDart(w, Description(), damage, PlayerConfig.Attr.Con, f.SustainCon, "A small dart saps your health!");
                break;
            case TrapTypes.SecretDoor:
            case TrapTypes.ScareMonster:
                break;

            // Town level traps are special, the stores.
            case >= TrapTypes.GeneralStore and <= TrapTypes.MagicShop:
                throw new NotSupportedException("storeEnter() is M13, not yet ported.");

            default:
                // All cases are handled, so this should never be reached!
                PlayerCore.PrintMessage(w, "Unknown trap value.");
                break;
        }
    }

    // ---- disarming (src/player_traps.cpp) --------------------------------

    private static int TrapDisarmAbility(PlayerWorld w)
    {
        var py = w.Py;

        int ability = py.Misc.Disarm;
        ability += 2;
        ability *= PlayerStatLogic.DisarmAdjustment(py);
        ability += PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, PlayerConfig.Attr.Int);
        ability += DataPlayerData.ClassLevelAdj[py.Misc.ClassId][PlayerConfig.ClassLevelAdj.Disarm] * py.Misc.Level / 3;

        if (py.Flags.Blind > 0 || PlayerMovement.NoLight(w))
        {
            ability = ability / 10;
        }

        if (py.Flags.Confused > 0)
        {
            ability = ability / 10;
        }

        if (py.Flags.Image > 0)
        {
            ability = ability / 10;
        }

        return ability;
    }

    private static void DisarmFloorTrap(PlayerWorld w, Coord coord, int total, int level, int dir, short miscUse)
    {
        var f = w.Py.Flags;
        var confused = f.Confused;

        if (total + 100 - level > RandomNumber.Next(100))
        {
            PlayerCore.PrintMessage(w, "You have disarmed the trap.");
            w.Py.Misc.Exp += miscUse;
            w.Objects.DeleteObject(coord);

            // make sure we move onto the trap even if confused
            f.Confused = 0;
            PlayerMovement.Move(w, dir, false);
            f.Confused = confused;

            PlayerCore.DisplayCharacterExperience(w);
            return;
        }

        // avoid randomNumber(0) call
        if (total > 5 && RandomNumber.Next(total) > 5)
        {
            PlayerCore.PrintMessageNoCommandInterrupt(w, "You failed to disarm the trap.");
            return;
        }

        PlayerCore.PrintMessage(w, "You set the trap off!");

        // make sure we move onto the trap even if confused
        f.Confused = 0;
        PlayerMovement.Move(w, dir, false);
        f.Confused = (short)(f.Confused + confused);
    }

    private static void DisarmChestTrap(PlayerWorld w, Coord coord, int total, int treasureId)
    {
        ref var item = ref w.Treasure[treasureId];

        if (!ObjectIdentification.SpellItemIdentified(item))
        {
            w.Game.PlayerFreeTurn = true;
            PlayerCore.PrintMessage(w, "I don't see a trap.");

            return;
        }

        if ((item.Flags & TreasureConfig.Chests.ChTrapped) != 0)
        {
            int level = item.DepthFirstFound;

            if ((total - level) > RandomNumber.Next(100))
            {
                item.Flags &= ~TreasureConfig.Chests.ChTrapped;

                item.SpecialNameId = (item.Flags & TreasureConfig.Chests.ChLocked) != 0
                    ? (byte)SpecialNameIds.SnLocked
                    : (byte)SpecialNameIds.SnDisarmed;

                PlayerCore.PrintMessage(w, "You have disarmed the chest.");

                w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref item);
                w.Py.Misc.Exp += level;

                PlayerCore.DisplayCharacterExperience(w);
            }
            else if (total > 5 && RandomNumber.Next(total) > 5)
            {
                PlayerCore.PrintMessageNoCommandInterrupt(w, "You failed to disarm the chest.");
            }
            else
            {
                PlayerCore.PrintMessage(w, "You set a trap off!");
                w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref item);
                ChestTrap(w, coord);
            }
            return;
        }

        PlayerCore.PrintMessage(w, "The chest was not trapped.");
        w.Game.PlayerFreeTurn = true;
    }

    // src/player_traps.cpp:106 playerDisarmTrap() -- disarms a trap -RAK-
    // `dir` is what getDirectionWithMemory() returned (the prompt is input;
    // see PlayerCore.RememberDirection for its game-state half).
    public static void DisarmTrap(PlayerWorld w, int dir)
    {
        var coord = w.Py.Pos;
        PlayerMovement.MovePosition(w.Dg, dir, ref coord);

        var tile = w.Dg.Floor[coord.Y, coord.X];

        var noDisarm = false;

        if (tile.CreatureId > 1 && tile.TreasureId != 0 &&
            (w.Treasure[tile.TreasureId].CategoryId == TreasureCategory.TvVisTrap || w.Treasure[tile.TreasureId].CategoryId == TreasureCategory.TvChest))
        {
            PlayerCore.ObjectBlockedByMonster(w, tile.CreatureId);
        }
        else if (tile.TreasureId != 0)
        {
            var disarmAbility = TrapDisarmAbility(w);

            var item = w.Treasure[tile.TreasureId];

            if (item.CategoryId == TreasureCategory.TvVisTrap)
            {
                DisarmFloorTrap(w, coord, disarmAbility, item.DepthFirstFound, dir, item.MiscUse);
            }
            else if (item.CategoryId == TreasureCategory.TvChest)
            {
                DisarmChestTrap(w, coord, disarmAbility, tile.TreasureId);
            }
            else
            {
                noDisarm = true;
            }
        }
        else
        {
            noDisarm = true;
        }

        if (noDisarm)
        {
            PlayerCore.PrintMessage(w, "I do not see anything to disarm there.");
            w.Game.PlayerFreeTurn = true;
        }
    }

    // ---- chest traps (src/player_traps.cpp) ------------------------------

    private static void ChestLooseStrength(PlayerWorld w)
    {
        PlayerCore.PrintMessage(w, "A small needle has pricked you!");

        if (w.Py.Flags.SustainStr)
        {
            PlayerCore.PrintMessage(w, "You are unaffected.");
            return;
        }

        PlayerStatLogic.StatRandomDecrease(w.Py, PlayerConfig.Attr.Str);

        PlayerCore.TakesHit(w, DiceRoller.Roll(new Model.Generated.Dice(1, 4)), "a poison needle");

        PlayerCore.PrintMessage(w, "You feel weakened!");
    }

    private static void ChestPoison(PlayerWorld w)
    {
        PlayerCore.PrintMessage(w, "A small needle has pricked you!");

        PlayerCore.TakesHit(w, DiceRoller.Roll(new Model.Generated.Dice(1, 6)), "a poison needle");

        w.Py.Flags.Poisoned = (short)(w.Py.Flags.Poisoned + 10 + RandomNumber.Next(20));
    }

    private static void ChestParalysed(PlayerWorld w)
    {
        PlayerCore.PrintMessage(w, "A puff of yellow gas surrounds you!");

        if (w.Py.Flags.FreeAction)
        {
            PlayerCore.PrintMessage(w, "You are unaffected.");
            return;
        }

        PlayerCore.PrintMessage(w, "You choke and pass out.");
        w.Py.Flags.Paralysis = (short)(10 + RandomNumber.Next(20));
    }

    private static void ChestSummonMonster(PlayerWorld w, Coord coord)
    {
        for (var i = 0; i < 3; i++)
        {
            var position = coord;
            w.Monsters.Summon(ref position, false, w.Py.Pos, w.Py.Flags.Speed);
        }
    }

    private static void ChestExplode(PlayerWorld w, Coord coord)
    {
        PlayerCore.PrintMessage(w, "There is a sudden explosion!");

        w.Objects.DeleteObject(coord);

        PlayerCore.TakesHit(w, DiceRoller.Roll(new Model.Generated.Dice(5, 8)), "an exploding chest");
    }

    // src/player_traps.cpp:199 chestTrap() -- chests have traps too. -RAK-
    // Note: Chest traps are based on the FLAGS value
    public static void ChestTrap(PlayerWorld w, Coord coord)
    {
        var flags = w.Treasure[w.Dg.Floor[coord.Y, coord.X].TreasureId].Flags;

        if ((flags & TreasureConfig.Chests.ChLoseStr) != 0)
        {
            ChestLooseStrength(w);
        }

        if ((flags & TreasureConfig.Chests.ChPoison) != 0)
        {
            ChestPoison(w);
        }

        if ((flags & TreasureConfig.Chests.ChParalysed) != 0)
        {
            ChestParalysed(w);
        }

        if ((flags & TreasureConfig.Chests.ChSummon) != 0)
        {
            ChestSummonMonster(w, coord);
        }

        if ((flags & TreasureConfig.Chests.ChExplode) != 0)
        {
            ChestExplode(w, coord);
        }
    }
}
