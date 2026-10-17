// Player status-effects dump harness: verifies the .NET migration's port of
// the per-turn player status-effect updates (M7,
// Umoria.Core.Character.PlayerStatusEffects) against the real compiled C++
// game.
//
// The status-effect functions (playerUpdateHeroStatus(),
// playerFoodConsumption(), playerUpdatePoisonedState(), ...) are all
// `static` inside src/game_run.cpp, called only from playDungeon()'s main
// loop. Rather than un-static two dozen game functions, this target drops
// game_run.cpp from its source list and #includes it below, so this one
// translation unit sees the real, unmodified statics. (main.cpp and ui_io.cpp
// are dropped too; ui_io.cpp is replaced by the M6 headless stub, which
// also makes checkForNonBlockingKeyPress() report "no key".)
//
// Each scenario: create a real character via characterCreate() (shared M6
// code, char_create_common.cpp), place them on a small hand-built test level
// (no monsters, no objects on the floor), apply the scenario's setup, run
// playDungeon()'s level-entry preamble, then tick playDungeon()'s loop body
// -- status-effect portion only -- dumping one state line per tick, and the
// full player at the end. A scenario stops early when
// dg.generate_new_level is set (death, word of recall), as the real loop
// would.
//
// Loop-body steps deliberately NOT exercised (documented in
// migration-plan.md's M7 section):
//   - storeMaintenance() every 1000 turns (M13) -- the only scenario that
//     reaches turn 1000 (detect_enchantment) is on the town level, where the
//     real loop doesn't run it either.
//   - the MON_CHANCE_OF_NEW monster-spawn roll (M9) and updateMonsters(true)
//     / compactMonsters() -- the test level has no monsters, so every
//     updateMonsters(false) the status code calls is a no-op loop.
//   - the keypress-interrupt check (UI; headless means no key).
//   - random teleportation (playerTeleport, M8) -- no scenario sets
//     py.flags.teleport; the harness aborts if one ever does.
//   - executeInputCommands() / game.teleport_player (M8 and later).
//
// Usage: status_effects_dump [output_file]
// With no output_file, writes to stdout.

#include "../../src/game_run.cpp"

#include <cstdio>
#include <cstdlib>

#include "../char_create_dump/char_create_common.h"

static FILE *out = stdout;

// ---- test level ---------------------------------------------------------

static const Coord_t POS_ROOM = Coord_t{5, 10};     // inside the lit room
static const Coord_t POS_DOORWAY = Coord_t{9, 12};  // dark doorway tile of the lit room
static const Coord_t POS_DARK = Coord_t{15, 40};    // open unlit area

// Mirrored exactly by StatusEffectsTraceTests.BuildTestLevel().
static void buildTestLevel() {
    dg.height = SCREEN_HEIGHT;
    dg.width = SCREEN_WIDTH;
    dg.game_turn = 0;
    dg.generate_new_level = false;

    for (int y = 0; y < MAX_HEIGHT; y++) {
        for (int x = 0; x < MAX_WIDTH; x++) {
            dg.floor[y][x] = Tile_t{};
            dg.floor[y][x].feature_id = TILE_GRANITE_WALL;
        }
    }

    // open unlit area
    for (int y = 1; y <= 20; y++) {
        for (int x = 1; x <= 64; x++) {
            dg.floor[y][x].feature_id = TILE_DARK_FLOOR;
        }
    }

    // lit room with a wall ring, all flagged perma_lit_room
    for (int y = 2; y <= 9; y++) {
        for (int x = 3; x <= 21; x++) {
            Tile_t &tile = dg.floor[y][x];
            tile.perma_lit_room = true;
            bool ring = y == 2 || y == 9 || x == 3 || x == 21;
            tile.feature_id = ring ? TILE_GRANITE_WALL : TILE_LIGHT_FLOOR;
        }
    }

    // doorway in the bottom wall
    dg.floor[POS_DOORWAY.y][POS_DOORWAY.x].feature_id = TILE_DARK_FLOOR;

    game = Game_t{};
    next_free_monster_id = config::monsters::MON_MIN_INDEX_ID;
    for (auto &monster : monsters) {
        monster = Monster_t{};
    }
}

// ---- scenario helpers (mirrored in the C# test) -------------------------

// Copies the first pack item with category in [lo, hi] into `slot` (count 1).
// The pack copy stays put -- this is test setup, not inventoryWield().
static int equipCopyOfFirst(int lo, int hi, int slot) {
    for (int i = 0; i < py.pack.unique_items; i++) {
        if (py.inventory[i].category_id >= lo && py.inventory[i].category_id <= hi) {
            py.inventory[slot] = py.inventory[i];
            py.inventory[slot].items_count = 1;
            py.equipment_count++;
            return slot;
        }
    }
    std::fprintf(stderr, "scenario setup: no pack item in category %d..%d\n", lo, hi);
    std::exit(3);
}

