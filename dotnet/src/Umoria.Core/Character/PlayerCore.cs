using Umoria.Core.Rng;
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
        w.MessageLog?.Invoke(message);
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

    // src/ui_io.cpp:378 getCommand() / getTileCharacter() / getMenuItemId()
    // -- one keypress for a prompt; false on <ESCAPE>. The game-state half
    // is getKeyInput()'s: it zeroes game.command_count ("just to be safe
    // -CJS-") on every key read. (M10)
    public static bool GetCommand(PlayerWorld w, string prompt, out char command)
    {
        w.Game.CommandCount = 0;

        var key = w.ReadCommand(prompt);
        command = key ?? (char)27;

        return key is not null;
    }

    // src/ui_io.cpp:466 getInputConfirmation() -- the yes/no prompt; like
    // every key read it zeroes game.command_count. (M10)
    public static bool GetInputConfirmation(PlayerWorld w, string prompt)
    {
        w.Game.CommandCount = 0;

        return w.Confirm(prompt);
    }

    // src/ui_inventory.cpp:1152 inventoryGetInputForItemId() -- the
    // game-state half: a range reaching into the equipment means "the whole
    // pack" (or the equipment list when the pack is empty), an empty choice
    // fails without a key being read, escaping sets game.player_free_turn,
    // and reading keys zeroes game.command_count. Which slot is chosen comes
    // from the PlayerWorld.SelectItem hook, given the effective range. (M10)
    public static bool GetItemId(PlayerWorld w, string prompt, int itemIdStart, int itemIdEnd, out int itemId)
    {
        return GetItemId(w, prompt, itemIdStart, itemIdEnd, null, null, out itemId);
    }

    // The same, with the original's `mask` and `message` arguments (the
    // store's sell command): a slot outside the range, or whose mask entry
    // is false, is refused -- `message` is printed (or the bell rung,
    // without one) and another choice read. (M13)
    public static bool GetItemId(PlayerWorld w, string prompt, int itemIdStart, int itemIdEnd, bool[]? mask, string? message, out int itemId)
    {
        var py = w.Py;
        itemId = 0;

        var packFull = false;
        if (itemIdEnd > PlayerConfig.Equipment.Wield)
        {
            packFull = true;
            itemIdEnd = py.Pack.UniqueItems == 0 ? py.EquipmentCount - 1 : py.Pack.UniqueItems - 1;
        }

        if (py.Pack.UniqueItems < 1 && (!packFull || py.EquipmentCount < 1))
        {
            w.ScreenWrite?.Invoke(0, 0, "You are not carrying anything.", true);
            return false;
        }

        // (choosing from the equipment list when the pack is empty maps the
        // letter to the n-th worn item; that menu is the UI's, M14)
        var inventoryMenu = !packFull || py.Pack.UniqueItems != 0;

        w.Game.CommandCount = 0;

        while (true)
        {
            // the prompt line, redrawn after a refused choice (M13: the
            // store screen's text goes through PlayerWorld.ScreenWrite; the
            // '*' list and '/' switch are the UI's, so this is the line
            // before either)
            if (w.ScreenWrite is not null)
            {
                var digits = inventoryMenu ? " 0-9," : "";
                var description = packFull
                    ? $"({(inventoryMenu ? "Inven" : "Equip")}: {(char)(itemIdStart + 'a')}-{(char)(itemIdEnd + 'a')},{digits} * to see, / for {(inventoryMenu ? "Equip" : "Inven")}, or ESC) {prompt}"
                    : $"(Items {(char)(itemIdStart + 'a')}-{(char)(itemIdEnd + 'a')},{digits} * for inventory list, ESC to exit) {prompt}";
                w.ScreenWrite(0, 0, Helpers.Truncate(description, Helpers.MoriaMessageSize), true);
            }

            var chosen = w.SelectItem(prompt, itemIdStart, itemIdEnd);
            if (chosen is null)
            {
                w.Game.PlayerFreeTurn = true;
                return false;
            }

            // (a slot outside the range is refused like a masked one -- the
            // original prints `message` for either)
            if (chosen.Value >= itemIdStart && chosen.Value <= itemIdEnd && (mask is null || mask[chosen.Value]))
            {
                itemId = chosen.Value;
                return true;
            }

            if (message is not null)
            {
                PrintMessage(w, message);
            }
            // (else terminalBellSound())
        }
    }

    // src/game.cpp:262 getDirectionWithMemory() -- prompts for a direction
    // -RAK-; direction memory added, for repeated commands. -CJS- (M10; M8
    // ported only the py.prev_dir half above, its commands take the
    // direction as a parameter)
    //
    // The roguelike-keys mapping (config::options::use_roguelike_keys,
    // default off) is key translation, left to the UI (M14).
    public static bool GetDirectionWithMemory(PlayerWorld w, string? prompt, out int direction)
    {
        // used in counted commands. -CJS-
        if (w.Game.UseLastDirection)
        {
            direction = w.Py.PrevDir;
            return true;
        }

        prompt ??= "Which direction?";

        while (true)
        {
            // Don't end a counted command. -CJS-
            var oldCount = w.Game.CommandCount;
            if (!GetCommand(w, prompt, out var command))
            {
                w.Game.PlayerFreeTurn = true;
                direction = 0;
                return false;
            }
            w.Game.CommandCount = oldCount;

            if (command >= '1' && command <= '9' && command != '5')
            {
                w.Py.PrevDir = (char)(command - '0');
                direction = w.Py.PrevDir;
                return true;
            }

            // (terminalBellSound() -- display)
        }
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
            PlayerSpells.CalculateAllowedSpellsCount(py, PlayerConfig.Attr.Int, w.Print);
            PlayerSpells.GainMana(py, PlayerConfig.Attr.Int);
        }
        else if (spellType == SpellConfig.SpellTypePriest)
        {
            PlayerSpells.CalculateAllowedSpellsCount(py, PlayerConfig.Attr.Wis, w.Print);
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

    // src/game.cpp:224 getRandomDirection() (moved here from PlayerDoors in
    // M11: wands and thrown objects pick one too when confused)
    public static int GetRandomDirection()
    {
        int dir;

        do
        {
            dir = RandomNumber.Next(9);
        } while (dir == 5);

        return dir;
    }

    // src/ui.cpp:103 drawCavePanel() -- the game-state half of redrawing the
    // whole screen (the blindness mushroom calls it). Through
    // printCharacterStatsBlock() it reaches printCharacterMovementState()
    // when searching or resting (PY_REPEAT upkeep) and always
    // printCharacterStudyInstruction(), which clears PY_STUDY. The rest is
    // display -- except that drawDungeonPanel() resolves every glyph through
    // caveGetTileSymbol(), which draws RNG while hallucinating: still the
    // open M14 decision. (M11)
    public static void DrawCavePanel(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if ((f.Status & (PlayerConfig.Status.PySearch | PlayerConfig.Status.PyRest)) != 0)
        {
            UpdateMovementStateFlags(w);
        }

        f.Status &= ~PlayerConfig.Status.PyStudy;
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
    // player can see. (Until M9 this threw with live monsters; it is now
    // Creatures.MonsterAi.UpdateMonsters.)
    public static void UpdateMonstersWithoutMoving(PlayerWorld w)
    {
        Creatures.MonsterAi.UpdateMonsters(w, false);
    }
}
