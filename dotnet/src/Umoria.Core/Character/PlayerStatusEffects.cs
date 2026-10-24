using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Character;

// The per-turn player status-effect updates carved out of src/game_run.cpp
// (M7): light fuel, heroism, food/hunger and regeneration, blindness,
// confusion, fear, poison, speed, resting, hallucination, paralysis, the
// timed protections, detect invisible, infravision, word of recall, the
// status-flag housekeeping, encumbrance and the detect-enchantment roll.
//
// Entry points mirror where playDungeon() runs them:
//   - EnterLevel()        -- the level-entry preamble before the loop.
//   - UpdateTimedEffects() -- the loop body from playerUpdateLightStatus()
//                            through playerUpdateWordOfRecall().
//   - UpdateStatusUpkeep() -- the loop body after random teleportation:
//                            playerStrength(), playerUpdateStatusFlags() and
//                            the detect-enchantment roll.
// playDungeon() itself (turn counter, store maintenance, monster spawning
// and movement, the keypress interrupt, random teleport, command input) is
// M16's job; see migration-plan.md's M7 section.
//
// Display calls (printCharacter*(), drawDungeonPanel()) go through w.Ui /
// ScreenRendering at the original's call sites (M14); messages go through
// PlayerCore.PrintMessage.
public static class PlayerStatusEffects
{
    // ---- playDungeon() level-entry preamble -----------------------------

    // src/game_run.cpp:2293 playDungeon() -- the state-changing part of the
    // preamble before its main loop.
    public static void EnterLevel(PlayerWorld w)
    {
        var py = w.Py;

        InitializePlayerLight(py);
        UpdateMaxDungeonDepth(w);
        ResetDungeonFlags(w);

        // (the original sets dg.panel.row/col = -1 to force a panel redraw -- UI)
        DungeonLighting.ResetView(py, w.Dg, w.Treasure, w.Game.Options);

        if ((py.Flags.Status & PlayerConfig.Status.PySearch) != 0)
        {
            PlayerCore.SearchOff(w);
        }

        // Light, but do not move critters
        PlayerCore.UpdateMonstersWithoutMoving(w);
    }

    // src/game_run.cpp:258 resetDungeonFlags() -- reset flags and
    // initialize variables
    private static void ResetDungeonFlags(PlayerWorld w)
    {
        w.Game.CommandCount = 0;
        w.Dg.GenerateNewLevel = false;
        w.Py.RunningTracker = 0;
        w.Game.TeleportPlayer = false;
        w.Monsters.MonsterMultiplyTotal = 0;
        w.Dg.Floor[w.Py.Pos.Y, w.Py.Pos.X].CreatureId = 1;
    }

    // src/game_run.cpp:268 playerInitializePlayerLight() -- check light
    // status for dungeon setup
    private static void InitializePlayerLight(Player py)
    {
        py.CarryingLight = py.Inventory[PlayerConfig.Equipment.Light].MiscUse > 0;
    }

    // src/game_run.cpp:273 playerUpdateMaxDungeonDepth() -- check for a
    // maximum level
    private static void UpdateMaxDungeonDepth(PlayerWorld w)
    {
        if (w.Dg.CurrentLevel > w.Py.Misc.MaxDungeonDepth)
        {
            w.Py.Misc.MaxDungeonDepth = (ushort)w.Dg.CurrentLevel;
        }
    }

    // ---- playDungeon() loop body ----------------------------------------

    // The loop body from playerUpdateLightStatus() through
    // playerUpdateWordOfRecall(), in the original order.
    public static void UpdateTimedEffects(PlayerWorld w)
    {
        UpdateLightStatus(w);

        // Heroism and Super Heroism must precede anything that can damage player
        UpdateHeroStatus(w);

        var regenAmount = FoodConsumption(w);
        UpdateRegeneration(w, regenAmount);

        UpdateBlindness(w);
        UpdateConfusion(w);
        UpdateFearState(w);
        UpdatePoisonedState(w);
        UpdateSpeed(w);
        UpdateRestingState(w);

        // (the original checks for a keypress here to interrupt
        // repeat/run/rest -- input, M8/M14; headless means no key)

        UpdateHallucination(w);
        UpdateParalysis(w);
        UpdateEvilProtection(w);
        UpdateInvulnerability(w);
        UpdateBlessedness(w);
        UpdateHeatResistance(w);
        UpdateColdResistance(w);
        UpdateDetectInvisible(w);
        UpdateInfraVision(w);
        UpdateWordOfRecall(w);
    }