// ---- scenarios ----------------------------------------------------------

typedef struct {
    const char *name;
    uint32_t seed;
    int race_id;
    bool is_male;
    int class_choice;
    Coord_t pos;
    int16_t level;
    int max_ticks;
    void (*setup)();
    void (*poke)(int tick); // called at the top of each tick, before game_turn++
} Scenario;

static void setupTimedEffects() {
    equipCopyOfFirst(TV_HAFTED, TV_SWORD, PlayerEquipment::Wield);
    py.flags.heroism = 6;
    py.flags.super_heroism = 4;
    py.flags.blind = 5;
    py.flags.confused = 7;
    py.flags.poisoned = 12;
    py.flags.fast = 5;
    py.flags.slow = 8;
    py.flags.paralysis = 3;
    py.flags.protect_evil = 5;
    py.flags.invulnerability = 6;
    py.flags.blessed = 7;
    py.flags.heat_resistance = 3;
    py.flags.cold_resistance = 4;
    py.flags.detect_invisible = 5;
    py.flags.timed_infra = 6;
}

static void pokeTimedEffects(int tick) {
    // Hallucination starts only while PY_BLIND is set, and ends before
    // blindness does: caveGetTileSymbol() draws randomNumber() for every
    // tile it renders while hallucinating and not blind, so any redraw
    // (drawDungeonPanel(), panelPutTile()) would consume game RNG in
    // proportion to screen geometry -- UI coupling deferred to M14 (see
    // migration-plan.md's M7 section).
    if (tick == 2) {
        py.flags.image = 3;
    }
    if (tick == 10) {
        py.stats.used[PlayerAttr::A_STR] = 3;
        py.flags.status |= config::player::status::PY_STR_WGT;
    }
    if (tick == 15) {
        py.flags.afraid = 6;
    }
    if (tick == 18) {
        py.flags.heroism = 3;
    }
    if (tick == 25) {
        py.flags.rest = 5;
        py.flags.status |= config::player::status::PY_REST;
    }
    if (tick == 27) {
        py.flags.confused = 2;
    }
    if (tick == 32) {
        py.stats.used[PlayerAttr::A_STR] = 18;
        py.flags.status |= config::player::status::PY_STR_WGT;
    }
}

static void setupStarvation() {
    py.flags.food = 2010;
    py.flags.food_digested = 15;
    py.flags.fast = 40;
    py.misc.current_hp = (int16_t) (py.misc.max_hp / 2);
}

static void setupRestRegen() {
    py.misc.current_hp = 1;
    py.misc.mana = 6;
    py.misc.current_mana = 0;
    py.misc.current_mana_fraction = 0;
    py.flags.rest = -SHRT_MAX;
    py.flags.status |= config::player::status::PY_REST;
    py.flags.food_digested--;
    py.flags.regenerate_hp = true;
}

static void pokeRestRegen(int tick) {
    if (tick == 60) {
        playerSearchOn();
    }
    if (tick == 90) {
        py.flags.poisoned = 4;
    }
    if (tick == 140) {
        py.flags.rest = 20;
        py.flags.status |= config::player::status::PY_REST;
    }
}

static void setupLightSource() {
    int slot = equipCopyOfFirst(TV_LIGHT, TV_LIGHT, PlayerEquipment::Light);
    py.inventory[slot].misc_use = 45;
}

static void pokeLightSource(int tick) {
    if (tick == 30) {
        py.running_tracker = 1;
    }
    if (tick == 50) {
        py.flags.blind = 3;
    }
    if (tick == 60) {
        py.inventory[PlayerEquipment::Light].misc_use = 2;
    }
}

static void setupRecallDown() {
    py.flags.word_of_recall = 4;
    py.misc.max_dungeon_depth = 7;
}

static void setupRecallUp() {
    py.flags.word_of_recall = 3;
}

static int16_t poison_con = 0;
static bool poison_invulnerable = false;

static void setupPoison() {
    py.flags.poisoned = 14;
    py.stats.used[PlayerAttr::A_CON] = (uint8_t) poison_con;
    if (poison_invulnerable) {
        py.flags.invulnerability = 20;
    }
}

static void setupDetectEnchantment() {
    py.misc.level = 40;
    py.flags.food = 15000;
    int slot = equipCopyOfFirst(TV_HAFTED, TV_SWORD, PlayerEquipment::Wield);
    // Starting kit is ID_KNOWN2|ID_STORE_BOUGHT, which itemEnchanted()
    // rejects -- clear it so the detection roll can actually hit.
    py.inventory[slot].to_hit = 3;
    py.inventory[slot].identification = 0;
    for (int i = 0; i < py.pack.unique_items; i++) {
        if (py.inventory[i].category_id >= TV_MIN_ENCHANT && py.inventory[i].category_id <= TV_MAX_ENCHANT) {
            py.inventory[i].to_ac = 2;
            py.inventory[i].identification = 0;
            break;
        }
    }
}

