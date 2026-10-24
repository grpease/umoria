// Persistence dump harness: verifies the .NET migration's M12 port (save /
// load, scores and the character sheet -- Umoria.Core.Persistence.GameSave
// / HighScores / CharacterSheet, and Items.ItemValue) against the real
// compiled C++ game.
//
// The save and score FILES are new formats in the port (JSON), so their
// bytes are not compared. What is compared is everything around them that
// the original defines:
//   value   storeItemValue() for every catalog object in a series of
//           identification states (the score adds it up over the pack);
//   sheet   outputPlayerCharacterToFile()'s text and
//           playerCalculateTotalPoints() for created characters, in four
//           variants that reach every branch of the sheet;
//   scores  recordNewHighScore()'s insertion rules: each scenario of
//           score_scenarios.inc (shared with the C# test) enters its
//           entries into an empty score file, then the file's entries are
//           read back and showScoresScreen()'s lines captured;
//   save    a saveGame() -> fresh-process reset -> loadGame() round trip
//           per scenario (alive, dead, hangup, resurrection, ...): the
//           restored state, the messages and the RNG state are dumped, so
//           the port must save and restore exactly the same fields and
//           apply the same pre-save and post-restore fixups. M13 added
//           the stores (owners, counters and stock after two days of
//           restocking) to the world saved and dumped.
//
// Same build pattern as item_use_dump (M11): the real sources minus
// main.cpp and ui_io.cpp (the M6 headless stub, which gained an opt-in log
// of putStringClearToEOL() lines for the score screen) -- zero changes to
// src/. The score and save files are relative paths (scores.dat, game.sav,
// as in the game), so the harness changes into a work directory first.
//
// Usage: persistence_dump <work_dir> [output_file]   (output_file absolute)

#include "../../src/headers.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#ifdef _WIN32
#include <direct.h>
#define chdir _chdir
#else
#include <unistd.h>
#endif

#include "../char_create_dump/char_create_common.h"
#include "../char_create_dump/ui_io_stub.h"

static FILE *out = stdout;

static void dumpMessages(std::vector<std::string> &log) {
    for (auto const &msg : log) {
        std::fprintf(out, "msg\t%s\n", msg.c_str());
    }
    log.clear();
}

// ---- value ---------------------------------------------------------------

// Mirrored in PersistenceTraceTests.DumpValues.
static void dumpValues() {
    setRandomSeed(4242);
    missiles_counter = 0;

    auto value = [](int id, const char *variant, Inventory_t const &item) {
        std::fprintf(out, "value.%d.%s\t%d\n", id, variant, storeItemValue(item));
    };

    for (int id = 0; id < MAX_OBJECTS_IN_GAME; id++) {
        std::memset(objects_identified, 0, sizeof(objects_identified));

        Inventory_t &item = game.treasure.list[1];
        inventoryItemCopyTo(id, item);
        uint8_t count = item.items_count;

        value(id, "plain", item);
        item.items_count = 5;
        value(id, "five", item);
        item.items_count = count;

        itemSetAsTried(item);
        value(id, "tried", item);

        magicTreasureMagicalAbility(1, 40);
        value(id, "magic", item);

        itemSetAsIdentified(item.category_id, item.sub_category_id);
        value(id, "known", item);

        spellItemIdentifyAndRemoveRandomInscription(item);
        value(id, "known2", item);
        item.items_count = 5;
        value(id, "known2_five", item);
        item.items_count = count;

        item.misc_use = -3;
        value(id, "negmisc", item);
        item.to_hit = -1;
        value(id, "neghit", item);
        item.identification |= config::identification::ID_DAMD;
        value(id, "damd", item);
    }
}

// ---- sheet ---------------------------------------------------------------