    // The loop body after random teleportation (M8): encumbrance, the
    // status-flag housekeeping and the detect-enchantment roll.
    public static void UpdateStatusUpkeep(PlayerWorld w)
    {
        var py = w.Py;

        // See if we are too weak to handle the weapon or pack. -CJS-
        if ((py.Flags.Status & PlayerConfig.Status.PyStrWgt) != 0)
        {
            PlayerCore.Strength(w);
        }

        // src/ui.cpp:381 printCharacterStudyInstruction() -- clears
        // PY_STUDY, then shows/blanks "Study".
        if ((py.Flags.Status & PlayerConfig.Status.PyStudy) != 0)
        {
            if (w.Ui is { } ui)
            {
                ui.PrintCharacterStudyInstruction();
            }
            else
            {
                py.Flags.Status &= ~PlayerConfig.Status.PyStudy;
            }
        }

        UpdateStatusFlags(w);

        // Allow for a slim chance of detect enchantment -CJS-
        // for 1st level char, check once every 2160 turns
        // for 40th level char, check once every 416 turns
        var chance = 10 + 750 / (5 + py.Misc.Level);
        if ((w.Dg.GameTurn & 0xF) == 0 && py.Flags.Confused == 0 && RandomNumber.Next(chance) == 1)
        {
            DetectEnchantment(w);
        }
    }

    // src/game_run.cpp:280 playerUpdateLightStatus() -- check light status
    private static void UpdateLightStatus(PlayerWorld w)
    {
        var py = w.Py;
        ref var item = ref py.Inventory[PlayerConfig.Equipment.Light];

        if (py.CarryingLight)
        {
            if (item.MiscUse > 0)
            {
                item.MiscUse--;

                if (item.MiscUse == 0)
                {
                    py.CarryingLight = false;
                    PlayerCore.PrintMessage(w, "Your light has gone out!");
                    PlayerCore.Disturb(w, 0, 1);

                    // unlight creatures
                    PlayerCore.UpdateMonstersWithoutMoving(w);
                }
                else if (item.MiscUse < 40 && RandomNumber.Next(5) == 1 && py.Flags.Blind < 1)
                {
                    PlayerCore.Disturb(w, 0, 0);
                    PlayerCore.PrintMessage(w, "Your light is growing faint.");
                }
            }
            else
            {
                py.CarryingLight = false;
                PlayerCore.Disturb(w, 0, 1);

                // unlight creatures
                PlayerCore.UpdateMonstersWithoutMoving(w);
            }
        }
        else if (item.MiscUse > 0)
        {
            item.MiscUse--;
            py.CarryingLight = true;
            PlayerCore.Disturb(w, 0, 1);

            // light creatures
            PlayerCore.UpdateMonstersWithoutMoving(w);
        }
    }

    // src/game_run.cpp:315 playerActivateHeroism()
    private static void ActivateHeroism(PlayerWorld w)
    {
        var m = w.Py.Misc;

        w.Py.Flags.Status |= PlayerConfig.Status.PyHero;
        PlayerCore.Disturb(w, 0, 0);

        m.MaxHp += 10;
        m.CurrentHp += 10;
        m.Bth += 12;
        m.BthWithBows += 12;

        PlayerCore.PrintMessage(w, "You feel like a HERO!");
        w.Ui?.PrintCharacterMaxHitPoints();
        w.Ui?.PrintCharacterCurrentHitPoints();
    }

    // src/game_run.cpp:329 playerDisableHeroism()
    private static void DisableHeroism(PlayerWorld w)
    {
        var m = w.Py.Misc;

        w.Py.Flags.Status &= ~PlayerConfig.Status.PyHero;
        PlayerCore.Disturb(w, 0, 0);

        m.MaxHp -= 10;
        if (m.CurrentHp > m.MaxHp)
        {
            m.CurrentHp = m.MaxHp;
            m.CurrentHpFraction = 0;
            w.Ui?.PrintCharacterCurrentHitPoints();
        }
        m.Bth -= 12;
        m.BthWithBows -= 12;

        PlayerCore.PrintMessage(w, "The heroism wears off.");
        w.Ui?.PrintCharacterMaxHitPoints();
    }

