using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Ui;

// The screens of three free commands the main loop dispatches (M15):
//   - look() (src/dungeon_los.cpp) -- looks at everything in a cone of
//     vision, one ---pause--- at a time;
//   - identifyGameObject() (src/identification.cpp) -- what a map symbol
//     is; its table is extracted mechanically from objectDescription()'s
//     switch, not retyped;
//   - setGameOptions() (src/game.cpp) -- the '=' options screen.
//
// Monster recall (recall.cpp: memoryRecall(), the screen behind look's
// "[(r)ecall]" and '/'s "You recall those details?") is out of the
// roadmap's scope. Both prompts are kept as the original asks them; the
// recall screen itself is a one-line placeholder that waits for a key.
public sealed partial class GameUi
{
    // ---- look() ---------------------------------------------------------

    // src/dungeon_los.cpp:227 -- the look state, file statics there.
    //   dungeon x = py.pos.x + fxx * (ray x) + fxy * (ray y)
    //   dungeon y = py.pos.y + fyx * (ray x) + fyy * (ray y)
    private int _losFxx, _losFxy, _losFyx, _losFyy;
    private int _losNumPlacesSeen;
    private bool _losHackNoQuery;
    private int _losRocksAndObjects;

    // Transformation matrices for the look ray directions (index 0 unused).
    private static readonly int[] LosDirSetFxy = [0, 1, 0, 0, -1];
    private static readonly int[] LosDirSetFxx = [0, 0, -1, 1, 0];
    private static readonly int[] LosDirSetFyy = [0, 0, 1, -1, 0];
    private static readonly int[] LosDirSetFyx = [0, 1, 0, 0, -1];

    // Map diagonals into dir_set index.
    private static readonly int[] LosMapDiagonals1 = [1, 3, 0, 2, 4];
    private static readonly int[] LosMapDiagonals2 = [2, 1, 0, 4, 3];

    // Any sufficiently big number will do
    private const int Gradf = 10000;

    // src/identification.h MORIA_OBJ_DESC_SIZE -- an obj_desc_t holds 159
    // characters
    private const int ObjDescSize = 160;

    // src/dungeon_los.cpp:258 look() -- prompts for a direction, and then
    // looks at every object in turn within a cone of vision in that
    // direction. For each object, the cursor is moved over the object, a
    // description is given, and we wait for the user to type something.
    // Typing ESCAPE will abort the entire look.
    //
    // Looks first at real objects and monsters, and looks at rock types
    // only after all other things have been seen. Only looks at rock types
    // if the highlight_seams option is set.
    public void Look()
    {
        var py = W.Py;

        if (py.Flags.Blind > 0)
        {
            PlayerCore.PrintMessage(W, "You can't see a damn thing!");
            return;
        }

        if (py.Flags.Image > 0)
        {
            PlayerCore.PrintMessage(W, "You can't believe what you are seeing! It's like a dream!");
            return;
        }

        if (!PlayerCore.GetAllDirections(W, "Look which direction?", out var dir))
        {
            return;
        }

        _losNumPlacesSeen = 0;
        _losRocksAndObjects = 0;

        // Have to set this up for the lookSee
        _losHackNoQuery = false;

        if (LookSee(0, 0, out _))
        {
            return;
        }

        bool abort;
        do
        {
            abort = false;
            if (dir == 5)
            {
                for (var i = 1; i <= 4; i++)
                {
                    _losFxx = LosDirSetFxx[i];
                    _losFyx = LosDirSetFyx[i];
                    _losFxy = LosDirSetFxy[i];
                    _losFyy = LosDirSetFyy[i];
                    if (LookRay(0, 2 * Gradf - 1, 1))
                    {
                        abort = true;
                        break;
                    }
                    _losFxy = -_losFxy;
                    _losFyy = -_losFyy;
                    if (LookRay(0, 2 * Gradf, 2))
                    {
                        abort = true;
                        break;
                    }
                }
            }
            else if ((dir & 1) == 0)
            {
                // Straight directions

                var i = dir >> 1;
                _losFxx = LosDirSetFxx[i];
                _losFyx = LosDirSetFyx[i];
                _losFxy = LosDirSetFxy[i];
                _losFyy = LosDirSetFyy[i];
                if (LookRay(0, Gradf, 1))
                {
                    abort = true;
                }
                else
                {
                    _losFxy = -_losFxy;
                    _losFyy = -_losFyy;
                    abort = LookRay(0, Gradf, 2);
                }
            }
            else
            {
                var i = LosMapDiagonals1[dir >> 1];
                _losFxx = LosDirSetFxx[i];
                _losFyx = LosDirSetFyx[i];
                _losFxy = -LosDirSetFxy[i];
                _losFyy = -LosDirSetFyy[i];
                if (LookRay(1, 2 * Gradf, Gradf))
                {
                    abort = true;
                }
                else
                {
                    i = LosMapDiagonals2[dir >> 1];
                    _losFxx = LosDirSetFxx[i];
                    _losFyx = LosDirSetFyx[i];
                    _losFxy = LosDirSetFxy[i];
                    _losFyy = LosDirSetFyy[i];
                    abort = LookRay(1, 2 * Gradf - 1, Gradf);
                }
            }

            _losRocksAndObjects++;
        } while (!abort && W.Game.Options.HighlightSeams && _losRocksAndObjects < 2);

        if (abort)
        {
            PlayerCore.PrintMessage(W, "--Aborting look--");
            return;
        }

        if (_losNumPlacesSeen != 0)
        {
            PlayerCore.PrintMessage(W, dir == 5 ? "That's all you see." : "That's all you see in that direction.");
        }
        else
        {
            PlayerCore.PrintMessage(W, dir == 5 ? "You see nothing of interest." : "You see nothing of interest in that direction.");
        }
    }