// Wears copies of the starting kit (it all starts in the pack), so the
// equipment list has something to show. Mirrored in
// PersistenceTraceTests.WearStartingKit.
static void wearStartingKit() {
    py.equipment_count = 0;
    for (int i = 0; i < py.pack.unique_items; i++) {
        Inventory_t const &item = py.inventory[i];
        int slot = -1;
        if (item.category_id >= TV_BOW && item.category_id <= TV_SWORD) {
            slot = PlayerEquipment::Wield;
        } else if (item.category_id == TV_SOFT_ARMOR || item.category_id == TV_HARD_ARMOR) {
            slot = PlayerEquipment::Body;
        } else if (item.category_id == TV_CLOAK) {
            slot = PlayerEquipment::Outer;
        } else if (item.category_id == TV_LIGHT) {
            slot = PlayerEquipment::Light;
        }
        if (slot >= 0 && py.inventory[slot].category_id == TV_NOTHING) {
            py.inventory[slot] = item;
            py.inventory[slot].items_count = 1;
            py.equipment_count++;
        }
    }
}

// Mirrored in PersistenceTraceTests.ApplySheetVariant.
static void applySheetVariant(int variant) {
    switch (variant) {
        case 0:
            dg.current_level = 0;
            break;
        case 1: {
            py.misc.level = 20;
            py.misc.exp = 5000;
            py.misc.max_exp = 6000;
            py.misc.au = 12345;
            py.misc.max_dungeon_depth = 7;
            dg.current_level = 3;
            const uint8_t used[6] = {3, 18, 19, 68, 117, 118};
            for (int i = 0; i < 6; i++) {
                py.stats.used[i] = used[i];
            }
            py.flags.see_infra = 3;
            wearStartingKit();

            // the wielded weapon, made magical and identified
            game.treasure.list[1] = py.inventory[PlayerEquipment::Wield];
            magicTreasureMagicalAbility(1, 30);
            spellItemIdentifyAndRemoveRandomInscription(game.treasure.list[1]);
            py.inventory[PlayerEquipment::Wield] = game.treasure.list[1];

            (void) std::strcpy(py.inventory[0].inscription, "my snack");
            break;
        }
        case 2:
            py.misc.level = 40;
            py.equipment_count = 0;
            py.pack.unique_items = 0;
            dg.current_level = 10;
            break;
        default:
            py.misc.level = 45;
            py.max_score = 99999;
            py.flags.see_infra = 5;
            py.misc.fos = 50;
            py.misc.bth = -100;
            py.misc.stealth_factor = -2;
            py.misc.disarm = 200;
            dg.current_level = 1;
            wearStartingKit();
            break;
    }
}

static void dumpSheetFile(const char *filename) {
    FILE *file = std::fopen(filename, "r");
    if (file == nullptr) {
        std::fprintf(out, "sheet missing\n");
        return;
    }

    char buffer[512];
    while (std::fgets(buffer, sizeof(buffer), file) != nullptr) {
        size_t len = std::strlen(buffer);
        if (len > 0 && buffer[len - 1] == '\n') {
            buffer[len - 1] = '\0';
        }
        std::fprintf(out, "sheet\t%s\n", buffer);
    }
    std::fclose(file);
}

static void dumpSheets() {
    // The flavour names once; createScriptedCharacter() leaves them alone.
    game.magic_seed = 777;
    magicInitializeItemNames();

    std::vector<std::string> log;
    uiStubSetMessageLog(&log);

    char filename[] = "sheet.txt";
    constexpr uint32_t seed = 12345;
    int case_index = 0;

    for (int race_id = 0; race_id < PLAYER_MAX_RACES; race_id++) {
        int class_count = raceClassCount(race_id);
        for (bool is_male : {true, false}) {
            for (int class_choice = 0; class_choice < class_count; class_choice++) {
                int variant = case_index % 4;
                case_index++;

                py = Player_t{};
                dg.current_level = 0;
                (void) createScriptedCharacter(seed, race_id, is_male, class_choice, 0);
                setRandomSeed(seed + 1);
                applySheetVariant(variant);
                log.clear(); // creation's own messages

                std::fprintf(out, "sheetcase race=%d male=%d class_choice=%d variant=%d\n", race_id, (int) is_male, class_choice, variant);
                std::fprintf(out, "points\t%d\n", playerCalculateTotalPoints());

                (void) std::remove(filename);
                bool ok = outputPlayerCharacterToFile(filename);
                std::fprintf(out, "ok\t%d\n", (int) ok);
                dumpMessages(log);
                dumpSheetFile(filename);
            }
        }
    }

    // An existing file: refused, then replaced.
    std::fprintf(out, "sheetcase replace\n");
    uiStubSetConfirmAnswer(false);
    std::fprintf(out, "ok\t%d\n", (int) outputPlayerCharacterToFile(filename));
    dumpMessages(log);
    uiStubSetConfirmAnswer(true);
    std::fprintf(out, "ok\t%d\n", (int) outputPlayerCharacterToFile(filename));
    dumpMessages(log);
    uiStubSetConfirmAnswer(false);
    (void) std::remove(filename);

    uiStubSetMessageLog(nullptr);
}