    // src/game_run.cpp:346 playerActivateSuperHeroism()
    private static void ActivateSuperHeroism(PlayerWorld w)
    {
        var m = w.Py.Misc;

        w.Py.Flags.Status |= PlayerConfig.Status.PyShero;
        PlayerCore.Disturb(w, 0, 0);

        m.MaxHp += 20;
        m.CurrentHp += 20;
        m.Bth += 24;
        m.BthWithBows += 24;

        PlayerCore.PrintMessage(w, "You feel like a SUPER HERO!");
        w.Ui?.PrintCharacterMaxHitPoints();
        w.Ui?.PrintCharacterCurrentHitPoints();
    }

    // src/game_run.cpp:360 playerDisableSuperHeroism()
    private static void DisableSuperHeroism(PlayerWorld w)
    {
        var m = w.Py.Misc;

        w.Py.Flags.Status &= ~PlayerConfig.Status.PyShero;
        PlayerCore.Disturb(w, 0, 0);

        m.MaxHp -= 20;
        if (m.CurrentHp > m.MaxHp)
        {
            m.CurrentHp = m.MaxHp;
            m.CurrentHpFraction = 0;
            w.Ui?.PrintCharacterCurrentHitPoints();
        }
        m.Bth -= 24;
        m.BthWithBows -= 24;

        PlayerCore.PrintMessage(w, "The super heroism wears off.");
        w.Ui?.PrintCharacterMaxHitPoints();
    }

    // src/game_run.cpp:377 playerUpdateHeroStatus()
    private static void UpdateHeroStatus(PlayerWorld w)
    {
        var f = w.Py.Flags;

        // Heroism
        if (f.Heroism > 0)
        {
            if ((f.Status & PlayerConfig.Status.PyHero) == 0)
            {
                ActivateHeroism(w);
            }

            f.Heroism--;

            if (f.Heroism == 0)
            {
                DisableHeroism(w);
            }
        }

        // Super Heroism
        if (f.SuperHeroism > 0)
        {
            if ((f.Status & PlayerConfig.Status.PyShero) == 0)
            {
                ActivateSuperHeroism(w);
            }

            f.SuperHeroism--;

            if (f.SuperHeroism == 0)
            {
                DisableSuperHeroism(w);
            }
        }
    }

    // src/game_run.cpp:405 playerFoodConsumption() -- returns the
    // hp/mana regeneration amount for this turn.
    private static int FoodConsumption(PlayerWorld w)
    {
        var f = w.Py.Flags;

        // Regenerate hp and mana
        int regenAmount = PlayerConfig.PlayerRegenNormal;

        if (f.Food < PlayerConfig.PlayerFoodAlert)
        {
            if (f.Food < PlayerConfig.PlayerFoodWeak)
            {
                if (f.Food < 0)
                {
                    regenAmount = 0;
                }
                else if (f.Food < PlayerConfig.PlayerFoodFaint)
                {
                    regenAmount = PlayerConfig.PlayerRegenFaint;
                }
                else if (f.Food < PlayerConfig.PlayerFoodWeak)
                {
                    regenAmount = PlayerConfig.PlayerRegenWeak;
                }

                if ((f.Status & PlayerConfig.Status.PyWeak) == 0)
                {
                    f.Status |= PlayerConfig.Status.PyWeak;
                    PlayerCore.PrintMessage(w, "You are getting weak from hunger.");
                    PlayerCore.Disturb(w, 0, 0);
                    w.Ui?.PrintCharacterHungerStatus();
                }

                if (f.Food < PlayerConfig.PlayerFoodFaint && RandomNumber.Next(8) == 1)
                {
                    f.Paralysis = (short)(f.Paralysis + RandomNumber.Next(5));
                    PlayerCore.PrintMessage(w, "You faint from the lack of food.");
                    PlayerCore.Disturb(w, 1, 0);
                }
            }
            else if ((f.Status & PlayerConfig.Status.PyHungry) == 0)
            {
                f.Status |= PlayerConfig.Status.PyHungry;
                PlayerCore.PrintMessage(w, "You are getting hungry.");
                PlayerCore.Disturb(w, 0, 0);
                w.Ui?.PrintCharacterHungerStatus();
            }
        }

        // Food consumption
        // Note: Sped up characters really burn up the food!
        if (f.Speed < 0)
        {
            f.Food = (short)(f.Food - f.Speed * f.Speed);
        }

        f.Food = (short)(f.Food - f.FoodDigested);

        if (f.Food < 0)
        {
            PlayerCore.TakesHit(w, -f.Food / 16, "starvation"); // -CJS-
            PlayerCore.Disturb(w, 1, 0);
        }

        return regenAmount;
    }

