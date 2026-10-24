using System.Text.Json;
using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Stores;

namespace Umoria.Core.Persistence;

// What loadGame() came back with. The original returns a bool plus a
// `generate` out-parameter, and exits the program itself on a bad file.
public enum LoadOutcome
{
    // "Save file does not exist." -- start a new character.
    NoSaveFile,

    // Only the monster memories and options were restored (a dead
    // character): loadGame() returned false, so a new character is made.
    MemoryOnly,

    // A character was restored. Generate a level only when the result's
    // Generate is set (a wizard's resurrection, which has no level).
    Restored,

    // Bad or unreadable file. The original prints "Please try again
    // without that save file.", waits for a key and exits (exitProgram());
    // that is the caller's (M15) to do. The world is partly overwritten.
    Failed,
}

public readonly record struct LoadResult(LoadOutcome Outcome, bool Generate);

// src/game_save.cpp saveGame() / saveChar() / svWrite() / loadGame() over
// the JSON format of SaveFile. The game logic around the bytes is the
// original's: the pre-save cleanup, the file-creation rules (never
// overwrite a save file this game didn't load, unless a wizard says so),
// the dead-character and resurrection paths, and the post-restore fixups.
//
// One instance per game: it holds game_save.cpp's file statics (the save
// file name, from_save_file, start_time). The clock is injectable because
// the original reads the wall clock for the save time and the file's age.
//
// Deferred, as SaveFile says: the message history (M14). The stores and
// loadGame()'s storeMaintenance() calls joined in M13.
public sealed class GameSave
{
    public GameSave(string saveFilePath = DefaultSaveFile, Func<uint>? clock = null)
    {
        SaveFilePath = saveFilePath;
        Clock = clock ?? (() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    // config::files::save_game's default
    public const string DefaultSaveFile = "game.sav";

    // config::files::save_game -- saveGame() changes it when the player
    // names a new file.
    public string SaveFilePath { get; set; }

    // getCurrentUnixTime()
    public Func<uint> Clock { get; }

    // from_save_file -- this game was restored from SaveFilePath, so saving
    // may overwrite it.
    public bool FromSaveFile { get; set; }

    // start_time -- when play started (set on restore)
    public uint StartTime { get; private set; }

    // How many times the last LoadGame called storeMaintenance(): one per
    // day (rounded) since the file was saved, at most 10.
    public int StoreMaintenanceDays { get; private set; }

    // src/game_save.cpp:58 saveGame() -- set up prior to actual save, do
    // the save, then clean up. On failure it offers to delete the old file
    // or asks for a new name (through w.Confirm / w.ReadString), until a
    // save works or the player gives up.
    public bool SaveGame(PlayerWorld w)
    {
        while (!SaveChar(w, SaveFilePath))
        {
            w.Print($"Save file '{SaveFilePath}' fails.");

            var deleteFailed = false;
            var deleted = false;
            if (File.Exists(SaveFilePath) && PlayerCore.GetInputConfirmation(w, "File exists. Delete old save file?"))
            {
                deleted = TryDelete(SaveFilePath);
                deleteFailed = !deleted;
            }

            if (!deleted)
            {
                if (deleteFailed)
                {
                    w.Print($"Can't delete '{SaveFilePath}'");
                }

                // (getStringInput() reads through getKeyInput(), which
                // zeroes game.command_count)
                w.Game.CommandCount = 0;
                w.ScreenWrite?.Invoke(0, 0, "New Save file [ESC to give up]:", true);
                var input = w.ReadString("New Save file [ESC to give up]:", 45);
                if (input is null)
                {
                    return false;
                }

                if (input.Length != 0)
                {
                    SaveFilePath = input;
                }
            }

            // ("Saving with '...'..." is display.)
        }

        return true;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // src/game_save.cpp:387 saveChar()
    private bool SaveChar(PlayerWorld w, string filename)
    {
        if (w.Game.CharacterSaved)
        {
            return true; // Nothing to save.
        }

        PlayerCore.Disturb(w, 1, 0); // Turn off resting and searching.
        PlayerCore.ChangeSpeed(w, -w.Py.Pack.Heaviness); // Fix the speed
        w.Py.Pack.Heaviness = 0;
        var ok = false;

        // open(O_CREAT | O_EXCL): never replace a file, unless it is the one
        // this game was restored from, or a wizard agrees to.
        FileStream? file = null;
        try
        {
            file = new FileStream(filename, FileMode.CreateNew, FileAccess.Write);
        }
        catch (IOException) when (File.Exists(filename))
        {
            if (FromSaveFile || (w.Game.WizardMode && PlayerCore.GetInputConfirmation(w, "Can't make new save file. Overwrite old?")))
            {
                file = TryOpenForOverwrite(filename);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        var opened = file is not null;

        if (file is not null)
        {
            using (file)
            {
                // The original's XOR key for the file -- meaningless for
                // JSON, but drawn all the same so the RNG stream after a
                // save (which can fail and play on) stays the original's.
                _ = (byte)(RandomNumber.Next(256) - 1);

                try
                {
                    JsonSerializer.Serialize(file, SvWrite(w), SaveFileJson.Options);
                    ok = true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        if (!ok)
        {
            if (opened)
            {
                TryDelete(filename);
                w.Print($"Error writing to file '{filename}'");
            }
            else
            {
                w.Print($"Can't create new file '{filename}'");
            }

            return false;
        }

        w.Game.CharacterSaved = true;
        w.Dg.GameTurn = -1;

        return true;
    }

    private static FileStream? TryOpenForOverwrite(string filename)
    {
        try
        {
            File.SetAttributes(filename, FileAttributes.Normal); // chmod(0600)
            return new FileStream(filename, FileMode.Truncate, FileAccess.Write);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // src/game_save.cpp:88 svWrite()
    private SaveFile SvWrite(PlayerWorld w)
    {
        var py = w.Py;
        var dg = w.Dg;
        var game = w.Game;

        // clear the game.character_is_dead flag when creating a HANGUP save
        // file, so that player can see tombstone when restart
        if (game.EofFlag != 0)
        {
            game.CharacterIsDead = false;
        }

        var save = new SaveFile
        {
            Options = SaveOptions(game.Options),
            CharacterIsDead = game.CharacterIsDead,
            TotalWinner = game.TotalWinner,
        };

        for (var i = 0; i < MonsterConstants.MonMaxCreatures; i++)
        {
            var r = w.CreatureRecall[i];
            if (r.Movement != 0 || r.Defenses != 0 || r.Kills != 0 || r.Spells != 0 || r.Deaths != 0 ||
                r.Attacks[0] != 0 || r.Attacks[1] != 0 || r.Attacks[2] != 0 || r.Attacks[3] != 0)
            {
                save.CreatureRecall.Add(new SavedRecall
                {
                    CreatureId = i,
                    Movement = r.Movement,
                    Spells = r.Spells,
                    Kills = r.Kills,
                    Deaths = r.Deaths,
                    Defenses = r.Defenses,
                    Wake = r.Wake,
                    Ignore = r.Ignore,
                    Attacks = (byte[])r.Attacks.Clone(),
                });
            }
        }

        var c = save.Character;
        c.Misc = Copy(py.Misc);
        c.Stats = Copy(py.Stats);
        c.Flags = Copy(py.Flags);
        c.MissilesCounter = TreasureMagic.MissilesCounter;
        c.GameTurn = dg.GameTurn;
        c.Pack = py.Inventory[..py.Pack.UniqueItems];
        c.Equipment = py.Inventory[PlayerConfig.Equipment.Wield..];
        c.PackWeight = py.Pack.Weight;
        c.EquipmentCount = py.EquipmentCount;
        c.ObjectsIdentified = (byte[])w.Identification.ObjectsIdentified.Clone();
        c.MagicSeed = game.MagicSeed;
        c.TownSeed = game.TownSeed;
        c.PanicSave = game.PanicSave;
        c.NoScore = game.NoScore;
        c.BaseHpLevels = (ushort[])py.BaseHpLevels.Clone();

        foreach (var store in w.Stores)
        {
            var saved = new SavedStore
            {
                TurnsLeftBeforeClosing = store.TurnsLeftBeforeClosing,
                InsultsCounter = store.InsultsCounter,
                OwnerId = store.OwnerId,
                GoodPurchases = store.GoodPurchases,
                BadPurchases = store.BadPurchases,
            };
            for (var j = 0; j < store.UniqueItemsCounter; j++)
            {
                saved.Inventory.Add(new SavedStoreItem { Cost = store.Inventory[j].Cost, Item = store.Inventory[j].Item });
            }
            c.Stores.Add(saved);
        }

        // save the current time in the save file
        var now = Clock();
        if (now < StartTime)
        {
            // someone is messing with the clock!,
            // assume that we have been playing for 1 day
            now = StartTime + 86400;
        }

        c.TimeSaved = now;
        c.CharacterDiedFrom = game.CharacterDiedFrom;
        c.MaxScore = HighScores.PlayerCalculateTotalPoints(w);

        // only level specific info follows, this allows characters to be
        // resurrected, the dungeon level info is not needed for a resurrection
        if (game.CharacterIsDead)
        {
            return save;
        }

        var level = new SavedLevel
        {
            CurrentLevel = (short)dg.CurrentLevel,
            PlayerPos = py.Pos,
            MonsterMultiplyTotal = (short)w.Monsters.MonsterMultiplyTotal,
            Height = (short)dg.Height,
            Width = (short)dg.Width,
            PanelMaxRows = dg.Panel.MaxRows,
            PanelMaxCols = dg.Panel.MaxCols,
            Tiles = new string[DungeonConstants.MaxHeight],
            Treasure = w.Treasure.List[TreasureConfig.MinTreasureListId..w.Treasure.CurrentId],
            Monsters = w.Monsters.Monsters[MonsterConfig.MonMinIndexId..w.Monsters.NextFreeMonsterId],
        };

        var row = new System.Text.StringBuilder(DungeonConstants.MaxWidth * 2);
        for (var y = 0; y < DungeonConstants.MaxHeight; y++)
        {
            row.Clear();
            for (var x = 0; x < DungeonConstants.MaxWidth; x++)
            {
                var tile = dg.Floor[y, x];
                if (tile.CreatureId != 0)
                {
                    level.Creatures.Add(new SavedTileId { Y = y, X = x, Id = tile.CreatureId });
                }

                if (tile.TreasureId != 0)
                {
                    level.Treasures.Add(new SavedTileId { Y = y, X = x, Id = tile.TreasureId });
                }

                row.Append(PackTile(tile).ToString("x2"));
            }

            level.Tiles[y] = row.ToString();
        }

        save.Level = level;
        return save;
    }

    // src/game_save.cpp:460 loadGame() -- certain checks are omitted for
    // the wizard. -CJS- Restores into the world's live objects. Messages go
    // through w.Print; the status lines ("Restoring Memory...", etc.) are
    // display only.
    public LoadResult LoadGame(PlayerWorld w)
    {
        var py = w.Py;
        var dg = w.Dg;
        var game = w.Game;

        var generate = true;

        if (!File.Exists(SaveFilePath))
        {
            w.Print("Save file does not exist.");
            return new LoadResult(LoadOutcome.NoSaveFile, generate);
        }

        // (clearScreen(); "Save file '...' present. Attempting restore.")

        if (dg.GameTurn >= 0)
        {
            w.Print("IMPOSSIBLE! Attempt to restore while still alive!");
            return Fail(dg);
        }

        SaveFile? save;
        try
        {
            using var stream = new FileStream(SaveFilePath, FileMode.Open, FileAccess.Read);
            save = JsonSerializer.Deserialize<SaveFile>(stream, SaveFileJson.Options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            w.Print("Can't open file for reading.");
            return Fail(dg);
        }
        catch (JsonException)
        {
            return ReadError(w);
        }

        dg.GameTurn = -1;

        if (save is null)
        {
            return ReadError(w);
        }

        if (save.FormatVersion != SaveFile.CurrentFormatVersion)
        {
            // ("Sorry. This save file is from a different version of umoria." -- display)
            return ReadError(w);
        }

        foreach (var saved in save.CreatureRecall)
        {
            if (saved.CreatureId < 0 || saved.CreatureId >= MonsterConstants.MonMaxCreatures ||
                saved.Attacks.Length != MonsterConstants.MonMaxAttacks)
            {
                return ReadError(w);
            }

            var memory = w.CreatureRecall[saved.CreatureId];
            memory.Movement = saved.Movement;
            memory.Spells = saved.Spells;
            memory.Kills = saved.Kills;
            memory.Deaths = saved.Deaths;
            memory.Defenses = saved.Defenses;
            memory.Wake = saved.Wake;
            memory.Ignore = saved.Ignore;
            saved.Attacks.CopyTo(memory.Attacks, 0);
        }

        RestoreOptions(save.Options, game.Options);

        var dead = save.CharacterIsDead;

        // Don't allow resurrection of game.total_winner characters. It
        // causes problems because the character level is out of the allowed
        // range.
        if (game.ToBeWizard && save.TotalWinner)
        {
            w.Print("Sorry, this character is retired from moria.");
            w.Print("You can not resurrect a retired character.");
        }
        else if (game.ToBeWizard && dead && PlayerCore.GetInputConfirmation(w, "Resurrect a dead character?"))
        {
            dead = false;
        }

        uint timeSaved = 0;

        if (!dead)
        {
            if (!RestoreCharacter(w, save.Character))
            {
                return ReadError(w);
            }

            // (svWrite() writes total_winner twice: in the bit field and
            // again after panic_save. This is the second.)
            game.TotalWinner = save.TotalWinner;

            timeSaved = save.Character.TimeSaved;
        }

        if (save.Level is null || dead)
        {
            if (!dead)
            {
                if (!game.ToBeWizard || dg.GameTurn < 0)
                {
                    return ReadError(w);
                }

                // ("Attempting a resurrection!")
                if (py.Misc.CurrentHp < 0)
                {
                    py.Misc.CurrentHp = 0;
                    py.Misc.CurrentHpFraction = 0;
                }

                // don't let them starve to death immediately
                if (py.Flags.Food < 0)
                {
                    py.Flags.Food = 0;
                }

                // don't let them immediately die of poison again
                if (py.Flags.Poisoned > 1)
                {
                    py.Flags.Poisoned = 1;
                }

                dg.CurrentLevel = 0; // Resurrect on the town level.
                game.CharacterGenerated = true;

                // set `noscore` to indicate a resurrection, and don't enter wizard mode
                game.ToBeWizard = false;
                game.NoScore |= 0x1;
            }
            else
            {
                // Make sure that this message is seen, since it is a bit
                // more interesting than the other messages.
                w.Print("Restoring Memory of a departed spirit...");
                dg.GameTurn = -1;
            }
        }
        else
        {
            // ("Restoring Character...")
            if (!RestoreLevel(w, save.Level))
            {
                return ReadError(w);
            }

            generate = false; // We have restored a cave - no need to generate.

            if (dg.GameTurn < 0)
            {
                return ReadError(w);
            }

            // don't overwrite the killed by string if character is dead
            if (py.Misc.CurrentHp >= 0)
            {
                game.CharacterDiedFrom = "(alive and well)";
            }

            game.CharacterGenerated = true;
        }

        // let the user overwrite the old save file when save/quit
        FromSaveFile = true;

        if (game.PanicSave)
        {
            w.Print("This game is from a panic save.  Score will not be added to scoreboard.");
        }
        else if (((game.NoScore == 0 ? 1 : 0) & 0x04) != 0)
        {
            // Never true: the original's `(!game.noscore) & 0x04` is a
            // 0-or-1 value masked with 4. Kept as written.
            w.Print("This character is already on the scoreboard; it will not be scored again.");
            game.NoScore |= 0x4;
        }

        if (dg.GameTurn >= 0)
        {
            // Only if a full restoration.
            py.WeaponIsHeavy = false;
            py.Pack.Heaviness = 0;
            PlayerCore.Strength(w);

            // rotate store inventory, depending on how old the save file
            // is foreach day old (rounded up), call storeMaintenance
            // calculate age in seconds
            StartTime = Clock();

            // check for reasonable values of time here ...
            var age = StartTime < timeSaved ? 0 : StartTime - timeSaved;

            age = (age + 43200) / 86400; // age in days
            if (age > 10)
            {
                age = 10; // in case save file is very old
            }

            StoreMaintenanceDays = (int)age;
            for (var i = 0; i < (int)age; i++)
            {
                StoreInventory.Maintenance(w);
            }
        }

        if (game.NoScore != 0)
        {
            w.Print("This save file cannot be used to get on the score board.");
        }

        // (The "Save file version X accepted on game version Y" note has no
        // counterpart: there is only one format version so far.)

        // if false: only restored options and monster memory.
        return new LoadResult(dg.GameTurn >= 0 ? LoadOutcome.Restored : LoadOutcome.MemoryOnly, generate);
    }

    private static LoadResult ReadError(PlayerWorld w)
    {
        w.Print("Error during reading of file.");
        return Fail(w.Dg);
    }

    private static LoadResult Fail(Model.Generated.Dungeon dg)
    {
        dg.GameTurn = -1;

        // ("Please try again without that save file." and exitProgram() are
        // the caller's -- see LoadOutcome.Failed.)
        return new LoadResult(LoadOutcome.Failed, true);
    }

    // The character half of loadGame(), with its bounds checks.
    private static bool RestoreCharacter(PlayerWorld w, SavedCharacter c)
    {
        var py = w.Py;
        var game = w.Game;

        if (c.Misc.History.Length != 4 || c.Stats.Max.Length != 6 || c.Stats.Current.Length != 6 ||
            c.Stats.Modified.Length != 6 || c.Stats.Used.Length != 6 || c.Flags.SpellsLearnedOrder.Length != 32 ||
            c.Pack.Length > PlayerConfig.Equipment.Wield ||
            c.Equipment.Length != PlayerConfig.PlayerInventorySize - PlayerConfig.Equipment.Wield ||
            c.ObjectsIdentified.Length != w.Identification.ObjectsIdentified.Length ||
            c.BaseHpLevels.Length != PlayerConfig.PlayerMaxLevel ||
            c.Stores.Count != StoreConfig.MaxStores || c.Stores.Any(s => s.Inventory.Count > StoreConfig.StoreMaxDiscreteItems))
        {
            return false;
        }

        py.Misc = c.Misc;
        py.Stats = c.Stats;
        py.Flags = c.Flags;

        TreasureMagic.MissilesCounter = c.MissilesCounter;
        w.Dg.GameTurn = c.GameTurn;

        py.Pack.UniqueItems = (short)c.Pack.Length;
        for (var i = 0; i < c.Pack.Length; i++)
        {
            py.Inventory[i] = Sanitize(c.Pack[i]);
        }

        for (var i = 0; i < c.Equipment.Length; i++)
        {
            py.Inventory[PlayerConfig.Equipment.Wield + i] = Sanitize(c.Equipment[i]);
        }

        py.Pack.Weight = c.PackWeight;
        py.EquipmentCount = c.EquipmentCount;
        c.ObjectsIdentified.CopyTo(w.Identification.ObjectsIdentified, 0);
        game.MagicSeed = c.MagicSeed;
        game.TownSeed = c.TownSeed;
        game.PanicSave = c.PanicSave;
        game.NoScore = c.NoScore;
        c.BaseHpLevels.CopyTo(py.BaseHpLevels, 0);

        // (slots past each store's count keep what they held, as in the
        // original, which reads only the saved ones)
        for (var i = 0; i < StoreConfig.MaxStores; i++)
        {
            var saved = c.Stores[i];
            var store = w.Stores[i];
            store.TurnsLeftBeforeClosing = saved.TurnsLeftBeforeClosing;
            store.InsultsCounter = saved.InsultsCounter;
            store.OwnerId = saved.OwnerId;
            store.UniqueItemsCounter = (byte)saved.Inventory.Count;
            store.GoodPurchases = saved.GoodPurchases;
            store.BadPurchases = saved.BadPurchases;
            for (var j = 0; j < saved.Inventory.Count; j++)
            {
                store.Inventory[j].Cost = saved.Inventory[j].Cost;
                store.Inventory[j].Item = Sanitize(saved.Inventory[j].Item);
            }
        }

        game.CharacterDiedFrom = c.CharacterDiedFrom;
        py.MaxScore = c.MaxScore;

        return true;
    }

    // The level half of loadGame(), with its bounds checks.
    private static bool RestoreLevel(PlayerWorld w, SavedLevel level)
    {
        var py = w.Py;
        var dg = w.Dg;

        // (The original checks `x > MAX_WIDTH || y > MAX_HEIGHT`, which
        // lets one past the edge through; here every index must fit.)
        static bool OnFloor(int y, int x) =>
            y >= 0 && y < DungeonConstants.MaxHeight && x >= 0 && x < DungeonConstants.MaxWidth;

        if (level.Tiles.Length != DungeonConstants.MaxHeight ||
            level.Tiles.Any(row => row.Length != DungeonConstants.MaxWidth * 2) ||
            level.Creatures.Any(t => !OnFloor(t.Y, t.X)) ||
            level.Treasures.Any(t => !OnFloor(t.Y, t.X)) ||
            TreasureConfig.MinTreasureListId + level.Treasure.Length > TreasureList.LevelMaxObjects ||
            MonsterConfig.MonMinIndexId + level.Monsters.Length > MonsterConstants.MonTotalAllocations)
        {
            return false;
        }

        dg.CurrentLevel = level.CurrentLevel;
        py.Pos = level.PlayerPos;
        w.Monsters.MonsterMultiplyTotal = level.MonsterMultiplyTotal;
        dg.Height = level.Height;
        dg.Width = level.Width;
        dg.Panel.MaxRows = level.PanelMaxRows;
        dg.Panel.MaxCols = level.PanelMaxCols;

        foreach (var t in level.Creatures)
        {
            dg.Floor[t.Y, t.X].CreatureId = t.Id;
        }

        foreach (var t in level.Treasures)
        {
            dg.Floor[t.Y, t.X].TreasureId = t.Id;
        }

        for (var y = 0; y < DungeonConstants.MaxHeight; y++)
        {
            var row = level.Tiles[y];
            for (var x = 0; x < DungeonConstants.MaxWidth; x++)
            {
                if (!byte.TryParse(row.AsSpan(x * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out var packed))
                {
                    return false;
                }

                UnpackTile(ref dg.Floor[y, x], packed);
            }
        }

        w.Treasure.CurrentId = TreasureConfig.MinTreasureListId + level.Treasure.Length;
        for (var i = 0; i < level.Treasure.Length; i++)
        {
            w.Treasure.List[TreasureConfig.MinTreasureListId + i] = Sanitize(level.Treasure[i]);
        }

        w.Monsters.NextFreeMonsterId = MonsterConfig.MonMinIndexId + level.Monsters.Length;
        for (var i = 0; i < level.Monsters.Length; i++)
        {
            w.Monsters.Monsters[MonsterConfig.MonMinIndexId + i] = level.Monsters[i];
        }

        return true;
    }

    // A JSON null inscription would break code that expects the C++ empty
    // buffer ("").
    private static Inventory Sanitize(Inventory item) => item with { Inscription = item.Inscription ?? "" };

    private static byte PackTile(Tile tile)
    {
        return (byte)(tile.FeatureId |
                      (tile.PermaLitRoom ? 1 << 4 : 0) |
                      (tile.FieldMark ? 1 << 5 : 0) |
                      (tile.PermanentLight ? 1 << 6 : 0) |
                      (tile.TemporaryLight ? 1 << 7 : 0));
    }

    private static void UnpackTile(ref Tile tile, byte packed)
    {
        tile.FeatureId = (byte)(packed & 0xF);
        tile.PermaLitRoom = ((packed >> 4) & 0x1) != 0;
        tile.FieldMark = ((packed >> 5) & 0x1) != 0;
        tile.PermanentLight = ((packed >> 6) & 0x1) != 0;
        tile.TemporaryLight = ((packed >> 7) & 0x1) != 0;
    }

    private static SavedOptions SaveOptions(GameOptions o) => new()
    {
        RunCutCorners = o.RunCutCorners,
        RunExamineCorners = o.RunExamineCorners,
        RunPrintSelf = o.RunPrintSelf,
        FindBound = o.FindBound,
        PromptToPickup = o.PromptToPickup,
        UseRoguelikeKeys = o.UseRoguelikeKeys,
        ShowInventoryWeights = o.ShowInventoryWeights,
        HighlightSeams = o.HighlightSeams,
        RunIgnoreDoors = o.RunIgnoreDoors,
        ErrorBeepSound = o.ErrorBeepSound,
        DisplayCounts = o.DisplayCounts,
    };

    private static void RestoreOptions(SavedOptions s, GameOptions o)
    {
        o.RunCutCorners = s.RunCutCorners;
        o.RunExamineCorners = s.RunExamineCorners;
        o.RunPrintSelf = s.RunPrintSelf;
        o.FindBound = s.FindBound;
        o.PromptToPickup = s.PromptToPickup;
        o.UseRoguelikeKeys = s.UseRoguelikeKeys;
        o.ShowInventoryWeights = s.ShowInventoryWeights;
        o.HighlightSeams = s.HighlightSeams;
        o.RunIgnoreDoors = s.RunIgnoreDoors;
        o.ErrorBeepSound = s.ErrorBeepSound;
        o.DisplayCounts = s.DisplayCounts;
    }

    // Snapshots of the live player blocks, so the save describes the moment
    // it was taken even if the caller keeps playing.
    private static T Copy<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, SaveFileJson.Options), SaveFileJson.Options)!;
}
