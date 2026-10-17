using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Character;

// Small, widely-called player state helpers from src/player.cpp (and
// src/player_run.cpp's playerEndRunning()), ported in M7 because the status
// effects reach them: playerDisturb(), playerSearchOn/Off(),
// playerRestOff(), playerChangeSpeed(), playerTakesHit(),
// playerCarryingLoadLimit(), playerStrength().
//
// Display calls (printCharacter*()) are omitted, except for the two
// "display" routines that also change game state: printMessage() (zeroes
// game.command_count -- see PrintMessage below) and
// printCharacterMovementState() (maintains PY_REPEAT -- see
// UpdateMovementStateFlags). flushInputBuffer() is UI-only.
//
// M8 added the helpers player actions share: the no-interrupt message
// variant, objectBlockedByMonster(), getDirectionWithMemory()'s prev_dir
// memory, and a fourth state-mutating "display" routine,
// displayCharacterExperience() -- which is where level gain happens.
public static class PlayerCore
{
    // src/player.cpp:148 playerDisturb() -- something happens to disturb
    // the player. -CJS- The first arg indicates a major disturbance, which
    // affects search. The second arg indicates a light change.
    public static void Disturb(PlayerWorld w, int majorDisturbance, int lightDisturbance)
    {
        var py = w.Py;

        w.Game.CommandCount = 0;

        if (majorDisturbance != 0 && (py.Flags.Status & PlayerConfig.Status.PySearch) != 0)
        {
            SearchOff(w);
        }

        if (py.Flags.Rest != 0)
        {
            RestOff(w);
        }

        if (lightDisturbance != 0 || py.RunningTracker != 0)
        {
            py.RunningTracker = 0;
            DungeonLighting.ResetView(py, w.Dg, w.Treasure, w.Game.Options);
        }

        // (flushInputBuffer() -- UI)
    }

    // src/player.cpp:168 playerSearchOn() -- Search Mode enhancement -RAK-
    public static void SearchOn(PlayerWorld w)
    {
        var f = w.Py.Flags;

        ChangeSpeed(w, 1);

        f.Status |= PlayerConfig.Status.PySearch;

        UpdateMovementStateFlags(w);

        f.FoodDigested++;
    }

    // src/player.cpp:179 playerSearchOff()
    public static void SearchOff(PlayerWorld w)
    {
        var f = w.Py.Flags;

        DungeonLighting.ResetView(w.Py, w.Dg, w.Treasure, w.Game.Options);
        ChangeSpeed(w, -1);

        f.Status &= ~PlayerConfig.Status.PySearch;

        UpdateMovementStateFlags(w);

        f.FoodDigested--;
    }

    // src/player.cpp:239 playerRestOff()
    public static void RestOff(PlayerWorld w)
    {
        var f = w.Py.Flags;

        f.Rest = 0;
        f.Status &= ~PlayerConfig.Status.PyRest;

        UpdateMovementStateFlags(w);

        // (printMessage(CNIL) -- flushes the "press any key" message line;
        // a null message leaves game.command_count alone)

        f.FoodDigested++;
    }

    // src/ui_io.cpp:245 printMessage() -- the game-state half: every
    // non-null message zeroes game.command_count (cancelling a repeated
    // command). The display half (message line, -more- prompt, history)
    // is ScreenRendering.PrintMessage's.
    public static void PrintMessage(PlayerWorld w, string message)
    {
        w.Game.CommandCount = 0;
        ScreenRendering.PrintMessage(message);
    }

    // src/ui_io.cpp:316 printMessageNoCommandInterrupt() -- saves and
    // restores game.command_count around printMessage(), so its net effect
    // on game state is nil (M8).
    public static void PrintMessageNoCommandInterrupt(PlayerWorld w, string message)
    {
        var count = w.Game.CommandCount;

        PrintMessage(w, message);

        w.Game.CommandCount = count;
    }