    // src/dungeon_los.cpp:372 lookRay() -- look at everything within a cone
    // of vision between two ray lines emanating from the player, and y or
    // more places away from the direct line of view. This is recursive.
    //
    // Rays are specified by gradients, y over x, multiplied by 2*GRADF.
    // This is ONLY called with gradients between 2*GRADF (45 degrees) and 1
    // (almost horizontal).
    private bool LookRay(int y, int from, int to)
    {
        // from is the larger angle of the ray, since we scan towards the
        // center line. If from is smaller, then the ray does not exist.
        if (from <= to || y > MonsterConfig.MonMaxSight)
        {
            return false;
        }

        // Find first visible location along this line. Minimum x such
        // that (2x-1)/x < from/GRADF <=> x > GRADF(2x-1)/from. This may
        // be called with y=0 whence x will be set to 0. Thus we need a
        // special fix.
        var x = Gradf * (2 * y - 1) / from + 1;
        if (x <= 0)
        {
            x = 1;
        }

        // Find last visible location along this line.
        // Maximum x such that (2x+1)/x > to/GRADF <=> x < GRADF(2x+1)/to
        var maxX = (Gradf * (2 * y + 1) - 1) / to;
        if (maxX > MonsterConfig.MonMaxSight)
        {
            maxX = MonsterConfig.MonMaxSight;
        }
        if (maxX < x)
        {
            return false;
        }

        // los_hack_no_query is a HACK to prevent doubling up on direct lines of
        // sight. If 'to' is  greater than 1, we do not really look at
        // stuff along the direct line of sight, but we do have to see
        // what is opaque for the purposes of obscuring other objects.
        _losHackNoQuery = (y == 0 && to > 1) || (y == x && from < Gradf * 2);

        if (LookSee(y, x, out var transparent))
        {
            return true;
        }

        if (y == x)
        {
            _losHackNoQuery = false;
        }

        // (the original jumps into the second loop below when the first
        // place is transparent: `goto init_transparent`)
        var initTransparent = transparent;

        while (true)
        {
            if (!initTransparent)
            {
                // Look down the window we've found.
                if (LookRay(y + 1, from, (2 * y + 1) * Gradf / x))
                {
                    return true;
                }

                // Find the start of next window.
                do
                {
                    if (x == maxX)
                    {
                        return false;
                    }

                    // See if this seals off the scan. (If y is zero, then it will.)
                    from = (2 * y - 1) * Gradf / x;

                    if (from <= to)
                    {
                        return false;
                    }

                    x++;

                    if (LookSee(y, x, out transparent))
                    {
                        return true;
                    }
                } while (!transparent);
            }

            initTransparent = false;

            // init_transparent:

            // Find the end of this window of visibility.
            do
            {
                if (x == maxX)
                {
                    // The window is trimmed by an earlier limit.
                    return LookRay(y + 1, from, to);
                }

                x++;

                if (LookSee(y, x, out transparent))
                {
                    return true;
                }
            } while (transparent);
        }
    }

