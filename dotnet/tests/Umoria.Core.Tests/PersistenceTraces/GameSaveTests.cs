using System.Text.Json;
using Umoria.Core.Persistence;

namespace Umoria.Core.Tests.PersistenceTraces;

// The parts of M12 with no C++ counterpart to trace against: the JSON
// format itself (version and bounds checks, damaged files) and the file
// rules of saveGame()/saveChar() (which the trace never reaches -- its
// saves always go to a fresh file). The game logic around a successful
// save and restore is PersistenceTraceTests' job.
public class GameSaveTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "umoria-gamesave-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public GameSaveTests()
    {
        Directory.CreateDirectory(_workDir);
        _path = Path.Combine(_workDir, "game.sav");
    }

    public void Dispose()
    {
        Directory.Delete(_workDir, true);
    }

    private static List<string> Log(Character.PlayerWorld w)
    {
        var log = new List<string>();
        w.MessageLog = log.Add;
        return log;
    }

    private void SaveAliveWorld(uint clock = 1_000_000)
    {
        var w = PersistenceTraceTests.BuildWorld(4001);
        Assert.True(new GameSave(_path, () => clock).SaveGame(w));
    }

    [Fact]
    public void Save_WritesTheDocumentedJsonShape()
    {
        SaveAliveWorld();

        using var doc = JsonDocument.Parse(File.ReadAllText(_path));
        var root = doc.RootElement;
        Assert.Equal(SaveFile.CurrentFormatVersion, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("(saved)", root.GetProperty("character").GetProperty("characterDiedFrom").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("character").GetProperty("objectsIdentified").ValueKind);

        var level = root.GetProperty("level");
        Assert.Equal(66, level.GetProperty("tiles").GetArrayLength());
        Assert.Equal(198 * 2, level.GetProperty("tiles")[0].GetString()!.Length);
    }

    [Fact]
    public void Save_DeadCharacterHasNoLevel()
    {
        var w = PersistenceTraceTests.BuildWorld(4001);
        w.Game.CharacterIsDead = true;
        Assert.True(new GameSave(_path).SaveGame(w));

        using var doc = JsonDocument.Parse(File.ReadAllText(_path));
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("level").ValueKind);
    }

    [Fact]
    public void Save_AlreadySavedWritesNothing()
    {
        var w = PersistenceTraceTests.BuildWorld(4001);
        w.Game.CharacterSaved = true;

        Assert.True(new GameSave(_path).SaveGame(w));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Save_NeverReplacesAFileThisGameDidNotLoad_UnlessTheOldOneIsDeleted()
    {
        File.WriteAllText(_path, "precious");
        var w = PersistenceTraceTests.BuildWorld(4001);
        var log = Log(w);
        var prompts = new List<string>();
        w.Confirm = prompt =>
        {
            prompts.Add(prompt);
            return true;
        };

        Assert.True(new GameSave(_path).SaveGame(w));

        Assert.Equal(["File exists. Delete old save file?"], prompts);
        Assert.Equal([$"Can't create new file '{_path}'", $"Save file '{_path}' fails."], log);
        Assert.StartsWith("{", File.ReadAllText(_path));
    }

    [Fact]
    public void Save_KeepsTheOldFileAndTakesANewName_OrGivesUp()
    {
        File.WriteAllText(_path, "precious");
        var other = Path.Combine(_workDir, "other.sav");

        var w = PersistenceTraceTests.BuildWorld(4001);
        w.Confirm = _ => false;
        var names = new Queue<string?>([other]);
        w.ReadString = (_, _) => names.Dequeue();
        var save = new GameSave(_path);

        Assert.True(save.SaveGame(w));
        Assert.Equal(other, save.SaveFilePath);
        Assert.Equal("precious", File.ReadAllText(_path));
        Assert.True(File.Exists(other));

        var w2 = PersistenceTraceTests.BuildWorld(4002);
        w2.Confirm = _ => false;
        w2.ReadString = (_, _) => null; // <ESCAPE>
        Assert.False(new GameSave(_path).SaveGame(w2));
        Assert.Equal("precious", File.ReadAllText(_path));
    }

    [Fact]
    public void Save_AfterRestoringFromTheFile_OverwritesIt()
    {
        SaveAliveWorld();

        var w = PersistenceTraceTests.FreshWorld();
        var save = new GameSave(_path);
        Assert.Equal(LoadOutcome.Restored, save.LoadGame(w).Outcome);
        Assert.True(save.FromSaveFile);

        w.Py.Misc.Au = 4242;
        Assert.True(save.SaveGame(w));

        var again = PersistenceTraceTests.FreshWorld();
        Assert.Equal(LoadOutcome.Restored, new GameSave(_path).LoadGame(again).Outcome);
        Assert.Equal(4242, again.Py.Misc.Au);
    }

    [Fact]
    public void Load_NoFile_StartsANewCharacter()
    {
        var w = PersistenceTraceTests.FreshWorld();
        var log = Log(w);

        Assert.Equal(LoadOutcome.NoSaveFile, new GameSave(_path).LoadGame(w).Outcome);
        Assert.Equal(["Save file does not exist."], log);
    }

    [Fact]
    public void Load_WhileAlive_Fails()
    {
        SaveAliveWorld();
        var w = PersistenceTraceTests.FreshWorld();
        w.Dg.GameTurn = 10;
        var log = Log(w);

        Assert.Equal(LoadOutcome.Failed, new GameSave(_path).LoadGame(w).Outcome);
        Assert.Equal(["IMPOSSIBLE! Attempt to restore while still alive!"], log);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("null")]
    [InlineData("{\"formatVersion\": 99}")]
    public void Load_DamagedOrForeignFile_Fails(string content)
    {
        File.WriteAllText(_path, content);
        var w = PersistenceTraceTests.FreshWorld();
        var log = Log(w);

        Assert.Equal(LoadOutcome.Failed, new GameSave(_path).LoadGame(w).Outcome);
        Assert.Equal(["Error during reading of file."], log);
        Assert.Equal(-1, w.Dg.GameTurn);
    }

    [Theory]
    [InlineData("pack")]
    [InlineData("tiles")]
    [InlineData("creature")]
    [InlineData("recall")]
    [InlineData("stores")]
    [InlineData("store_stock")]
    public void Load_OutOfBoundsData_Fails(string what)
    {
        SaveAliveWorld();
        var save = JsonSerializer.Deserialize<SaveFile>(File.ReadAllText(_path), SaveFileJson.Options)!;
        switch (what)
        {
            case "pack":
                save.Character.Pack = new Model.Generated.Inventory[23];
                break;
            case "tiles":
                save.Level!.Tiles = save.Level.Tiles[..10];
                break;
            case "creature":
                save.Level!.Creatures.Add(new SavedTileId { Y = 66, X = 0, Id = 2 });
                break;
            case "recall":
                save.CreatureRecall.Add(new SavedRecall { CreatureId = 279, Attacks = new byte[4] });
                break;
            case "stores":
                save.Character.Stores.RemoveAt(5);
                break;
            case "store_stock":
                save.Character.Stores[0].Inventory = Enumerable.Range(0, 25).Select(_ => new SavedStoreItem()).ToList();
                break;
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(save, SaveFileJson.Options));

        var w = PersistenceTraceTests.FreshWorld();
        Assert.Equal(LoadOutcome.Failed, new GameSave(_path).LoadGame(w).Outcome);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(43199, 0)]
    [InlineData(43200, 1)]
    [InlineData(3 * 86400 + 50000, 4)]
    [InlineData(30 * 86400, 10)]
    public void Load_CountsStoreMaintenanceDaysByTheFilesAge(uint ageSeconds, int days)
    {
        SaveAliveWorld(clock: 1_000_000);

        var w = PersistenceTraceTests.FreshWorld();
        var save = new GameSave(_path, () => 1_000_000 + ageSeconds);
        Assert.Equal(LoadOutcome.Restored, save.LoadGame(w).Outcome);
        Assert.Equal(days, save.StoreMaintenanceDays);
        Assert.Equal(1_000_000 + ageSeconds, save.StartTime);
    }

    // loadGame() calls storeMaintenance() once per day of the file's age
    // (M13): the same stores as a same-day restore followed by that many
    // maintenance calls, from the same RNG state.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Load_MaintainsTheStoresOncePerDay(int days)
    {
        SaveAliveWorld(clock: 1_000_000);

        Rng.MoriaRng.SetRandomSeed(777);
        var aged = PersistenceTraceTests.FreshWorld();
        Assert.Equal(LoadOutcome.Restored, new GameSave(_path, () => 1_000_000 + (uint)days * 86400).LoadGame(aged).Outcome);

        Rng.MoriaRng.SetRandomSeed(777);
        var fresh = PersistenceTraceTests.FreshWorld();
        Assert.Equal(LoadOutcome.Restored, new GameSave(_path, () => 1_000_000).LoadGame(fresh).Outcome);
        var sameDay = StoresSnapshot(fresh);
        for (var i = 0; i < days; i++)
        {
            Stores.StoreInventory.Maintenance(fresh);
        }

        Assert.Equal(StoresSnapshot(fresh), StoresSnapshot(aged));
        Assert.Equal(days == 0, StoresSnapshot(aged) == sameDay);
    }

    // svWrite() writes every store's counters and its stocked slots; they
    // come back unchanged (M13).
    [Fact]
    public void SaveAndLoad_RoundTripsTheStores()
    {
        var w = PersistenceTraceTests.BuildWorld(4001);
        Stores.StoreCommands.InitializeOwners(w);
        Stores.StoreInventory.Maintenance(w);
        Stores.StoreInventory.Maintenance(w);
        w.Stores[2].InsultsCounter = 3;
        w.Stores[3].TurnsLeftBeforeClosing = 4321;
        w.Stores[4].GoodPurchases = 7;
        w.Stores[5].BadPurchases = 2;
        w.Stores[1].Inventory[0].Cost = 55;
        Assert.True(new GameSave(_path, () => 1_000_000).SaveGame(w));

        var restored = PersistenceTraceTests.FreshWorld();
        Assert.Equal(LoadOutcome.Restored, new GameSave(_path, () => 1_000_000).LoadGame(restored).Outcome);

        Assert.Equal(StoresSnapshot(w), StoresSnapshot(restored));
    }

    private static string StoresSnapshot(Character.PlayerWorld w) =>
        string.Join("\n", w.Stores.Select(store =>
            $"{store.OwnerId} {store.InsultsCounter} {store.TurnsLeftBeforeClosing} {store.GoodPurchases} {store.BadPurchases} {store.UniqueItemsCounter}: " +
            string.Join(", ", store.Inventory.Take(store.UniqueItemsCounter).Select(r => $"{r.Cost}/{r.Item}"))));

    [Fact]
    public void Load_ClockBehindTheSaveTime_CountsNoDays()
    {
        SaveAliveWorld(clock: 1_000_000);

        var save = new GameSave(_path, () => 500);
        Assert.Equal(LoadOutcome.Restored, save.LoadGame(PersistenceTraceTests.FreshWorld()).Outcome);
        Assert.Equal(0, save.StoreMaintenanceDays);
    }

    private static HighScore Score(int points, int birth = 1, string died = "a Kobold") =>
        new() { Points = points, BirthDate = birth, Gender = (byte)'M', DiedFrom = died, Name = $"P{points}" };

    [Fact]
    public void HighScores_EntriesBelowTheCapAreNotAdded()
    {
        var scores = new List<HighScore> { Score(300, 1), Score(200, 2), Score(100, 3) };

        Assert.False(HighScores.Insert(scores, Score(50, 4), maxEntries: 3));
        Assert.True(HighScores.Insert(scores, Score(150, 5), maxEntries: 3));
        Assert.Equal([300, 200, 150, 100], scores.Select(s => s.Points));
    }

    [Fact]
    public void HighScores_ForeignFormatVersionIsLeftAlone()
    {
        var path = Path.Combine(_workDir, "scores.json");
        File.WriteAllText(path, "{\"formatVersion\": 7, \"entries\": []}");

        var w = PersistenceTraceTests.BuildWorld(4001);
        HighScores.RecordNewHighScore(w, path);

        Assert.Equal("{\"formatVersion\": 7, \"entries\": []}", File.ReadAllText(path));
    }

    [Fact]
    public void HighScores_MissingFileStartsANewBoard()
    {
        var path = Path.Combine(_workDir, "scores.json");
        var w = PersistenceTraceTests.BuildWorld(4001);
        w.Game.CharacterDiedFrom = "an Orc";

        HighScores.RecordNewHighScore(w, path);

        var entry = Assert.Single(HighScores.ReadFile(path)!.Entries);
        Assert.Equal("Orc", entry.DiedFrom);
        Assert.Equal(HighScores.PlayerCalculateTotalPoints(w), entry.Points);
    }
}