    // src/game_run.cpp:455 playerUpdateRegeneration()
    private static void UpdateRegeneration(PlayerWorld w, int amount)
    {
        var py = w.Py;

        if (py.Flags.RegenerateHp)
        {
            amount = amount * 3 / 2;
        }

        if ((py.Flags.Status & PlayerConfig.Status.PySearch) != 0 || py.Flags.Rest != 0)
        {
            amount *= 2;
        }

        if (py.Flags.Poisoned < 1 && py.Misc.CurrentHp < py.Misc.MaxHp)
        {
            RegenerateHitPoints(w, amount);
        }

        if (py.Misc.CurrentMana < py.Misc.Mana)
        {
            RegenerateMana(w, amount);
        }
    }

    // src/game_run.cpp:1995 playerRegenerateHitPoints() -RAK-
    private static void RegenerateHitPoints(PlayerWorld w, int percent)
    {
        var m = w.Py.Misc;

        int oldChp = m.CurrentHp;
        var newChp = m.MaxHp * percent + PlayerConfig.PlayerRegenHpBase;

        // div 65536
        m.CurrentHp = (short)(m.CurrentHp + (newChp >> 16));

        // check for overflow
        if (m.CurrentHp < 0 && oldChp > 0)
        {
            m.CurrentHp = short.MaxValue;
        }

        // mod 65536
        var newChpFraction = (newChp & 0xFFFF) + m.CurrentHpFraction;

        if (newChpFraction >= 0x10000)
        {
            m.CurrentHpFraction = (ushort)(newChpFraction - 0x10000);
            m.CurrentHp++;
        }
        else
        {
            m.CurrentHpFraction = (ushort)newChpFraction;
        }

        // must set frac to zero even if equal
        if (m.CurrentHp >= m.MaxHp)
        {
            m.CurrentHp = m.MaxHp;
            m.CurrentHpFraction = 0;
        }

        if (oldChp != m.CurrentHp)
        {
            w.Ui?.PrintCharacterCurrentHitPoints();
        }
    }

    // src/game_run.cpp:2029 playerRegenerateMana() -RAK-
    private static void RegenerateMana(PlayerWorld w, int percent)
    {
        var m = w.Py.Misc;

        int oldCmana = m.CurrentMana;
        var newMana = m.Mana * percent + PlayerConfig.PlayerRegenMnBase;

        // div 65536
        m.CurrentMana = (short)(m.CurrentMana + (newMana >> 16));

        // check for overflow
        if (m.CurrentMana < 0 && oldCmana > 0)
        {
            m.CurrentMana = short.MaxValue;
        }

        // mod 65536
        var newManaFraction = (newMana & 0xFFFF) + m.CurrentManaFraction;

        if (newManaFraction >= 0x10000)
        {
            m.CurrentManaFraction = (ushort)(newManaFraction - 0x10000);
            m.CurrentMana++;
        }
        else
        {
            m.CurrentManaFraction = (ushort)newManaFraction;
        }

        // must set frac to zero even if equal
        if (m.CurrentMana >= m.Mana)
        {
            m.CurrentMana = m.Mana;
            m.CurrentManaFraction = 0;
        }

        if (oldCmana != m.CurrentMana)
        {
            w.Ui?.PrintCharacterCurrentMana();
        }
    }

    // src/game_run.cpp:473 playerUpdateBlindness()
    private static void UpdateBlindness(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.Blind <= 0)
        {
            return;
        }

        if ((f.Status & PlayerConfig.Status.PyBlind) == 0)
        {
            f.Status |= PlayerConfig.Status.PyBlind;

            ScreenRendering.DrawDungeonPanel(w.Dg);
            w.Ui?.PrintCharacterBlindStatus();
            PlayerCore.Disturb(w, 0, 1);

            // unlight creatures
            PlayerCore.UpdateMonstersWithoutMoving(w);
        }

        f.Blind--;