    // src/dungeon_los.cpp:470 lookSee() -- the place (rayY, rayX) along
    // the current ray; true aborts the look (<ESCAPE> at a ---pause---).
    private bool LookSee(int rayY, int rayX, out bool transparent)
    {
        var py = W.Py;

        if (rayX < 0 || rayY < 0 || rayY > rayX)
        {
            PlayerCore.PrintMessage(W, $"Illegal call to lookSee({rayY}, {rayX})");
        }

        var description = rayX == 0 && rayY == 0 ? "You are on" : "You see";

        var x = py.Pos.X + _losFxx * rayX + _losFxy * rayY;
        var y = py.Pos.Y + _losFyx * rayX + _losFyy * rayY;
        var coord = new Coord(y, x);

        if (!CoordInsidePanel(coord))
        {
            transparent = false;
            return false;
        }

        var tile = W.Dg.Floor[coord.Y, coord.X];
        transparent = tile.FeatureId <= TileConstants.MaxOpenSpace;

        if (_losHackNoQuery)
        {
            return false; // Don't look at a direct line of sight. A hack.
        }

        var key = Escape;
        var msg = "";

        if (_losRocksAndObjects == 0 && tile.CreatureId > 1 && W.Monsters.Monsters[tile.CreatureId].Lit)
        {
            var j = W.Monsters.Monsters[tile.CreatureId].CreatureId;
            var name = DataCreaturesData.CreaturesList[j].Name;
            msg = Helpers.Truncate($"{description} {(Helpers.IsVowel(name[0]) ? "an" : "a")} {name}. [(r)ecall]", ObjDescSize);
            description = "It is on";
            PutStringClearToEol(msg, 0, 0);

            PanelMoveCursor(coord.Y, coord.X);
            key = GetKeyInput();

            if (key == 'r' || key == 'R')
            {
                TerminalSaveScreen();
                key = MemoryRecall(j);
                TerminalRestoreScreen();
            }
        }

        if (tile.TemporaryLight || tile.PermanentLight || tile.FieldMark)
        {
            string? wallDescription = null;

            // `goto granite` -- a secret door looks like granite
            var granite = false;

            if (tile.TreasureId != 0)
            {
                var item = W.Treasure[tile.TreasureId];

                if (item.CategoryId == TreasureCategory.TvSecretDoor)
                {
                    granite = true;
                }
                else if (_losRocksAndObjects == 0 && item.CategoryId != TreasureCategory.TvInvisTrap)
                {
                    var objString = ItemDescription.Describe(W.Identification, item, true);

                    msg = Helpers.Truncate($"{description} {objString} ---pause---", ObjDescSize);
                    description = "It is in";
                    PutStringClearToEol(msg, 0, 0);

                    PanelMoveCursor(coord.Y, coord.X);
                    key = GetKeyInput();
                }
            }

            if (granite || ((_losRocksAndObjects != 0 || msg.Length != 0) && tile.FeatureId >= TileConstants.MinClosedSpace))
            {
                switch (granite ? TileConstants.TileGraniteWall : tile.FeatureId)
                {
                    case TileConstants.TileBoundaryWall:
                    case TileConstants.TileGraniteWall:
                        // Granite is only interesting if it contains something.
                        wallDescription = msg.Length != 0 ? "a granite wall" : null;
                        break;
                    case TileConstants.TileMagmaWall:
                        wallDescription = "some dark rock";
                        break;
                    case TileConstants.TileQuartzWall:
                        wallDescription = "a quartz vein";
                        break;
                }

                if (wallDescription is not null)
                {
                    msg = Helpers.Truncate($"{description} {wallDescription} ---pause---", ObjDescSize);
                    PutStringClearToEol(msg, 0, 0);
                    PanelMoveCursor(coord.Y, coord.X);
                    key = GetKeyInput();
                }
            }
        }

        if (msg.Length != 0)
        {
            _losNumPlacesSeen++;
            if (key == Escape)
            {
                return true;
            }
        }

        return false;
    }

    // ---- monster recall (out of scope) -----------------------------------

    // src/recall.cpp:56 memoryMonsterKnown() -- do we know anything about
    // this creature?
    private bool MemoryMonsterKnown(Recall memory)
    {
        if (W.Game.WizardMode)
        {
            return true;
        }

        if (memory.Movement != 0 || memory.Defenses != 0 || memory.Kills != 0 || memory.Spells != 0 || memory.Deaths != 0)
        {
            return true;
        }

        foreach (var attack in memory.Attacks)
        {
            if (attack != 0)
            {
                return true;
            }
        }

        return false;
    }