// ---- scores --------------------------------------------------------------

struct ScoreScenario {
    const char *name;
    const char *entries;
};

#define SCORE_SCENARIO(name, entries) {name, entries},
static const ScoreScenario score_scenarios[] = {
#include "score_scenarios.inc"
};
#undef SCORE_SCENARIO

static std::vector<std::string> split(const std::string &text, char separator) {
    std::vector<std::string> parts;
    size_t start = 0;
    while (true) {
        size_t end = text.find(separator, start);
        if (end == std::string::npos) {
            parts.push_back(text.substr(start));
            return parts;
        }
        parts.push_back(text.substr(start, end - start));
        start = end + 1;
    }
}

// Mirrored in PersistenceTraceTests.EnterScore.
static void enterScore(int k, std::string const &spec) {
    // points:birth:gender:race:class:flags:died (died may hold ':')
    std::vector<std::string> f = split(spec, ':');
    for (size_t i = 7; i < f.size(); i++) {
        f[6] += ":" + f[i];
    }
    int points = std::atoi(f[0].c_str());

    py = Player_t{};
    dg.current_level = k % 5;
    py.misc.max_exp = points - 50 * dg.current_level;
    py.misc.date_of_birth = std::atoi(f[1].c_str());
    py.misc.gender = f[2] == "M";
    py.misc.race_id = (uint8_t) std::atoi(f[3].c_str());
    py.misc.class_id = (uint8_t) std::atoi(f[4].c_str());
    py.misc.level = (uint16_t) (1 + k % 40);
    py.misc.max_hp = (int16_t) (10 + k);
    py.misc.current_hp = (int16_t) (k % 3 == 0 ? -1 : 5 + k);
    py.misc.max_dungeon_depth = 0;
    if (k % 4 == 3) {
        (void) std::strcpy(py.misc.name, "Supercalifragilistic22");
    } else {
        (void) std::snprintf(py.misc.name, sizeof(py.misc.name), "P%d", k);
    }

    game.noscore = (int16_t) (f[5] == "n" ? 2 : 0);
    panic_save = f[5] == "p";
    (void) std::strcpy(game.character_died_from, f[6].c_str());

    recordNewHighScore();
}

static void dumpScoreFile() {
    FILE *fp = std::fopen(config::files::scores.c_str(), "rb");
    if (fp == nullptr) {
        std::fprintf(out, "scorefile missing\n");
        return;
    }

    // skip the version bytes (the original's format, not compared)
    (void) getc(fp);
    (void) getc(fp);
    (void) getc(fp);

    setFileptr(fp);

    HighScore_t score{};
    readHighScore(score);
    while (feof(fp) == 0) {
        std::fprintf(out, "entry\tpoints=%d birth=%d uid=%d mhp=%d chp=%d depth=%u level=%u deepest=%u gender=%c race=%u class=%u name=[%s] died=[%s]\n",
                     score.points, score.birth_date, score.uid, score.mhp, score.chp, score.dungeon_depth, score.level, score.deepest_dungeon_depth, score.gender, score.race,
                     score.character_class, score.name, score.died_from);
        readHighScore(score);
    }
    std::fclose(fp);
}

static void dumpScores() {
    std::vector<std::string> log;
    std::vector<std::string> screen;
    uiStubSetMessageLog(&log);

    for (auto const &scenario : score_scenarios) {
        std::fprintf(out, "scorecase %s\n", scenario.name);

        // An empty score file, as the game's installer leaves it.
        (void) std::remove(config::files::scores.c_str());
        FILE *fp = std::fopen(config::files::scores.c_str(), "wb");
        std::fclose(fp);

        std::vector<std::string> entries = split(scenario.entries, '|');
        for (size_t k = 0; k < entries.size(); k++) {
            enterScore((int) k, entries[k]);
        }
        panic_save = false;
        game.noscore = 0;
        dumpMessages(log);

        dumpScoreFile();

        uiStubSetScreenLog(&screen);
        uiStubSetScript(std::string(10, ' '), "");
        showScoresScreen();
        uiStubSetScreenLog(nullptr);
        for (auto const &text : screen) {
            std::fprintf(out, "screen\t%s\n", text.c_str());
        }
        screen.clear();
        dumpMessages(log);
    }

    (void) std::remove(config::files::scores.c_str());
    uiStubSetMessageLog(nullptr);
}