// Message/command-count interplay: printMessage() zeroes
// game.command_count (cancelling a repeated command), and
// printCharacterMovementState() maintains PY_REPEAT from it.
static void setupMessageInterrupt() {
    py.flags.protect_evil = 3;
    py.flags.heat_resistance = 5;
    py.flags.cold_resistance = 7;
}

static void pokeMessageInterrupt(int tick) {
    if (tick == 2) {
        game.command_count = 9;
        playerSearchOn(); // movement state with a count -> PY_REPEAT
    }
    if (tick == 8) {
        game.command_count = 3;
    }
    if (tick == 9) {
        py.flags.rest = 2;
        py.flags.status |= config::player::status::PY_REST;
    }
}

// ---- tick ---------------------------------------------------------------

static int countTiles(int which) {
    int count = 0;
    for (int y = 0; y < MAX_HEIGHT; y++) {
        for (int x = 0; x < MAX_WIDTH; x++) {
            Tile_t const &tile = dg.floor[y][x];
            bool flag = which == 0 ? tile.temporary_light : which == 1 ? tile.permanent_light : tile.field_mark;
            if (flag) {
                count++;
            }
        }
    }
    return count;
}

static void dumpTick(int tick) {
    std::fprintf(out,
                 "tick %d turn=%d rnd=%u st=%08x hp=%d.%u/%d mana=%d.%u/%d food=%d dig=%d spd=%d rest=%d "
                 "blind=%d conf=%d afraid=%d pois=%d fast=%d slow=%d image=%d para=%d pevil=%d invuln=%d "
                 "hero=%d shero=%d bless=%d heat=%d cold=%d detinv=%d seeinv=%d infra=%d tinfra=%d recall=%d "
                 "bth=%d bthb=%d ac=%d dac=%d depth=%d maxdepth=%u gen=%d dead=%d died=[%s] light=%d carry=%d "
                 "tlo=%d heavy=%d heaviness=%d cmd=%u run=%d tlit=%d plit=%d fm=%d\n",
                 tick, dg.game_turn, getRandomSeed(), py.flags.status, py.misc.current_hp, py.misc.current_hp_fraction,
                 py.misc.max_hp, py.misc.current_mana, py.misc.current_mana_fraction, py.misc.mana, py.flags.food,
                 py.flags.food_digested, py.flags.speed, py.flags.rest, py.flags.blind, py.flags.confused,
                 py.flags.afraid, py.flags.poisoned, py.flags.fast, py.flags.slow, py.flags.image, py.flags.paralysis,
                 py.flags.protect_evil, py.flags.invulnerability, py.flags.heroism, py.flags.super_heroism,
                 py.flags.blessed, py.flags.heat_resistance, py.flags.cold_resistance, py.flags.detect_invisible,
                 py.flags.see_invisible ? 1 : 0, py.flags.see_infra, py.flags.timed_infra, py.flags.word_of_recall,
                 py.misc.bth, py.misc.bth_with_bows, py.misc.ac, py.misc.display_ac, dg.current_level,
                 py.misc.max_dungeon_depth, dg.generate_new_level ? 1 : 0, game.character_is_dead ? 1 : 0,
                 game.character_died_from, py.inventory[PlayerEquipment::Light].misc_use, py.carrying_light ? 1 : 0,
                 py.temporary_light_only ? 1 : 0, py.weapon_is_heavy ? 1 : 0, py.pack.heaviness, game.command_count,
                 py.running_tracker, countTiles(0), countTiles(1), countTiles(2));
}

// playDungeon()'s level-entry preamble (game_run.cpp), minus display.
static void enterLevel() {
    playerInitializePlayerLight();
    playerUpdateMaxDungeonDepth();
    resetDungeonFlags();

    dg.panel.row = dg.panel.col = -1;
    dungeonResetView();

    if ((py.flags.status & config::player::status::PY_SEARCH) != 0u) {
        playerSearchOff();
    }

    updateMonsters(false);
}