    // In place of src/recall.cpp:588 memoryRecall(): recall.cpp is out of
    // the roadmap's scope, so the recall screen is a placeholder that reads
    // a key, as the real one does, and returns it.
    private char MemoryRecall(int monsterId)
    {
        ClearScreen();
        PutString($"(Monster recall for {DataCreaturesData.CreaturesList[monsterId].Name} is not part of this port.)", 0, 0);
        PutString("--pause--", 1, 0);
        return GetKeyInput();
    }

    // src/recall.cpp:676 recallMonsterAttributes() -- the "You recall those
    // details?" prompt, then each known creature using `command`'s symbol.
    private void RecallMonsterAttributes(char command)
    {
        var n = 0;

        for (var i = MonsterConstants.MonMaxCreatures - 1; i >= 0; i--)
        {
            if (DataCreaturesData.CreaturesList[i].Sprite == command && MemoryMonsterKnown(W.CreatureRecall[i]))
            {
                if (n == 0)
                {
                    var confirmed = GetInputConfirmationWithAbort(40, "You recall those details?");
                    if (confirmed != 1)
                    {
                        break;
                    }

                    EraseLine(0, 40);
                    TerminalSaveScreen();
                }
                n++;

                var query = MemoryRecall(i);
                TerminalRestoreScreen();
                if (query == Escape)
                {
                    break;
                }
            }
        }
    }

    // ---- identifyGameObject() ----------------------------------------------

    // src/identification.cpp:218 identifyGameObject()
    public void IdentifyGameObject()
    {
        // being an ASCII character representing the item/monster tile, e.g. `+` = Door.
        if (!PlayerCore.GetCommand(W, "Enter character to be identified :", out var itemId))
        {
            return;
        }

        PutStringClearToEol(ObjectDescription(itemId), 0, 0);
        RecallMonsterAttributes(itemId);
    }

    // src/identification.cpp:16 objectDescription() -- every printing ASCII
    // character, in the order in which they appear in the ASCII character
    // set. Generated from the original's switch by
    // dotnet/tools/CppModelExtractor/extract_object_descriptions.py -- edit
    // that, not these rows.
    private string ObjectDescription(char command) =>
        command switch
        {
            ' ' => "  - An open pit.",
            '!' => "! - A potion.",
            '"' => "\" - An amulet, periapt, or necklace.",
            '#' => "# - A stone wall.",
            '$' => "$ - Treasure.",
            '%' => "% - Not used.",
            '&' => "& - Treasure chest.",
            '\'' => "' - An open door.",
            '(' => "( - Soft armor.",
            ')' => ") - A shield.",
            '*' => "* - Gems.",
            '+' => "+ - A closed door.",
            ',' => ", - Food or mushroom patch.",
            '-' => "- - A wand",
            '.' => ". - Floor.",
            '/' => "/ - A pole weapon.",
            '1' => "1 - Entrance to General Store.",
            '2' => "2 - Entrance to Armory.",
            '3' => "3 - Entrance to Weaponsmith.",
            '4' => "4 - Entrance to Temple.",
            '5' => "5 - Entrance to Alchemy shop.",
            '6' => "6 - Entrance to Magic-Users store.",
            ':' => ": - Rubble.",
            ';' => "; - A loose rock.",
            '<' => "< - An up staircase.",
            '=' => "= - A ring.",
            '>' => "> - A down staircase.",
            '?' => "? - A scroll.",
            '@' => W.Py.Misc.Name,
            'A' => "A - Giant Ant Lion.",
            'B' => "B - The Balrog.",
            'C' => "C - Gelatinous Cube.",
            'D' => "D - An Ancient Dragon (Beware).",
            'E' => "E - Elemental.",
            'F' => "F - Giant Fly.",
            'G' => "G - Ghost.",
            'H' => "H - Hobgoblin.",
            'J' => "J - Jelly.",
            'K' => "K - Killer Beetle.",
            'L' => "L - Lich.",
            'M' => "M - Mummy.",
            'O' => "O - Ooze.",
            'P' => "P - Giant humanoid.",
            'Q' => "Q - Quylthulg (Pulsing Flesh Mound).",
            'R' => "R - Reptile.",
            'S' => "S - Giant Scorpion.",
            'T' => "T - Troll.",
            'U' => "U - Umber Hulk.",
            'V' => "V - Vampire.",
            'W' => "W - Wight or Wraith.",
            'X' => "X - Xorn.",
            'Y' => "Y - Yeti.",
            '[' => "[ - Hard armor.",
            '\\' => "\\ - A hafted weapon.",
            ']' => "] - Misc. armor.",
            '^' => "^ - A trap.",
            '_' => "_ - A staff.",
            'a' => "a - Giant Ant.",
            'b' => "b - Giant Bat.",
            'c' => "c - Giant Centipede.",
            'd' => "d - Dragon.",
            'e' => "e - Floating Eye.",
            'f' => "f - Giant Frog.",
            'g' => "g - Golem.",
            'h' => "h - Harpy.",
            'i' => "i - Icky Thing.",
            'j' => "j - Jackal.",
            'k' => "k - Kobold.",
            'l' => "l - Giant Louse.",
            'm' => "m - Mold.",
            'n' => "n - Naga.",
            'o' => "o - Orc or Ogre.",
            'p' => "p - Person (Humanoid).",
            'q' => "q - Quasit.",
            'r' => "r - Rodent.",
            's' => "s - Skeleton.",
            't' => "t - Giant Tick.",
            'w' => "w - Worm or Worm Mass.",
            'y' => "y - Yeek.",
            'z' => "z - Zombie.",
            '{' => "{ - Arrow, bolt, or bullet.",
            '|' => "| - A sword or dagger.",
            '}' => "} - Bow, crossbow, or sling.",
            '~' => "~ - Miscellaneous item.",
            _ => "Not Used.",
        };