// ---- save ----------------------------------------------------------------

// What a fresh process holds before startMoria() calls loadGame().
// Mirrored in PersistenceTraceTests.FreshWorld.
static void resetWorld() {
    py = Player_t{};
    playerInitializeBaseExperienceLevels();
    dg = Dungeon_t{0, 0, {}, -1, 0, true, {}};
    game = Game_t{};
    // Game_t{} leaves the treasure list at 0 until treasureLinker() runs
    // (generateCave() links it before any use); the port's TreasureList
    // starts linked. Do what treasureLinker() (file-static in
    // dungeon_generate.cpp) does to the count, so a memory-only restore,
    // which never generates a level, compares equal.
    game.treasure.current_id = config::treasure::MIN_TREASURE_LIST_ID;
    for (auto &monster : monsters) {
        monster = Monster_t{};
    }
    next_free_monster_id = config::monsters::MON_MIN_INDEX_ID;
    monster_multiply_total = 0;
    for (auto &memory : creature_recall) {
        memory = Recall_t{};
    }
    std::memset(objects_identified, 0, sizeof(objects_identified));
    missiles_counter = 0;
    eof_flag = 0;
    panic_save = false;
    for (auto &store : stores) {
        store = Store_t{};
    }

    config::options::display_counts = true;
    config::options::find_bound = false;
    config::options::run_cut_corners = true;
    config::options::run_examine_corners = true;
    config::options::run_ignore_doors = false;
    config::options::run_print_self = false;
    config::options::highlight_seams = false;
    config::options::prompt_to_pickup = false;
    config::options::use_roguelike_keys = false;
    config::options::show_inventory_weights = false;
    config::options::error_beep_sound = true;
}

// A created character on a generated level 1, with state the save must
// carry (and some it must not). Mirrored in PersistenceTraceTests.BuildWorld.
static void buildWorld(uint32_t seed) {
    resetWorld();
    (void) createScriptedCharacter(seed, 1, true, 1, 0);

    missiles_counter = 0;
    setRandomSeed(seed + 7);
    dg.current_level = 1;
    generateCave();

    // the stores (M13): owners, two days of restocking, and counters
    storeInitializeOwners();
    storeMaintenance();
    storeMaintenance();
    stores[2].insults_counter = 3;
    stores[3].turns_left_before_closing = 4321;
    stores[4].good_purchases = 7;
    stores[5].bad_purchases = 2;
    stores[1].inventory[0].cost = 55;

    py.misc.date_of_birth = 1700000000;
    py.flags.status |= config::player::status::PY_SEARCH | config::player::status::PY_REST;
    py.flags.rest = 5;
    py.pack.heaviness = 3;
    py.weapon_is_heavy = true;
    py.carrying_light = true;
    (void) std::strcpy(py.inventory[0].inscription, "hello");

    creature_recall[5].kills = 3;
    creature_recall[5].attacks[2] = 4;
    creature_recall[17].movement = 0x123;
    creature_recall[17].spells = 0x80000001u;
    creature_recall[278].wake = 9;
    creature_recall[278].ignore = 2;

    config::options::find_bound = true;
    config::options::run_cut_corners = false;
    config::options::use_roguelike_keys = true;
    config::options::display_counts = false;
    config::options::highlight_seams = true;

    game.magic_seed = seed;
    game.town_seed = seed + 8762;
    game.character_generated = true;
    dg.game_turn = 1234;
    (void) std::strcpy(game.character_died_from, "(saved)");
}