    // src/identification.cpp:941 objectBlockedByMonster() -- "The <monster>
    // is in your way!" (M8)
    public static void ObjectBlockedByMonster(PlayerWorld w, int monsterId)
    {
        var monster = w.Monsters.Monsters[monsterId];
        var name = DataCreaturesData.CreaturesList[monster.CreatureId].Name;

        var description = monster.Lit ? $"The {name}" : "Something";

        PrintMessage(w, $"{description} is in your way!");
    }

    // src/game.cpp:262 getDirectionWithMemory() -- the game-state half:
    // the direction the player typed is remembered in py.prev_dir for
    // repeated (counted) commands. Reading the key is input (M14). (M8)
    public static void RememberDirection(PlayerWorld w, int direction)
    {
        w.Py.PrevDir = (char)direction;
    }

    // src/ui.cpp:728 playerGainLevel() -- increases hit points and level
    // -RAK- (M8)
    private static void GainLevel(PlayerWorld w)
    {
        var py = w.Py;

        py.Misc.Level++;

        PrintMessage(w, $"Welcome to level {py.Misc.Level}.");

        PlayerStatLogic.CalculateHitPoints(py);

        var newExp = (int)(py.BaseExpLevels[py.Misc.Level - 1] * py.Misc.ExperienceFactor / 100);

        if (py.Misc.Exp > newExp)
        {
            // lose some of the 'extra' exp when gaining several levels at once
            var difExp = py.Misc.Exp - newExp;
            py.Misc.Exp = newExp + (difExp / 2);
        }

        // (printCharacterLevel(), printCharacterTitle() -- display)

        // (PlayerSpells' messages are display-only, but the "Welcome" message
        // above has already zeroed game.command_count, so that's exact.)
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
    }

    // src/ui.cpp:760 displayCharacterExperience() -- "prints experience
    // -RAK-", but it is also where the player gains levels (playerGainLevel())
    // and max_exp is kept up to date. Only the printLongNumber() is display.
    // (M8)
    public static void DisplayCharacterExperience(PlayerWorld w)
    {
        var m = w.Py.Misc;

        if (m.Exp > PlayerConfig.PlayerMaxExp)
        {
            m.Exp = PlayerConfig.PlayerMaxExp;
        }

        while (m.Level < PlayerConfig.PlayerMaxLevel && (int)(w.Py.BaseExpLevels[m.Level - 1] * m.ExperienceFactor / 100) <= m.Exp)
        {
            GainLevel(w);
        }

        if (m.Exp > m.MaxExp)
        {
            m.MaxExp = m.Exp;
        }
    }

    // src/ui.cpp:306 printCharacterMovementState() -- the game-state half:
    // PY_REPEAT is cleared, then set again only when the status line would
    // show "Repeat" (not paralysed, not resting, a command count pending).
    public static void UpdateMovementStateFlags(PlayerWorld w)
    {
        var f = w.Py.Flags;

        f.Status &= ~PlayerConfig.Status.PyRepeat;

        if (f.Paralysis > 1)
        {
            return;
        }

        if ((f.Status & PlayerConfig.Status.PyRest) != 0)
        {
            return;
        }

        if (w.Game.CommandCount > 0)
        {
            f.Status |= PlayerConfig.Status.PyRepeat;
        }
    }

    // src/player_run.cpp:238 playerEndRunning() -- switch off the run flag
    // and get the light correct. -CJS-
    public static void EndRunning(PlayerWorld w)
    {
        DungeonLighting.EndRunning(w.Py, w.Dg, w.Treasure, w.Game.Options);
    }

    // src/player.cpp:370 playerChangeSpeed() -- changes speed of monsters
    // relative to player -RAK- Note: When the player is sped up or slowed
    // down, I simply change the speed of all the monsters. This greatly
    // simplified the logic.
    public static void ChangeSpeed(PlayerWorld w, int speed)
    {
        var f = w.Py.Flags;

        f.Speed = (short)(f.Speed + speed);
        f.Status |= PlayerConfig.Status.PySpeed;

        var monsters = w.Monsters.Monsters;
        for (var i = w.Monsters.NextFreeMonsterId - 1; i >= MonsterConfig.MonMinIndexId; i--)
        {
            monsters[i].Speed = (short)(monsters[i].Speed + speed);
        }
    }

