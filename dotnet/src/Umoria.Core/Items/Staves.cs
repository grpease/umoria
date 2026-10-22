using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Spells;

namespace Umoria.Core.Items;

// src/staves.cpp -- using staffs and aiming wands (M11). Like scrolls, the
// spells were ported in M9/M10; this is the device-skill roll, the
// dispatch and the charge bookkeeping.
public static class Staves
{
    // src/staves.cpp:8 StaffSpellTypes
    private enum StaffSpellTypes
    {
        StaffLight = 1,
        DetectDoorsStairs,
        TrapLocation,
        TreasureLocation,
        ObjectLocation,
        Teleportation,
        Earthquakes,
        Summoning,
        // skipping 9
        Destruction = 10,
        Starlight,
        HasteMonsters,
        SlowMonsters,
        SleepMonsters,
        CureLightWounds,
        DetectInvisible,
        Speed,
        Slowness,
        MassPolymorph,
        RemoveCurse,
        DetectEvil,
        Curing,
        DispelEvil,
        // skipping 24
        Darkness = 25,
        // skipping lots...
        StoreBoughtFlag = 32,
    }

    // src/staves.cpp:240 WandSpellTypes
    private enum WandSpellTypes
    {
        WandLight = 1,
        LightningBolt,
        FrostBolt,
        FireBolt,
        StoneToMud,
        Polymorph,
        HealMonster,
        HasteMonster,
        SlowMonster,
        ConfuseMonster,
        SleepMonster,
        DrainLife,
        TrapDoorDestruction,
        WandMagicMissile,
        WallBuilding,
        CloneMonster,
        TeleportAway,
        Disarming,
        LightningBall,
        ColdBall,
        FireBall,
        StinkingCloud,
        AcidBall,
        Wonder,
    }

    // The device-skill part of staffPlayerCanUse() and wandAim(), which differ
    // only in how they subtract the item's level and clamp below 1.
    private static int DeviceChance(Player py, in Inventory item, bool isStaff)
    {
        var chance = py.Misc.SavingThrow + PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, PlayerConfig.Attr.Int);
        chance -= isStaff ? item.DepthFirstFound - 5 : item.DepthFirstFound;
        chance += DataPlayerData.ClassLevelAdj[py.Misc.ClassId][PlayerConfig.ClassLevelAdj.Device] * py.Misc.Level / 3;

        if (py.Flags.Confused > 0)
        {
            chance /= 2;
        }

        // Give everyone a slight chance
        if (chance < PlayerConfig.PlayerUseDeviceDifficulty && RandomNumber.Next(PlayerConfig.PlayerUseDeviceDifficulty - chance + 1) == 1)
        {
            chance = PlayerConfig.PlayerUseDeviceDifficulty;
        }

        if (chance < 1)
        {
            chance = 1;
        }