static void dumpWorld(const char *label) {
    std::fprintf(out, "%s\n", label);
    dumpPlayer();
    std::fprintf(out, "pos\t%d,%d\n", py.pos.y, py.pos.x);
    std::fprintf(out, "max_score\t%d\n", py.max_score);
    std::fprintf(out, "date_of_birth\t%d\n", py.misc.date_of_birth);
    std::fprintf(out, "game\tmagic=%u town=%u noscore=%d panic=%d winner=%d dead=%d generated=%d saved=%d wizard=%d died=[%s] missiles=%d\n", game.magic_seed,
                 game.town_seed, game.noscore, (int) panic_save, (int) game.total_winner, (int) game.character_is_dead, (int) game.character_generated,
                 (int) game.character_saved, (int) game.to_be_wizard, game.character_died_from, missiles_counter);
    std::fprintf(out, "dg\tturn=%d level=%d height=%d width=%d max_rows=%d max_cols=%d\n", dg.game_turn, dg.current_level, dg.height, dg.width, dg.panel.max_rows,
                 dg.panel.max_cols);
    std::fprintf(out, "options\t%d%d%d%d%d%d%d%d%d%d%d\n", (int) config::options::run_cut_corners, (int) config::options::run_examine_corners,
                 (int) config::options::run_print_self, (int) config::options::find_bound, (int) config::options::prompt_to_pickup,
                 (int) config::options::use_roguelike_keys, (int) config::options::show_inventory_weights, (int) config::options::highlight_seams,
                 (int) config::options::run_ignore_doors, (int) config::options::error_beep_sound, (int) config::options::display_counts);

    uint32_t h = 2166136261u;
    auto mix = [&h](int32_t v) {
        h ^= (uint32_t) v;
        h *= 16777619u;
    };
    for (auto &row : dg.floor) {
        for (auto const &t : row) {
            mix(t.creature_id | (t.treasure_id << 8) | (t.feature_id << 16) | ((int) t.perma_lit_room << 24) | ((int) t.field_mark << 25) | ((int) t.permanent_light << 26) |
                ((int) t.temporary_light << 27));
        }
    }
    std::fprintf(out, "floor\t%u\n", h);

    std::fprintf(out, "treasures %d\n", game.treasure.current_id - config::treasure::MIN_TREASURE_LIST_ID);
    for (int i = config::treasure::MIN_TREASURE_LIST_ID; i < game.treasure.current_id; i++) {
        Inventory_t const &item = game.treasure.list[i];
        std::fprintf(out, "treasure %d id=%u sn=%u insc=[%s] flags=%u cat=%u sprite=%u misc=%d cost=%d sub=%u count=%u weight=%u to_hit=%d to_dam=%d ac=%d to_ac=%d dice=%ux%u depth=%u ident=%u\n",
                     i, item.id, item.special_name_id, item.inscription, item.flags, item.category_id, item.sprite, item.misc_use, item.cost, item.sub_category_id,
                     item.items_count, item.weight, item.to_hit, item.to_damage, item.ac, item.to_ac, item.damage.dice, item.damage.sides, item.depth_first_found,
                     item.identification);
    }

    for (int s = 0; s < MAX_STORES; s++) {
        Store_t const &store = stores[s];
        std::fprintf(out, "store %d own=%u ins=%d close=%d n=%u good=%u bad=%u\n", s, store.owner_id, store.insults_counter, store.turns_left_before_closing,
                     store.unique_items_counter, store.good_purchases, store.bad_purchases);
        for (int i = 0; i < store.unique_items_counter; i++) {
            Inventory_t const &item = store.inventory[i].item;
            std::fprintf(out, "stock %d.%d cost=%d id=%u sn=%u insc=[%s] flags=%u misc=%d count=%u to_hit=%d to_dam=%d to_ac=%d ident=%u\n", s, i, store.inventory[i].cost,
                         item.id, item.special_name_id, item.inscription, item.flags, item.misc_use, item.items_count, item.to_hit, item.to_damage, item.to_ac,
                         item.identification);
        }
    }

    std::fprintf(out, "monsters %d multiply=%d\n", next_free_monster_id - config::monsters::MON_MIN_INDEX_ID, monster_multiply_total);
    for (int i = config::monsters::MON_MIN_INDEX_ID; i < next_free_monster_id; i++) {
        Monster_t const &m = monsters[i];
        std::fprintf(out, "monster %d %u %d %d %d %d %d %u %u %u %u\n", i, m.creature_id, m.hp, m.sleep_count, m.pos.y, m.pos.x, m.speed, m.distance_from_player,
                     (unsigned) m.lit, m.stunned_amount, m.confused_amount);
    }

    for (int i = 0; i < MON_MAX_CREATURES; i++) {
        Recall_t const &r = creature_recall[i];
        if (r.movement || r.spells || r.kills || r.deaths || r.defenses || r.wake || r.ignore || r.attacks[0] || r.attacks[1] || r.attacks[2] || r.attacks[3]) {
            std::fprintf(out, "recall %d %u %u %u %u %u %u %u %u %u %u %u\n", i, r.movement, r.spells, r.kills, r.deaths, r.defenses, r.wake, r.ignore, r.attacks[0],
                         r.attacks[1], r.attacks[2], r.attacks[3]);
        }
    }
}