    // src/player.cpp:642 playerTakesHit() -- decreases players hit points
    // and sets game.character_is_dead flag if necessary -RAK-
    public static void TakesHit(PlayerWorld w, int damage, string creatureNameLabel)
    {
        var py = w.Py;

        if (py.Flags.Invulnerability > 0)
        {
            damage = 0;
        }
        py.Misc.CurrentHp = (short)(py.Misc.CurrentHp - damage);

        if (py.Misc.CurrentHp >= 0)
        {
            return;
        }

        if (!w.Game.CharacterIsDead)
        {
            w.Game.CharacterIsDead = true;

            w.Game.CharacterDiedFrom = creatureNameLabel;

            w.Game.TotalWinner = false;
        }

        w.Dg.GenerateNewLevel = true;
    }

    // src/player.cpp:731 playerCarryingLoadLimit() -- computes current
    // weight limit -RAK-
    public static int CarryingLoadLimit(Player py)
    {
        var weightCap = py.Stats.Used[PlayerConfig.Attr.Str] * PlayerConfig.PlayerWeightCap + py.Misc.Weight;

        if (weightCap > 3000)
        {
            weightCap = 3000;
        }

        return weightCap;
    }

    // src/player.cpp:742 playerStrength() -- are we strong enough for the
    // current pack and weapon? -CJS-
    public static void Strength(PlayerWorld w)
    {
        var py = w.Py;
        var item = py.Inventory[PlayerConfig.Equipment.Wield];

        if (item.CategoryId != TreasureCategory.TvNothing && py.Stats.Used[PlayerConfig.Attr.Str] * 15 < item.Weight)
        {
            if (!py.WeaponIsHeavy)
            {
                PrintMessage(w, "You have trouble wielding such a heavy weapon.");
                py.WeaponIsHeavy = true;
                PlayerBonuses.RecalculateBonuses(py);
            }
        }
        else if (py.WeaponIsHeavy)
        {
            py.WeaponIsHeavy = false;
            if (item.CategoryId != TreasureCategory.TvNothing)
            {
                PrintMessage(w, "You are strong enough to wield your weapon.");
            }
            PlayerBonuses.RecalculateBonuses(py);
        }

        var limit = CarryingLoadLimit(py);

        if (limit < py.Pack.Weight)
        {
            limit = py.Pack.Weight / (limit + 1);
        }
        else
        {
            limit = 0;
        }

        if (py.Pack.Heaviness != limit)
        {
            if (py.Pack.Heaviness < limit)
            {
                PrintMessage(w, "Your pack is so heavy that it slows you down.");
            }
            else
            {
                PrintMessage(w, "You move more easily under the weight of your pack.");
            }
            ChangeSpeed(w, limit - py.Pack.Heaviness);
            py.Pack.Heaviness = (short)limit;
        }

        py.Flags.Status &= ~PlayerConfig.Status.PyStrWgt;
    }

    // src/monster.cpp:1313 updateMonsters(false) -- "light, but do not move
    // critters": the status effects call this after any change to what the
    // player can see. Per monster it updates distance/visibility via
    // monsterUpdateVisibility(), which is M9's (monster AI) to port. With
    // no live monsters (or a dead character) the original's loop body never
    // runs, so that case is exact; otherwise this throws rather than
    // silently skip a real state update.
    public static void UpdateMonstersWithoutMoving(PlayerWorld w)
    {
        if (w.Monsters.NextFreeMonsterId > MonsterConfig.MonMinIndexId && !w.Game.CharacterIsDead)
        {
            throw new NotSupportedException(
                "updateMonsters(false) with live monsters needs monsterUpdateVisibility() (M9), not yet ported.");
        }
    }
}