        return chance;
    }

    // src/staves.cpp:38 staffPlayerIsCarrying()
    private static bool StaffPlayerIsCarrying(PlayerWorld w, out int itemPosStart, out int itemPosEnd)
    {
        itemPosStart = 0;
        itemPosEnd = 0;

        if (w.Py.Pack.UniqueItems == 0)
        {
            PlayerCore.PrintMessage(w, "But you are not carrying anything.");
            return false;
        }

        if (!InventoryDamage.FindRange(w.Py, TreasureCategory.TvStaff, TreasureCategory.TvNever, out itemPosStart, out itemPosEnd))
        {
            PlayerCore.PrintMessage(w, "You are not carrying any staffs.");
            return false;
        }

        return true;
    }

    // src/staves.cpp:52 staffPlayerCanUse()
    private static bool StaffPlayerCanUse(PlayerWorld w, ref Inventory item)
    {
        var chance = DeviceChance(w.Py, item, true);

        if (RandomNumber.Next(chance) < PlayerConfig.PlayerUseDeviceDifficulty)
        {
            PlayerCore.PrintMessage(w, "You failed to use the staff properly.");
            return false;
        }

        if (item.MiscUse < 1)
        {
            PlayerCore.PrintMessage(w, "The staff has no charges left.");
            if (!ObjectIdentification.SpellItemIdentified(item))
            {
                ObjectIdentification.ItemAppendToInscription(ref item, IdentificationConfig.IdEmpty);
            }
            return false;
        }

        return true;
    }

    // src/staves.cpp:87 staffDischarge()
    private static bool StaffDischarge(PlayerWorld w, ref Inventory item)
    {
        var py = w.Py;
        var f = py.Flags;
        var identified = false;

        item.MiscUse--;

        var flags = item.Flags;
        while (flags != 0)
        {
            switch ((StaffSpellTypes)(Helpers.GetAndClearFirstBit(ref flags) + 1))
            {
                case StaffSpellTypes.StaffLight:
                    identified = DetectionSpells.LightArea(w, py.Pos);
                    break;
                case StaffSpellTypes.DetectDoorsStairs:
                    identified = DetectionSpells.DetectSecretDoorsWithinVicinity(w);
                    break;
                case StaffSpellTypes.TrapLocation:
                    identified = DetectionSpells.DetectTrapsWithinVicinity(w);
                    break;
                case StaffSpellTypes.TreasureLocation:
                    identified = DetectionSpells.DetectTreasureWithinVicinity(w);
                    break;
                case StaffSpellTypes.ObjectLocation:
                    identified = DetectionSpells.DetectObjectsWithinVicinity(w);
                    break;
                case StaffSpellTypes.Teleportation:
                    PlayerMovement.Teleport(w, 100);
                    identified = true;
                    break;
                case StaffSpellTypes.Earthquakes:
                    identified = true;
                    DamageSpells.Earthquake(w);
                    break;
                case StaffSpellTypes.Summoning:
                    identified = false;

                    // (randomNumber(4) is re-rolled on every pass of the loop test)
                    for (var i = 0; i < RandomNumber.Next(4); i++)
                    {
                        var coord = py.Pos;
                        identified |= w.Monsters.Summon(ref coord, false, py.Pos, f.Speed);
                    }
                    break;
                case StaffSpellTypes.Destruction:
                    identified = true;
                    TerrainSpells.DestroyArea(w, py.Pos);
                    break;
                case StaffSpellTypes.Starlight:
                    identified = true;
                    TerrainSpells.Starlite(w, py.Pos);
                    break;
                case StaffSpellTypes.HasteMonsters:
                    identified = MonsterStatusSpells.SpeedAllMonsters(w, 1);
                    break;
                case StaffSpellTypes.SlowMonsters:
                    identified = MonsterStatusSpells.SpeedAllMonsters(w, -1);
                    break;
                case StaffSpellTypes.SleepMonsters:
                    identified = MonsterStatusSpells.SleepAllMonsters(w);
                    break;
                case StaffSpellTypes.CureLightWounds:
                    identified = SpellEffects.ChangePlayerHitPoints(w, RandomNumber.Next(8));
                    break;
                case StaffSpellTypes.DetectInvisible:
                    identified = DetectionSpells.DetectInvisibleCreaturesWithinVicinity(w);
                    break;
                case StaffSpellTypes.Speed:
                    if (f.Fast == 0)
                    {
                        identified = true;
                    }
                    f.Fast = (short)(f.Fast + RandomNumber.Next(30) + 15);
                    break;
                case StaffSpellTypes.Slowness:
                    if (f.Slow == 0)
                    {
                        identified = true;
                    }
                    f.Slow = (short)(f.Slow + RandomNumber.Next(30) + 15);
                    break;
                case StaffSpellTypes.MassPolymorph:
                    identified = MonsterStatusSpells.MassPolymorph(w);
                    break;
                case StaffSpellTypes.RemoveCurse:
                    if (SpellEffects.RemoveCurseFromAllWornItems(w))
                    {
                        if (f.Blind < 1)
                        {
                            PlayerCore.PrintMessage(w, "The staff glows blue for a moment..");
                        }
                        identified = true;
                    }
                    break;
                case StaffSpellTypes.DetectEvil:
                    identified = DetectionSpells.DetectEvil(w);
                    break;
                case StaffSpellTypes.Curing:
                    if (PlayerMagic.CureBlindness(py) || PlayerMagic.CurePoison(py) || PlayerMagic.CureConfusion(py))
                    {
                        identified = true;
                    }
                    break;
                case StaffSpellTypes.DispelEvil:
                    identified = DamageSpells.DispelCreature(w, MonsterConfig.Defense.CdEvil, 60);
                    break;
                case StaffSpellTypes.Darkness:
                    identified = DetectionSpells.DarkenArea(w, py.Pos);
                    break;
                case StaffSpellTypes.StoreBoughtFlag:
                    // store bought flag
                    break;
                default:
                    // All cases are handled, so this should never be reached!
                    PlayerCore.PrintMessage(w, "Internal error in staffs()");
                    break;
            }
        }

        return identified;
    }

    // src/staves.cpp:200 staffUse() -- use a staff. -RAK-
    public static void StaffUse(PlayerWorld w)
    {
        var py = w.Py;

        w.Game.PlayerFreeTurn = true;

        if (!StaffPlayerIsCarrying(w, out var itemPosStart, out var itemPosEnd))
        {
            return;
        }

        if (!PlayerCore.GetItemId(w, "Use which staff?", itemPosStart, itemPosEnd, out var itemId))
        {
            return;
        }

        // From here on player uses up a turn
        w.Game.PlayerFreeTurn = false;

        if (!StaffPlayerCanUse(w, ref py.Inventory[itemId]))
        {
            return;
        }

        var identified = StaffDischarge(w, ref py.Inventory[itemId]);

        ItemUse.IdentifyOrTry(w, identified, ref itemId);

        ItemDescription.ChargesRemainingDescription(w, itemId);
    }

    // src/staves.cpp:267 wandDischarge()
    private static bool WandDischarge(PlayerWorld w, ref Inventory item, int direction)
    {
        var py = w.Py;
        var names = DataPlayerData.SpellNames;

        // decrement "use" variable
        item.MiscUse--;

        var identified = false;
        var flags = item.Flags;

        while (flags != 0)
        {
            var coord = py.Pos;

            // Wand types
            switch ((WandSpellTypes)(Helpers.GetAndClearFirstBit(ref flags) + 1))
            {
                case WandSpellTypes.WandLight:
                    PlayerCore.PrintMessage(w, "A line of blue shimmering light appears.");
                    TerrainSpells.LightLine(w, py.Pos, direction);
                    identified = true;
                    break;
                case WandSpellTypes.LightningBolt:
                    DamageSpells.FireBolt(w, coord, direction, DiceRoller.Roll(new Model.Generated.Dice(4, 8)), MagicSpellFlags.Lightning, names[8]);
                    identified = true;
                    break;
                case WandSpellTypes.FrostBolt:
                    DamageSpells.FireBolt(w, coord, direction, DiceRoller.Roll(new Model.Generated.Dice(6, 8)), MagicSpellFlags.Frost, names[14]);
                    identified = true;
                    break;
                case WandSpellTypes.FireBolt:
                    DamageSpells.FireBolt(w, coord, direction, DiceRoller.Roll(new Model.Generated.Dice(9, 8)), MagicSpellFlags.Fire, names[22]);
                    identified = true;
                    break;
                case WandSpellTypes.StoneToMud:
                    identified = TerrainSpells.WallToMud(w, coord, direction);
                    break;
                case WandSpellTypes.Polymorph:
                    identified = MonsterStatusSpells.PolymorphMonster(w, coord, direction);
                    break;
                case WandSpellTypes.HealMonster:
                    identified = DamageSpells.ChangeMonsterHitPoints(w, coord, direction, -DiceRoller.Roll(new Model.Generated.Dice(4, 6)));
                    break;
                case WandSpellTypes.HasteMonster:
                    identified = MonsterStatusSpells.SpeedMonster(w, coord, direction, 1);
                    break;
                case WandSpellTypes.SlowMonster:
                    identified = MonsterStatusSpells.SpeedMonster(w, coord, direction, -1);
                    break;
                case WandSpellTypes.ConfuseMonster:
                    identified = MonsterStatusSpells.ConfuseMonster(w, coord, direction);
                    break;
                case WandSpellTypes.SleepMonster:
                    identified = MonsterStatusSpells.SleepMonster(w, coord, direction);
                    break;
                case WandSpellTypes.DrainLife:
                    identified = DamageSpells.DrainLifeFromMonster(w, coord, direction);
                    break;
                case WandSpellTypes.TrapDoorDestruction:
                    identified = TerrainSpells.DestroyDoorsTrapsInDirection(w, coord, direction);
                    break;
                case WandSpellTypes.WandMagicMissile:
                    DamageSpells.FireBolt(w, coord, direction, DiceRoller.Roll(new Model.Generated.Dice(2, 6)), MagicSpellFlags.MagicMissile, names[0]);
                    identified = true;
                    break;
                case WandSpellTypes.WallBuilding:
                    identified = TerrainSpells.BuildWall(w, coord, direction);
                    break;
                case WandSpellTypes.CloneMonster:
                    identified = MonsterStatusSpells.CloneMonster(w, coord, direction);
                    break;
                case WandSpellTypes.TeleportAway:
                    identified = MonsterStatusSpells.TeleportAwayMonsterInDirection(w, coord, direction);
                    break;
                case WandSpellTypes.Disarming:
                    identified = TerrainSpells.DisarmAllInDirection(w, coord, direction);
                    break;
                case WandSpellTypes.LightningBall:
                    DamageSpells.FireBall(w, coord, direction, 32, MagicSpellFlags.Lightning, "Lightning Ball");
                    identified = true;
                    break;
                case WandSpellTypes.ColdBall:
                    DamageSpells.FireBall(w, coord, direction, 48, MagicSpellFlags.Frost, "Cold Ball");
                    identified = true;
                    break;
                case WandSpellTypes.FireBall:
                    DamageSpells.FireBall(w, coord, direction, 72, MagicSpellFlags.Fire, names[28]);
                    identified = true;
                    break;
                case WandSpellTypes.StinkingCloud:
                    DamageSpells.FireBall(w, coord, direction, 12, MagicSpellFlags.PoisonGas, names[6]);
                    identified = true;
                    break;
                case WandSpellTypes.AcidBall:
                    DamageSpells.FireBall(w, coord, direction, 60, MagicSpellFlags.Acid, "Acid Ball");
                    identified = true;
                    break;
                case WandSpellTypes.Wonder:
                    flags = 1u << (RandomNumber.Next(23) - 1);
                    break;
                default:
                    // All cases are handled, so this should never be reached!
                    PlayerCore.PrintMessage(w, "Internal error in wands()");
                    break;
            }
        }

        return identified;
    }

    // src/staves.cpp:376 wandAim() -- wands for the aiming.
    public static void WandAim(PlayerWorld w)
    {
        var py = w.Py;

        w.Game.PlayerFreeTurn = true;

        if (py.Pack.UniqueItems == 0)
        {
            PlayerCore.PrintMessage(w, "But you are not carrying anything.");
            return;
        }

        if (!InventoryDamage.FindRange(py, TreasureCategory.TvWand, TreasureCategory.TvNever, out var itemPosStart, out var itemPosEnd))
        {
            PlayerCore.PrintMessage(w, "You are not carrying any wands.");
            return;
        }

        if (!PlayerCore.GetItemId(w, "Aim which wand?", itemPosStart, itemPosEnd, out var itemId))
        {
            return;
        }

        w.Game.PlayerFreeTurn = false;

        if (!PlayerCore.GetDirectionWithMemory(w, null, out var direction))
        {
            return;
        }

        if (py.Flags.Confused > 0)
        {
            PlayerCore.PrintMessage(w, "You are confused.");
            direction = PlayerCore.GetRandomDirection();
        }

        ref var item = ref py.Inventory[itemId];

        var chance = DeviceChance(py, item, false);

        if (RandomNumber.Next(chance) < PlayerConfig.PlayerUseDeviceDifficulty)
        {
            PlayerCore.PrintMessage(w, "You failed to use the wand properly.");
            return;
        }

        if (item.MiscUse < 1)
        {
            PlayerCore.PrintMessage(w, "The wand has no charges left.");
            if (!ObjectIdentification.SpellItemIdentified(item))
            {
                ObjectIdentification.ItemAppendToInscription(ref item, IdentificationConfig.IdEmpty);
            }
            return;
        }

        var identified = WandDischarge(w, ref item, direction);

        ItemUse.IdentifyOrTry(w, identified, ref itemId);

        ItemDescription.ChargesRemainingDescription(w, itemId);
    }
}