struct SaveScenario {
    const char *name;
    bool dead;
    bool eof;
    bool winner;
    bool panic;
    int noscore;
    bool to_be_wizard; // set after the reset, before loading
    bool confirm;      // what "Resurrect a dead character?" answers
};

static const SaveScenario save_scenarios[] = {
    {"alive", false, false, false, false, 0, false, false},
    {"dead", true, false, false, false, 0, false, false},
    {"hangup", true, true, false, false, 0, false, false},
    {"resurrect", true, false, false, false, 0, true, true},
    {"resurrect_declined", true, false, false, false, 0, true, false},
    {"retired", true, false, true, false, 0, true, true},
    {"winner_alive", false, false, true, false, 0, false, false},
    {"panic", false, false, false, true, 2, false, false},
    {"wizard_alive", false, false, false, false, 0, true, false},
};

static void dumpSaves() {
    std::vector<std::string> log;
    uiStubSetMessageLog(&log);

    uint32_t seed = 4001;
    for (auto const &scenario : save_scenarios) {
        std::fprintf(out, "savecase %s\n", scenario.name);
        buildWorld(seed++);

        game.character_is_dead = scenario.dead;
        game.total_winner = scenario.winner;
        panic_save = scenario.panic;
        game.noscore = (int16_t) scenario.noscore;
        eof_flag = scenario.eof ? 1 : 0;
        if (scenario.dead) {
            (void) std::strcpy(game.character_died_from, "a Kobold");
            py.misc.current_hp = -5;
            py.flags.food = -10;
            py.flags.poisoned = 7;
        }

        dumpWorld("before");
        log.clear(); // creation's own messages

        (void) std::remove(config::files::save_game.c_str());
        bool saved = saveGame();
        std::fprintf(out, "saved\t%d speed=%d rest=%d status=%u heaviness=%d turn=%d saved=%d dead=%d rnd=%u\n", (int) saved, py.flags.speed, py.flags.rest,
                     py.flags.status, py.pack.heaviness, dg.game_turn, (int) game.character_saved, (int) game.character_is_dead, getRandomSeed());
        dumpMessages(log);

        resetWorld();
        game.to_be_wizard = scenario.to_be_wizard;
        uiStubSetConfirmAnswer(scenario.confirm);

        bool generate = false;
        bool loaded = loadGame(generate);
        uiStubSetConfirmAnswer(false);
        std::fprintf(out, "loaded\t%d generate=%d\n", (int) loaded, (int) generate);
        dumpMessages(log);

        dumpWorld("after");
    }

    (void) std::remove(config::files::save_game.c_str());
    uiStubSetMessageLog(nullptr);
}

int main(int argc, char *argv[]) {
    if (argc < 2) {
        std::fprintf(stderr, "usage: persistence_dump <work_dir> [output_file]\n");
        return 1;
    }

    if (argc >= 3) {
        out = std::fopen(argv[2], "w");
        if (out == nullptr) {
            std::fprintf(stderr, "could not open output file: %s\n", argv[2]);
            return 1;
        }
    }

    if (chdir(argv[1]) != 0) {
        std::fprintf(stderr, "could not change to work directory: %s\n", argv[1]);
        return 1;
    }

    playerDumpSetOutput(out);

    initializeMonsterLevels();
    initializeTreasureLevels();

    dumpValues();
    dumpSheets();
    dumpScores();
    dumpSaves();

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