    // ---- setGameOptions() ---------------------------------------------------

    // src/game.cpp:115 game_options[] -- the '=' screen's rows
    private static readonly (string Prompt, Func<GameOptions, bool> Get, Action<GameOptions, bool> Set)[] OptionRows =
    [
        ("Running: cut known corners", o => o.RunCutCorners, (o, v) => o.RunCutCorners = v),
        ("Running: examine potential corners", o => o.RunExamineCorners, (o, v) => o.RunExamineCorners = v),
        ("Running: print self during run", o => o.RunPrintSelf, (o, v) => o.RunPrintSelf = v),
        ("Running: stop when map sector changes", o => o.FindBound, (o, v) => o.FindBound = v),
        ("Running: run through open doors", o => o.RunIgnoreDoors, (o, v) => o.RunIgnoreDoors = v),
        ("Prompt to pick up objects", o => o.PromptToPickup, (o, v) => o.PromptToPickup = v),
        ("Rogue like commands", o => o.UseRoguelikeKeys, (o, v) => o.UseRoguelikeKeys = v),
        ("Show weights in inventory", o => o.ShowInventoryWeights, (o, v) => o.ShowInventoryWeights = v),
        ("Highlight and notice mineral seams", o => o.HighlightSeams, (o, v) => o.HighlightSeams = v),
        ("Beep for invalid character", o => o.ErrorBeepSound, (o, v) => o.ErrorBeepSound = v),
        ("Display rest/repeat counts", o => o.DisplayCounts, (o, v) => o.DisplayCounts = v),
    ];

    // src/game.cpp:137 setGameOptions() -- set or unset various boolean
    // options -CJS-
    public void SetGameOptions()
    {
        var options = W.Game.Options;

        PutStringClearToEol("  ESC when finished, y/n to set options, <return> or - to move cursor", 0, 0);

        var max = OptionRows.Length;
        for (var i = 0; i < max; i++)
        {
            PutStringClearToEol($"{OptionRows[i].Prompt,-38}: {(OptionRows[i].Get(options) ? "yes" : "no ")}", i + 1, 0);
        }
        EraseLine(max + 1, 0);

        var optionId = 0;
        while (true)
        {
            MoveCursor(optionId + 1, 40);

            switch (GetKeyInput())
            {
                case Escape:
                    return;
                case '-':
                    optionId = optionId > 0 ? optionId - 1 : max - 1;
                    break;
                case ' ':
                case '\n':
                case '\r':
                    optionId = optionId + 1 < max ? optionId + 1 : 0;
                    break;
                case 'y':
                case 'Y':
                    PutString("yes", optionId + 1, 40);

                    OptionRows[optionId].Set(options, true);

                    optionId = optionId + 1 < max ? optionId + 1 : 0;
                    break;
                case 'n':
                case 'N':
                    PutString("no ", optionId + 1, 40);

                    OptionRows[optionId].Set(options, false);

                    optionId = optionId + 1 < max ? optionId + 1 : 0;
                    break;
                default:
                    TerminalBellSound();
                    break;
            }
        }
    }
}