        if (f.Blind == 0)
        {
            f.Status &= ~PlayerConfig.Status.PyBlind;

            w.Ui?.PrintCharacterBlindStatus();
            ScreenRendering.DrawDungeonPanel(w.Dg);
            PlayerCore.Disturb(w, 0, 1);

            // light creatures
            PlayerCore.UpdateMonstersWithoutMoving(w);

            PlayerCore.PrintMessage(w, "The veil of darkness lifts.");
        }
    }

    // src/game_run.cpp:505 playerUpdateConfusion()
    private static void UpdateConfusion(PlayerWorld w)
    {
        var py = w.Py;
        var f = py.Flags;

        if (f.Confused <= 0)
        {
            return;
        }

        if ((f.Status & PlayerConfig.Status.PyConfused) == 0)
        {
            f.Status |= PlayerConfig.Status.PyConfused;
            w.Ui?.PrintCharacterConfusedState();
        }

        f.Confused--;

        if (f.Confused == 0)
        {
            f.Status &= ~PlayerConfig.Status.PyConfused;

            w.Ui?.PrintCharacterConfusedState();

            PlayerCore.PrintMessage(w, "You feel less confused now.");

            if (f.Rest != 0)
            {
                PlayerCore.RestOff(w);
            }
        }
    }

    // src/game_run.cpp:529 playerUpdateFearState()
    private static void UpdateFearState(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.Afraid <= 0)
        {
            return;
        }

        if ((f.Status & PlayerConfig.Status.PyFear) == 0)
        {
            if (f.SuperHeroism + f.Heroism > 0)
            {
                f.Afraid = 0;
            }
            else
            {
                f.Status |= PlayerConfig.Status.PyFear;
                w.Ui?.PrintCharacterFearState();
            }
        }
        else if (f.SuperHeroism + f.Heroism > 0)
        {
            f.Afraid = 1;
        }

        f.Afraid--;

        if (f.Afraid == 0)
        {
            f.Status &= ~PlayerConfig.Status.PyFear;

            w.Ui?.PrintCharacterFearState();
            PlayerCore.PrintMessage(w, "You feel bolder now.");
            PlayerCore.Disturb(w, 0, 0);
        }
    }

    // src/game_run.cpp:556 playerUpdatePoisonedState()
    private static void UpdatePoisonedState(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.Poisoned <= 0)
        {
            return;
        }

        if ((f.Status & PlayerConfig.Status.PyPoisoned) == 0)
        {
            f.Status |= PlayerConfig.Status.PyPoisoned;
            w.Ui?.PrintCharacterPoisonedState();
        }

        f.Poisoned--;

        if (f.Poisoned == 0)
        {
            f.Status &= ~PlayerConfig.Status.PyPoisoned;

            w.Ui?.PrintCharacterPoisonedState();
            PlayerCore.PrintMessage(w, "You feel better.");
            PlayerCore.Disturb(w, 0, 0);

            return;
        }

        var turn = w.Dg.GameTurn;
        var damage = PlayerStatLogic.StatAdjustmentConstitution(w.Py) switch
        {
            -4 => 4,
            -3 or -2 => 3,
            -1 => 2,
            0 => 1,
            1 or 2 or 3 => turn % 2 == 0 ? 1 : 0,
            4 or 5 => turn % 3 == 0 ? 1 : 0,
            6 => turn % 4 == 0 ? 1 : 0,
            _ => 0,
        };

        PlayerCore.TakesHit(w, damage, "poison");
        PlayerCore.Disturb(w, 1, 0);
    }

    // src/game_run.cpp:615 playerUpdateFastness()
    private static void UpdateFastness(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.Fast <= 0)
        {
            return;
        }

        if ((f.Status & PlayerConfig.Status.PyFast) == 0)
        {
            f.Status |= PlayerConfig.Status.PyFast;
            PlayerCore.ChangeSpeed(w, -1);

            PlayerCore.PrintMessage(w, "You feel yourself moving faster.");
            PlayerCore.Disturb(w, 0, 0);
        }

        f.Fast--;

        if (f.Fast == 0)
        {
            f.Status &= ~PlayerConfig.Status.PyFast;
            PlayerCore.ChangeSpeed(w, 1);

            PlayerCore.PrintMessage(w, "You feel yourself slow down.");
            PlayerCore.Disturb(w, 0, 0);
        }
    }

    // src/game_run.cpp:639 playerUpdateSlowness()
    private static void UpdateSlowness(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.Slow <= 0)
        {
            return;
        }

        if ((f.Status & PlayerConfig.Status.PySlow) == 0)
        {
            f.Status |= PlayerConfig.Status.PySlow;
            PlayerCore.ChangeSpeed(w, 1);

            PlayerCore.PrintMessage(w, "You feel yourself moving slower.");
            PlayerCore.Disturb(w, 0, 0);
        }

        f.Slow--;

        if (f.Slow == 0)
        {
            f.Status &= ~PlayerConfig.Status.PySlow;
            PlayerCore.ChangeSpeed(w, -1);

            PlayerCore.PrintMessage(w, "You feel yourself speed up.");
            PlayerCore.Disturb(w, 0, 0);
        }
    }

    // src/game_run.cpp:663 playerUpdateSpeed()
    private static void UpdateSpeed(PlayerWorld w)
    {
        UpdateFastness(w);
        UpdateSlowness(w);
    }

    // src/game_run.cpp:669 playerUpdateRestingState() -- resting is over?
    private static void UpdateRestingState(PlayerWorld w)
    {
        var py = w.Py;
        var f = py.Flags;

        if (f.Rest > 0)
        {
            f.Rest--;

            // Resting over
            if (f.Rest == 0)
            {
                PlayerCore.RestOff(w);
            }
        }
        else if (f.Rest < 0)
        {
            // Rest until reach max mana and max hit points.
            f.Rest++;

            if ((py.Misc.CurrentHp == py.Misc.MaxHp && py.Misc.CurrentMana == py.Misc.Mana) || f.Rest == 0)
            {
                PlayerCore.RestOff(w);
            }
        }
    }

    // src/game_run.cpp:688 playerUpdateHallucination() -- hallucinating?
    // (Random characters appear!)
    private static void UpdateHallucination(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.Image <= 0)
        {
            return;
        }

        PlayerCore.EndRunning(w);

        f.Image--;

        if (f.Image == 0)
        {
            // Used to draw entire screen! -CJS-
            ScreenRendering.DrawDungeonPanel(w.Dg);
        }
    }

    // src/game_run.cpp:703 playerUpdateParalysis()
    private static void UpdateParalysis(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.Paralysis <= 0)
        {
            return;
        }

        // when paralysis true, you can not see any movement that occurs
        f.Paralysis--;

        PlayerCore.Disturb(w, 1, 0);
    }

    // src/game_run.cpp:715 playerUpdateEvilProtection() -- protection from
    // evil counter
    private static void UpdateEvilProtection(PlayerWorld w)
    {
        var py = w.Py;
        var f = py.Flags;

        if (f.ProtectEvil <= 0)
        {
            return;
        }

        f.ProtectEvil--;

        if (f.ProtectEvil == 0)
        {
            PlayerCore.PrintMessage(w, "You no longer feel safe from evil.");
        }
    }

    // src/game_run.cpp:727 playerUpdateInvulnerability()
    private static void UpdateInvulnerability(PlayerWorld w)
    {
        var f = w.Py.Flags;
        var m = w.Py.Misc;

        if (f.Invulnerability <= 0)
        {
            return;
        }

        if ((f.Status & PlayerConfig.Status.PyInvuln) == 0)
        {
            f.Status |= PlayerConfig.Status.PyInvuln;
            PlayerCore.Disturb(w, 0, 0);

            m.Ac += 100;
            m.DisplayAc += 100;

            w.Ui?.PrintCharacterCurrentArmorClass();
            PlayerCore.PrintMessage(w, "Your skin turns into steel!");
        }

        f.Invulnerability--;

        if (f.Invulnerability == 0)
        {
            f.Status &= ~PlayerConfig.Status.PyInvuln;
            PlayerCore.Disturb(w, 0, 0);

            m.Ac -= 100;
            m.DisplayAc -= 100;

            w.Ui?.PrintCharacterCurrentArmorClass();
            PlayerCore.PrintMessage(w, "Your skin returns to normal.");
        }
    }

    // src/game_run.cpp:757 playerUpdateBlessedness()
    private static void UpdateBlessedness(PlayerWorld w)
    {
        var f = w.Py.Flags;
        var m = w.Py.Misc;

        if (f.Blessed <= 0)
        {
            return;
        }

        if ((f.Status & PlayerConfig.Status.PyBlessed) == 0)
        {
            f.Status |= PlayerConfig.Status.PyBlessed;
            PlayerCore.Disturb(w, 0, 0);

            m.Bth += 5;
            m.BthWithBows += 5;
            m.Ac += 2;
            m.DisplayAc += 2;

            PlayerCore.PrintMessage(w, "You feel righteous!");
            w.Ui?.PrintCharacterCurrentArmorClass();
        }

        f.Blessed--;

        if (f.Blessed == 0)
        {
            f.Status &= ~PlayerConfig.Status.PyBlessed;
            PlayerCore.Disturb(w, 0, 0);

            m.Bth -= 5;
            m.BthWithBows -= 5;
            m.Ac -= 2;
            m.DisplayAc -= 2;

            PlayerCore.PrintMessage(w, "The prayer has expired.");
            w.Ui?.PrintCharacterCurrentArmorClass();
        }
    }

    // src/game_run.cpp:792 playerUpdateHeatResistance() -- Resist Heat
    private static void UpdateHeatResistance(PlayerWorld w)
    {
        var py = w.Py;
        var f = py.Flags;

        if (f.HeatResistance <= 0)
        {
            return;
        }

        f.HeatResistance--;

        if (f.HeatResistance == 0)
        {
            PlayerCore.PrintMessage(w, "You no longer feel safe from flame.");
        }
    }

    // src/game_run.cpp:804 playerUpdateColdResistance()
    private static void UpdateColdResistance(PlayerWorld w)
    {
        var py = w.Py;
        var f = py.Flags;

        if (f.ColdResistance <= 0)
        {
            return;
        }

        f.ColdResistance--;

        if (f.ColdResistance == 0)
        {
            PlayerCore.PrintMessage(w, "You no longer feel safe from cold.");
        }
    }

    // src/game_run.cpp:816 playerUpdateDetectInvisible()
    private static void UpdateDetectInvisible(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.DetectInvisible <= 0)
        {
            return;
        }

        if ((f.Status & PlayerConfig.Status.PyDetInv) == 0)
        {
            f.Status |= PlayerConfig.Status.PyDetInv;
            f.SeeInvisible = true;

            // light but don't move creatures
            PlayerCore.UpdateMonstersWithoutMoving(w);
        }

        f.DetectInvisible--;

        if (f.DetectInvisible == 0)
        {
            f.Status &= ~PlayerConfig.Status.PyDetInv;

            // may still be able to see_invisible if wearing magic item
            PlayerBonuses.RecalculateBonuses(w.Py);

            // unlight but don't move creatures
            PlayerCore.UpdateMonstersWithoutMoving(w);
        }
    }

    // src/game_run.cpp:843 playerUpdateInfraVision() -- timed infra-vision
    private static void UpdateInfraVision(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.TimedInfra <= 0)
        {
            return;
        }

        if ((f.Status & PlayerConfig.Status.PyTimInfra) == 0)
        {
            f.Status |= PlayerConfig.Status.PyTimInfra;
            f.SeeInfra++;

            // light but don't move creatures
            PlayerCore.UpdateMonstersWithoutMoving(w);
        }

        f.TimedInfra--;

        if (f.TimedInfra == 0)
        {
            f.Status &= ~PlayerConfig.Status.PyTimInfra;
            f.SeeInfra--;

            // unlight but don't move creatures
            PlayerCore.UpdateMonstersWithoutMoving(w);
        }
    }

    // src/game_run.cpp:868 playerUpdateWordOfRecall() -- Note:
    // Word-of-Recall is a delayed action. Only sets the level and
    // dg.generate_new_level; the actual regeneration is playDungeon()'s
    // caller's job (M16).
    private static void UpdateWordOfRecall(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.WordOfRecall <= 0)
        {
            return;
        }

        if (f.WordOfRecall == 1)
        {
            w.Dg.GenerateNewLevel = true;

            f.Paralysis++;
            f.WordOfRecall = 0;

            if (w.Dg.CurrentLevel > 0)
            {
                w.Dg.CurrentLevel = 0;
                PlayerCore.PrintMessage(w, "You feel yourself yanked upwards!");
            }
            else if (w.Py.Misc.MaxDungeonDepth != 0)
            {
                w.Dg.CurrentLevel = w.Py.Misc.MaxDungeonDepth;
                PlayerCore.PrintMessage(w, "You feel yourself yanked downwards!");
            }
        }
        else
        {
            f.WordOfRecall--;
        }
    }

    // src/game_run.cpp:891 playerUpdateStatusFlags() -- the bit clearing,
    // plus printCharacterMovementState()'s PY_REPEAT upkeep; the other
    // printCharacter*() redraws are display only.
    private static void UpdateStatusFlags(PlayerWorld w)
    {
        var py = w.Py;
        var f = py.Flags;

        if ((f.Status & PlayerConfig.Status.PySpeed) != 0)
        {
            f.Status &= ~PlayerConfig.Status.PySpeed;
            w.Ui?.PrintCharacterSpeed();
        }

        if ((f.Status & PlayerConfig.Status.PyParalysed) != 0 && f.Paralysis < 1)
        {
            PlayerCore.UpdateMovementStateFlags(w);
            f.Status &= ~PlayerConfig.Status.PyParalysed;
        }
        else if (f.Paralysis > 0)
        {
            PlayerCore.UpdateMovementStateFlags(w);
            f.Status |= PlayerConfig.Status.PyParalysed;
        }
        else if (f.Rest != 0)
        {
            PlayerCore.UpdateMovementStateFlags(w);
        }

        if ((f.Status & PlayerConfig.Status.PyArmor) != 0)
        {
            w.Ui?.PrintCharacterCurrentArmorClass();
            f.Status &= ~PlayerConfig.Status.PyArmor;
        }

        if ((f.Status & PlayerConfig.Status.PyStats) != 0)
        {
            for (var n = 0; n < 6; n++)
            {
                if (((PlayerConfig.Status.PyStr << n) & f.Status) != 0)
                {
                    w.Ui?.DisplayCharacterStats(n);
                }
            }

            f.Status &= ~PlayerConfig.Status.PyStats;
        }

        if ((f.Status & PlayerConfig.Status.PyHp) != 0)
        {
            w.Ui?.PrintCharacterMaxHitPoints();
            w.Ui?.PrintCharacterCurrentHitPoints();
            f.Status &= ~PlayerConfig.Status.PyHp;
        }

        if ((f.Status & PlayerConfig.Status.PyMana) != 0)
        {
            w.Ui?.PrintCharacterCurrentMana();
            f.Status &= ~PlayerConfig.Status.PyMana;
        }
    }

    // src/game_run.cpp:935 playerDetectEnchantment() -- allow for a slim
    // chance of detect enchantment -CJS-
    private static void DetectEnchantment(PlayerWorld w)
    {
        var py = w.Py;

        for (var i = 0; i < PlayerConfig.PlayerInventorySize; i++)
        {
            if (i == py.Pack.UniqueItems)
            {
                i = 22;
            }

            ref var item = ref py.Inventory[i];

            // if in inventory, succeed 1 out of 50 times,
            // if in equipment list, success 1 out of 10 times
            var chance = i < 22 ? 50 : 10;

            if (item.CategoryId != TreasureCategory.TvNothing && ItemEnchanted(item) && RandomNumber.Next(chance) == 1)
            {
                PlayerCore.Disturb(w, 0, 0);

                // (message: "There's something about what you are %s...",
                // filled from ui_inventory.cpp's
                // playerItemWearingDescription(i) -- display only, so not
                // built here)

                // itemAppendToInscription()
                item.Identification |= IdentificationConfig.IdMagik;
            }
        }
    }

    // src/game_run.cpp:2064 itemEnchanted() -- is an item an enchanted
    // weapon or armor and we don't know? -CJS- only returns true if it is a
    // good enchantment
    private static bool ItemEnchanted(in Inventory item)
    {
        if (item.CategoryId < TreasureCategory.TvMinEnchant || item.CategoryId > TreasureCategory.TvMaxEnchant || PlayerInventory.ItemIsCursed(item))
        {
            return false;
        }

        if (ObjectIdentification.SpellItemIdentified(item))
        {
            return false;
        }

        if ((item.Identification & IdentificationConfig.IdMagik) != 0)
        {
            return false;
        }

        if (item.ToHit > 0 || item.ToDamage > 0 || item.ToAc > 0)
        {
            return true;
        }

        if ((0x4000107fu & item.Flags) != 0 && item.MiscUse > 0)
        {
            return true;
        }

        return (0x07ffe980u & item.Flags) != 0;
    }
}