// The status-effect portion of one playDungeon() loop iteration, in the
// original order. Skipped steps are marked (see the header comment).
static void tick() {
    dg.game_turn++;

    // [skipped: storeMaintenance(), new-monster roll]

    playerUpdateLightStatus();

    // Heroism and Super Heroism must precede anything that can damage player
    playerUpdateHeroStatus();

    int regen_amount = playerFoodConsumption();
    playerUpdateRegeneration(regen_amount);

    playerUpdateBlindness();
    playerUpdateConfusion();
    playerUpdateFearState();
    playerUpdatePoisonedState();
    playerUpdateSpeed();
    playerUpdateRestingState();

    // [skipped: keypress interrupt check]

    playerUpdateHallucination();
    playerUpdateParalysis();
    playerUpdateEvilProtection();
    playerUpdateInvulnerability();
    playerUpdateBlessedness();
    playerUpdateHeatResistance();
    playerUpdateColdResistance();
    playerUpdateDetectInvisible();
    playerUpdateInfraVision();
    playerUpdateWordOfRecall();

    // [skipped: random teleportation -- asserted unreachable]
    if (py.flags.teleport) {
        std::fprintf(stderr, "scenario reached random teleportation (M8) -- not covered\n");
        std::exit(4);
    }

    if ((py.flags.status & config::player::status::PY_STR_WGT) != 0u) {
        playerStrength();
    }

    if ((py.flags.status & config::player::status::PY_STUDY) != 0u) {
        printCharacterStudyInstruction();
    }

    playerUpdateStatusFlags();

    int chance = 10 + 750 / (5 + py.misc.level);
    if ((dg.game_turn & 0xF) == 0 && py.flags.confused == 0 && randomNumber(chance) == 1) {
        playerDetectEnchantment();
    }

    // [skipped: compactMonsters(), command input, game.teleport_player, updateMonsters(true)]
}

static void runScenario(Scenario const &s) {
    if (!createScriptedCharacter(s.seed, s.race_id, s.is_male, s.class_choice, 0)) {
        std::fprintf(stderr, "%s: key script not fully consumed -- flow mismatch\n", s.name);
        std::exit(2);
    }

    buildTestLevel();
    py.pos = s.pos;
    dg.current_level = s.level;

    s.setup();
    enterLevel();

    std::fprintf(out, "scenario %s\n", s.name);
    dumpTick(0);

    for (int t = 1; t <= s.max_ticks && !dg.generate_new_level; t++) {
        if (s.poke != nullptr) {
            s.poke(t);
        }
        tick();
        dumpTick(t);
    }

    std::fprintf(out, "final %s\n", s.name);
    dumpPlayer();
}

int main(int argc, char *argv[]) {
    if (argc >= 2) {
        out = std::fopen(argv[1], "w");
        if (out == nullptr) {
            std::fprintf(stderr, "could not open output file: %s\n", argv[1]);
            return 1;
        }
    }
    playerDumpSetOutput(out);

    // Human=0, Half-Elf=1, Elf=2, Halfling=3, Gnome=4, Dwarf=5, Half-Orc=6,
    // Half-Troll=7. Humans offer every class, so for race 0 class_choice is
    // the class id (0 Warrior, 1 Mage, 2 Priest, ...).
    const Scenario scenarios[] = {
        {"timed_effects", 12345, 0, true, 0, POS_ROOM, 1, 45, setupTimedEffects, pokeTimedEffects},
        {"starvation", 987654321, 3, false, 0, POS_DARK, 2, 300, setupStarvation, nullptr},
        {"rest_regen", 31337, 0, false, 1, POS_DOORWAY, 1, 400, setupRestRegen, pokeRestRegen},
        {"light_source", 12345, 6, false, 0, POS_DARK, 1, 75, setupLightSource, pokeLightSource},
        {"recall_down", 31337, 0, true, 2, POS_ROOM, 0, 20, setupRecallDown, nullptr},
        {"recall_up", 987654321, 5, false, 0, POS_ROOM, 5, 20, setupRecallUp, nullptr},
        {"detect_enchantment", 987654321, 1, true, 0, POS_ROOM, 0, 1200, setupDetectEnchantment, nullptr},
        {"message_interrupt", 12345, 0, false, 3, POS_DOORWAY, 1, 14, setupMessageInterrupt, pokeMessageInterrupt},
    };

    for (auto const &s : scenarios) {
        runScenario(s);
    }

    // Poison damage by constitution adjustment: one scenario per
    // playerStatAdjustmentConstitution() result (-4..4), plus one
    // invulnerable run (damage forced to 0).
    const int16_t cons[] = {3, 4, 5, 6, 10, 17, 50, 100, 118};
    char name[64];
    for (int16_t con : cons) {
        poison_con = con;
        poison_invulnerable = false;
        std::snprintf(name, sizeof(name), "poison_con_%d", con);
        runScenario({name, 31337, 7, true, 0, POS_ROOM, 3, 16, setupPoison, nullptr});
    }
    poison_con = 10;
    poison_invulnerable = true;
    runScenario({"poison_invulnerable", 31337, 7, true, 0, POS_ROOM, 3, 16, setupPoison, nullptr});

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
