using System.Text.Json;
using System.Text.Json.Serialization;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Persistence;

// The JSON save file (M12) -- a new format, by the roadmap's earlier
// compatibility-breaking decision, in place of game_save.cpp's XOR-encoded
// byte stream. It holds exactly what svWrite() writes, grouped the way
// svWrite() orders it, so loadGame()'s logic ports unchanged:
//
//   - the monster memories and the options, always restored, even for a
//     dead character (the "Restoring Memory of a departed spirit" case);
//   - the character, restored unless the character is dead;
//   - the current level, absent for a dead character -- svWrite() stops
//     before it, which is how loadGame() tells a resurrection apart.
//
// M13 added the stores (svWrite() writes each store's owner, counters and
// inventory, between the character's hit-point table and the save time).
// M15 added the message history (last_message_id and messages[], after the
// seeds, where svWrite() writes them).
public sealed class SaveFile
{
    // Bumped when the format changes incompatibly; LoadGame rejects other
    // versions, as validGameVersion() did.
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    // creature_recall[] entries with anything remembered, as svWrite()
    // skips the empty ones.
    public List<SavedRecall> CreatureRecall { get; set; } = [];

    // svWrite()'s option bit field, as names.
    public SavedOptions Options { get; set; } = new();

    // The bit field's two status bits: game.character_is_dead (sign bit)
    // and game.total_winner.
    public bool CharacterIsDead { get; set; }
    public bool TotalWinner { get; set; }

    public SavedCharacter Character { get; set; } = new();

    // null when the character is dead.
    public SavedLevel? Level { get; set; }
}

public sealed class SavedRecall
{
    public int CreatureId { get; set; }
    public uint Movement { get; set; }
    public uint Spells { get; set; }
    public ushort Kills { get; set; }
    public ushort Deaths { get; set; }
    public ushort Defenses { get; set; }
    public byte Wake { get; set; }
    public byte Ignore { get; set; }
    public byte[] Attacks { get; set; } = [];
}

public sealed class SavedOptions
{
    public bool RunCutCorners { get; set; }
    public bool RunExamineCorners { get; set; }
    public bool RunPrintSelf { get; set; }
    public bool FindBound { get; set; }
    public bool PromptToPickup { get; set; }
    public bool UseRoguelikeKeys { get; set; }
    public bool ShowInventoryWeights { get; set; }
    public bool HighlightSeams { get; set; }
    public bool RunIgnoreDoors { get; set; }
    public bool ErrorBeepSound { get; set; }
    public bool DisplayCounts { get; set; }
}

// Everything svWrite() writes between the options and the level. The
// player's misc/stats/flags blocks are saved whole: svWrite() writes every
// one of their fields (date_of_birth last, separately; it lives in Misc
// here).
public sealed class SavedCharacter
{
    public PlayerMisc Misc { get; set; } = new();
    public PlayerStats Stats { get; set; } = new();
    public PlayerFlags Flags { get; set; } = new();

    // missiles_counter (TreasureMagic.MissilesCounter)
    public short MissilesCounter { get; set; }
    public int GameTurn { get; set; }

    // py.inventory[0, unique_items) -- the pack's slots between the last
    // item and the first equipment slot are not saved.
    public Inventory[] Pack { get; set; } = [];

    // py.inventory[Wield, PLAYER_INVENTORY_SIZE)
    public Inventory[] Equipment { get; set; } = [];

    public short PackWeight { get; set; }
    public short EquipmentCount { get; set; }

    public byte[] ObjectsIdentified { get; set; } = [];

    // The flavour names are not saved: they are re-derived from the magic
    // seed (ObjectIdentification.MagicInitializeItemNames, M11).
    public uint MagicSeed { get; set; }
    public uint TownSeed { get; set; }

    // last_message_id and messages[] -- the ^P history (M15). Older files
    // without them restore an empty history.
    public short LastMessageId { get; set; }
    public string[] Messages { get; set; } = [];

    // "this indicates 'cheating' if it is a one"
    public bool PanicSave { get; set; }
    public short NoScore { get; set; }
    public ushort[] BaseHpLevels { get; set; } = [];

    // stores[] -- all six, each with its stocked slots only (M13)
    public List<SavedStore> Stores { get; set; } = [];

    // Unix time of the save; the age of the file decides how many times
    // the stores are maintained on restore (M13).
    public uint TimeSaved { get; set; }
    public string CharacterDiedFrom { get; set; } = "";

    // playerCalculateTotalPoints() at save time, restored as py.max_score.
    public int MaxScore { get; set; }
}

// src/store.h Store_t, as svWrite() writes it: the counters, then the
// first unique_items_counter inventory records. (M13)
public sealed class SavedStore
{
    public int TurnsLeftBeforeClosing { get; set; }
    public short InsultsCounter { get; set; }
    public byte OwnerId { get; set; }
    public ushort GoodPurchases { get; set; }
    public ushort BadPurchases { get; set; }
    public List<SavedStoreItem> Inventory { get; set; } = [];
}

public sealed class SavedStoreItem
{
    public int Cost { get; set; }
    public Inventory Item { get; set; }
}

public sealed class SavedLevel
{
    public short CurrentLevel { get; set; }
    public Coord PlayerPos { get; set; }
    public short MonsterMultiplyTotal { get; set; }
    public short Height { get; set; }
    public short Width { get; set; }
    public short PanelMaxRows { get; set; }
    public short PanelMaxCols { get; set; }

    // Floor cells with a monster / an object on them.
    public List<SavedTileId> Creatures { get; set; } = [];
    public List<SavedTileId> Treasures { get; set; } = [];

    // The rest of every tile, one string per row, two hex digits per tile:
    // svWrite()'s packing, feature_id | perma_lit_room << 4 | field_mark << 5
    // | permanent_light << 6 | temporary_light << 7 (without its run-length
    // encoding).
    public string[] Tiles { get; set; } = [];

    // game.treasure.list[MIN_TREASURE_LIST_ID, current_id)
    public Inventory[] Treasure { get; set; } = [];

    // monsters[MON_MIN_INDEX_ID, next_free_monster_id)
    public Monster[] Monsters { get; set; } = [];
}

public sealed class SavedTileId
{
    public int Y { get; set; }
    public int X { get; set; }
    public byte Id { get; set; }
}

// Shared serializer settings for the save and score files.
public static class SaveFileJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new ByteArrayAsNumbersConverter() },
    };

    // System.Text.Json writes byte[] as base64; the stats, attack memories
    // and objects_identified[] read better as plain number arrays.
    private sealed class ByteArrayAsNumbersConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("expected an array of numbers");
            }

            var bytes = new List<byte>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                bytes.Add(reader.GetByte());
            }

            return bytes.ToArray();
        }

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var b in value)
            {
                writer.WriteNumberValue(b);
            }

            writer.WriteEndArray();
        }
    }
}
