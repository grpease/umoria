// Monster AI & combat dump harness: verifies the .NET migration's port of
// monster movement, monster melee and spells, the player's melee, and the
// damage-spell primitives (M9 -- Umoria.Core.Creatures.MonsterAi /
// MonsterAttacks / MonsterSpells / MonsterLife, Character.PlayerCombat,
// Items.InventoryDamage, Spells.DamageSpells / SpellEffects) against the
// real compiled C++ game.
//
// Same build pattern as player_actions_dump (M8): every function exercised
// is a global, so this links the real sources minus main.cpp and ui_io.cpp
// (replaced by the M6 headless stub) -- zero changes to src/.
//
// Each scenario: create a real character (M6), build the hand-made arena
// below (mirrored by MonsterCombatTraceTests.BuildArena()), apply the
// scenario's setup bits, place its monsters from a spec string, run
// playDungeon()'s level-entry panel/light reset, then execute a scripted
// command string. A command that takes a turn is followed, as in
// playDungeon()'s loop, by `dg.game_turn++; updateMonsters(true)` (unless
// it left game.player_free_turn set). One state line per step (player,
// RNG, hashes of the floor, treasure list, monster list, inventory and
// monster memory) plus one line listing every monster; the full player at
// the end. A scenario stops when dg.generate_new_level is set (death).
//
// Monster spec (space separated): `cid@y,x` then optional modifiers
// `zN` sleep count, `hN` hit points, `cN` confused, `sN` stunned;
// `F<cid>:<n>` fills n copies of creature cid along the dark room's rows.
//
// Script tokens (space separated, `*N` repeats a token N times):
//   turn-taking:  w  wait          m<d>  playerMove(d, true)
//                 b<d> playerBash() (key d)   o<d> playerOpenClosedObject()
//                 t<d> playerTunnel(d)
//                 B<d>:<e>:<n> spellFireBolt(dir, n, element e)
//                 L<d>:<e>:<n> spellFireBall(dir, n, element e)
//                 H<d>:<n> spellChangeMonsterHitPoints   D<d> spellDrainLife
//                 X<mask>:<n> spellDispelCreature
//                 Z monsterSleep(py.pos)                   A<n> spellAggravate
//   free:         E  spellEarthquake() -- free so that no monster then has to
//                    escape a wall with fewer than five open squares around
//                    it: monsterMoveOutOfWall() reads uninitialized
//                    directions[] there (see MonsterAi.MoveOutOfWall)
//                 c  py.flags.confuse_monster = true
//                 r<n> py.flags.rest = n     p<n> py.flags.paralysis = n
//                 K compactMonsters()        U updateMonsters(false)
//                 W<id> wield object id (replacing the weapon)
//                 x<n> py.flags.blind = n, with PY_BLIND set
//
// Usage: monster_combat_dump [output_file]

#include "../../src/headers.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

#include "../char_create_dump/char_create_common.h"
#include "../char_create_dump/ui_io_stub.h"

static FILE *out = stdout;

// ---- arena (mirrored by MonsterCombatTraceTests.BuildArena) ---------------

static void carve(int y1, int x1, int y2, int x2, uint8_t feature) {
    for (int y = y1; y <= y2; y++) {
        for (int x = x1; x <= x2; x++) {
            dg.floor[y][x].feature_id = feature;
        }
    }
}

static void carveRoom(int y1, int x1, int y2, int x2, bool lit) {
    for (int y = y1; y <= y2; y++) {
        for (int x = x1; x <= x2; x++) {
            Tile_t &tile = dg.floor[y][x];
            bool ring = y == y1 || y == y2 || x == x1 || x == x2;
            tile.perma_lit_room = lit;
            if (ring) {
                tile.feature_id = TILE_GRANITE_WALL;
            } else {
                tile.feature_id = lit ? TILE_LIGHT_FLOOR : TILE_DARK_FLOOR;
            }
        }
    }
}

static int placeObject(Coord_t coord, int object_id) {
    int id = popt();
    dg.floor[coord.y][coord.x].treasure_id = (uint8_t) id;
    inventoryItemCopyTo(object_id, game.treasure.list[id]);
    return id;
}

static void placeDoor(Coord_t coord, int object_id, int16_t misc_use) {
    int id = placeObject(coord, object_id);
    game.treasure.list[id].misc_use = misc_use;
    dg.floor[coord.y][coord.x].feature_id = object_id == config::dungeon::objects::OBJ_OPEN_DOOR ? TILE_CORR_FLOOR : TILE_BLOCKED_FLOOR;
}

static void buildArena(int16_t depth) {
    dg.height = MAX_HEIGHT;
    dg.width = MAX_WIDTH;
    dg.current_level = depth;
    dg.game_turn = 0;
    dg.generate_new_level = false;

    for (int y = 0; y < MAX_HEIGHT; y++) {
        for (int x = 0; x < MAX_WIDTH; x++) {
            dg.floor[y][x] = Tile_t{};
            bool edge = y == 0 || y == MAX_HEIGHT - 1 || x == 0 || x == MAX_WIDTH - 1;
            dg.floor[y][x].feature_id = edge ? TILE_BOUNDARY_WALL : TILE_GRANITE_WALL;
        }
    }

    game = Game_t{};
    for (auto &item : game.treasure.list) {
        inventoryItemCopyTo(config::dungeon::objects::OBJ_NOTHING, item);
    }
    game.treasure.current_id = config::treasure::MIN_TREASURE_LIST_ID;
    for (auto &monster : monsters) {
        monster = blank_monster;
    }
    next_free_monster_id = config::monsters::MON_MIN_INDEX_ID;
    monster_multiply_total = 0;
    hack_monptr = -1;
    for (auto &memory : creature_recall) {
        memory = Recall_t{};
    }

    // Room A (lit), a granite pillar and an enclosed 3x3 granite block
    carveRoom(1, 1, 21, 61, true);
    carve(8, 40, 8, 40, TILE_GRANITE_WALL);
    carve(14, 45, 16, 47, TILE_GRANITE_WALL);

    // door corridor east to room B: closed, locked, stuck and secret doors
    carve(11, 61, 11, 100, TILE_CORR_FLOOR);
    placeDoor(Coord_t{11, 65}, config::dungeon::objects::OBJ_CLOSED_DOOR, 0);
    placeDoor(Coord_t{11, 70}, config::dungeon::objects::OBJ_CLOSED_DOOR, 10);
    placeDoor(Coord_t{11, 75}, config::dungeon::objects::OBJ_CLOSED_DOOR, -5);
    placeDoor(Coord_t{11, 80}, config::dungeon::objects::OBJ_SECRET_DOOR, 0);

    // Room B (dark)
    carveRoom(1, 100, 21, 170, false);
    carve(11, 100, 11, 100, TILE_CORR_FLOOR);
}

// ---- setup (mirrored in the C# test) --------------------------------------

enum SetupBits {
    S_WEAPON = 1,
    S_ARMOUR = 2,
    S_TOUGH = 4,
    S_ITEMS = 8,
    S_PROT_EVIL = 16,
    S_CONFUSE = 32,
    S_SEE_INVIS = 64,
    S_INFRA = 128,
    S_AGGRAVATE = 256,
    S_SUSTAIN = 512,
    S_FREE_ACTION = 1024,
    S_RESIST = 2048,
    S_DARK = 4096,
    S_ARROWS = 8192,
    S_BLIND = 16384,
    S_FRAIL = 32768,
    S_GLYPH = 65536,
    S_CHEST = 131072,
    S_FLOOR_ITEMS = 262144,
    S_ENCHANTED = 524288,
    S_EXPERIENCED = 1048576,
    S_FAST = 2097152,
    S_LOCKED_CHEST = 4194304,
    S_MANA = 8388608,
    S_EGO_ARROWS = 16777216,
    S_NEAR_ITEMS = 33554432,
    S_MANY_ITEMS = 67108864,
    S_FEEBLE = 134217728,
    S_NIMBLE = 268435456,
    S_POOR = 536870912,
    S_GLYPH_UNDER = 1073741824,
};

static void equipCopyOfFirst(int lo, int hi, int slot) {
    for (int i = 0; i < py.pack.unique_items; i++) {
        if (py.inventory[i].category_id >= lo && py.inventory[i].category_id <= hi) {
            py.inventory[slot] = py.inventory[i];
            py.inventory[slot].items_count = 1;
            py.equipment_count++;
            return;
        }
    }
    std::fprintf(stderr, "scenario setup: no pack item in category %d..%d\n", lo, hi);
    std::exit(3);
}

static void equipObject(int object_id, int slot) {
    inventoryItemCopyTo(object_id, py.inventory[slot]);
    py.equipment_count++;
}

static void carry(int object_id, int count, int16_t misc_use) {
    Inventory_t item{};
    inventoryItemCopyTo(object_id, item);
    item.items_count = (uint8_t) count;
    if (misc_use >= 0) {
        item.misc_use = misc_use;
    }
    (void) inventoryCarryItem(item);
}

static void setup(int param) {
    Coord_t east = Coord_t{py.pos.y, py.pos.x + 1};

    if ((param & S_DARK) == 0) {
        equipCopyOfFirst(TV_LIGHT, TV_LIGHT, PlayerEquipment::Light);
        py.carrying_light = true;
    }
    if ((param & S_WEAPON) != 0) {
        equipCopyOfFirst(TV_HAFTED, TV_SWORD, PlayerEquipment::Wield);
    }
    if ((param & S_ARROWS) != 0) {
        equipObject(80, PlayerEquipment::Wield); // & Arrow~
        py.inventory[PlayerEquipment::Wield].items_count = 3;
    }
    if ((param & S_EGO_ARROWS) != 0) {
        // every slay/brand plus stat/search/stealth/speed/infra bonuses, so
        // that using the last one up reaches playerAdjustBonusesForItem()
        equipObject(80, PlayerEquipment::Wield); // & Arrow~
        Inventory_t &arrows = py.inventory[PlayerEquipment::Wield];
        arrows.items_count = 10;
        arrows.misc_use = 1;
        arrows.flags |= config::treasure::flags::TR_EGO_WEAPON | config::treasure::flags::TR_STR | config::treasure::flags::TR_DEX |
                        config::treasure::flags::TR_SEARCH | config::treasure::flags::TR_STEALTH | config::treasure::flags::TR_SPEED |
                        config::treasure::flags::TR_INFRA;
    }
    if ((param & S_ARMOUR) != 0) {
        equipObject(103, PlayerEquipment::Body);  // Soft Leather Armor
        equipObject(126, PlayerEquipment::Arm);   // & Small Leather Shield
        equipObject(95, PlayerEquipment::Head);   // & Hard Leather Cap
        equipObject(93, PlayerEquipment::Feet);   // & Pair of Hard Leather Boots
        equipObject(124, PlayerEquipment::Hands); // & Set of Leather Gloves
        equipObject(123, PlayerEquipment::Outer); // & Cloak
    }
    if ((param & S_ENCHANTED) != 0) {
        py.inventory[PlayerEquipment::Wield].to_hit = 3;
        py.inventory[PlayerEquipment::Wield].to_damage = 3;
        for (int slot : {PlayerEquipment::Body, PlayerEquipment::Arm, PlayerEquipment::Head, PlayerEquipment::Feet, PlayerEquipment::Hands, PlayerEquipment::Outer}) {
            py.inventory[slot].to_ac = 2;
        }
        py.inventory[PlayerEquipment::Body].flags |= config::treasure::flags::TR_RES_ACID;
    }
    if ((param & (S_WEAPON | S_ARMOUR | S_ENCHANTED | S_ARROWS | S_EGO_ARROWS)) != 0) {
        playerRecalculateBonuses();
    }
    if ((param & S_TOUGH) != 0) {
        // through the per-level table, so that level gain (which recomputes
        // max_hp from it) keeps the boost
        for (int i = 0; i < PLAYER_MAX_LEVEL; i++) {
            py.base_hp_levels[i] = (uint16_t) (600 + 10 * i);
        }
        py.misc.max_hp = 600;
        py.misc.current_hp = 600;
    }
    if ((param & S_FRAIL) != 0) {
        py.misc.current_hp = 3;
    }
    if ((param & S_ITEMS) != 0) {
        carry(268, 3, -1);  // & Flask~ of Oil
        carry(237, 2, -1);  // Cure Light Wounds (potion)
        carry(176, 2, -1);  // Identify (scroll)
        carry(282, 1, 6);   // Magic Missile (wand)
        carry(293, 1, 5);   // Light (staff)
        carry(140, 1, -1);  // Resist Fire (ring)
        carry(21, 2, -1);   // & Ration~ of Food
    }
    if ((param & S_MANY_ITEMS) != 0) {
        for (int ring = 132; ring <= 137; ring++) {
            carry(ring, 1, -1); // six rings
        }
        carry(270, 1, 4); // Lightning Bolts (wand)
        carry(84, 5, -1); // & Iron Spike~
    }
    if ((param & S_FEEBLE) != 0) {
        py.misc.plusses_to_damage = -30;
    }
    if ((param & S_NIMBLE) != 0) {
        py.stats.used[PlayerAttr::A_DEX] = 118;
    }
    if ((param & S_POOR) != 0) {
        py.misc.au = 3;
    }

    if ((param & S_PROT_EVIL) != 0) {
        py.flags.protect_evil = 50;
    }
    if ((param & S_CONFUSE) != 0) {
        py.flags.confuse_monster = true;
    }
    if ((param & S_SEE_INVIS) != 0) {
        py.flags.see_invisible = true;
    }
    if ((param & S_INFRA) != 0) {
        py.flags.see_infra = 5;
    }
    if ((param & S_AGGRAVATE) != 0) {
        py.flags.aggravate = true;
    }
    if ((param & S_SUSTAIN) != 0) {
        py.flags.sustain_str = true;
        py.flags.sustain_dex = true;
        py.flags.sustain_con = true;
        py.flags.sustain_int = true;
        py.flags.sustain_wis = true;
    }
    if ((param & S_FREE_ACTION) != 0) {
        py.flags.free_action = true;
    }
    if ((param & S_RESIST) != 0) {
        py.flags.resistant_to_fire = true;
        py.flags.resistant_to_cold = true;
        py.flags.resistant_to_acid = true;
        py.flags.resistant_to_light = true;
        py.flags.heat_resistance = 20;
        py.flags.cold_resistance = 20;
    }
    if ((param & S_BLIND) != 0) {
        py.flags.blind = 50;
        py.flags.status |= config::player::status::PY_BLIND;
    }
    if ((param & S_GLYPH) != 0) {
        (void) placeObject(east, config::dungeon::objects::OBJ_SCARE_MON);
    }
    if ((param & S_GLYPH_UNDER) != 0) {
        (void) placeObject(py.pos, config::dungeon::objects::OBJ_SCARE_MON);
    }
    if ((param & S_NEAR_ITEMS) != 0) {
        (void) placeObject(Coord_t{py.pos.y - 1, py.pos.x - 1}, 237);
        (void) placeObject(Coord_t{py.pos.y + 1, py.pos.x - 1}, 268);
        (void) placeObject(Coord_t{py.pos.y, py.pos.x - 2}, 176);
        (void) placeObject(Coord_t{py.pos.y - 2, py.pos.x}, 21);
        (void) placeObject(Coord_t{py.pos.y + 2, py.pos.x}, 103);
    }
    if ((param & (S_CHEST | S_LOCKED_CHEST)) != 0) {
        int id = placeObject(east, 331); // & Large Steel Chest
        if ((param & S_LOCKED_CHEST) != 0) {
            game.treasure.list[id].flags |= config::treasure::chests::CH_LOCKED | config::treasure::chests::CH_SUMMON;
        }
    }
    if ((param & S_FLOOR_ITEMS) != 0) {
        (void) placeObject(Coord_t{py.pos.y - 1, py.pos.x + 3}, 237);
        (void) placeObject(Coord_t{py.pos.y, py.pos.x + 3}, 176);
        (void) placeObject(Coord_t{py.pos.y + 1, py.pos.x + 3}, 268);
        (void) placeObject(Coord_t{py.pos.y, py.pos.x + 5}, 21);
        (void) placeObject(Coord_t{py.pos.y + 2, py.pos.x + 4}, 282);
    }
    if ((param & S_EXPERIENCED) != 0) {
        py.misc.exp = 3000;
        displayCharacterExperience();
    }
    if ((param & S_FAST) != 0) {
        py.flags.speed = -1;
    }
    if ((param & S_MANA) != 0) {
        // last: S_EXPERIENCED's level gain recomputes mana
        py.misc.mana = 30;
        py.misc.current_mana = 30;
    }
}

static void placeMonsters(const char *spec) {
    std::string s(spec);
    size_t pos = 0;
    while (pos < s.size()) {
        size_t end = s.find(' ', pos);
        if (end == std::string::npos) {
            end = s.size();
        }
        std::string token = s.substr(pos, end - pos);
        pos = end + 1;
        if (token.empty()) {
            continue;
        }

        if (token[0] == 'F') {
            int creature_id = 0;
            int count = 0;
            (void) std::sscanf(token.c_str() + 1, "%d:%d", &creature_id, &count);
            for (int k = 0; k < count; k++) {
                Coord_t coord = Coord_t{2 + k / 60, 105 + k % 60};
                (void) monsterPlaceNew(coord, creature_id, false);
            }
            continue;
        }

        int creature_id = 0;
        int y = 0;
        int x = 0;
        int consumed = 0;
        (void) std::sscanf(token.c_str(), "%d@%d,%d%n", &creature_id, &y, &x, &consumed);
        Coord_t coord = Coord_t{y, x};
        if (!monsterPlaceNew(coord, creature_id, false)) {
            std::fprintf(stderr, "could not place monster %s\n", token.c_str());
            std::exit(6);
        }
        Monster_t &monster = monsters[dg.floor[y][x].creature_id];

        const char *mods = token.c_str() + consumed;
        while (*mods != '\0') {
            char key = *mods++;
            int value = (int) std::strtol(mods, const_cast<char **>(&mods), 10);
            switch (key) {
                case 'z':
                    monster.sleep_count = (int16_t) value;
                    break;
                case 'h':
                    monster.hp = (int16_t) value;
                    break;
                case 'c':
                    monster.confused_amount = (uint8_t) value;
                    break;
                case 's':
                    monster.stunned_amount = (uint8_t) value;
                    break;
                default:
                    std::fprintf(stderr, "bad monster modifier in %s\n", token.c_str());
                    std::exit(7);
            }
        }
    }
}

static void resetOptions() {
    config::options::find_bound = false;
    config::options::run_cut_corners = true;
    config::options::run_examine_corners = true;
    config::options::run_ignore_doors = false;
    config::options::run_print_self = false;
    config::options::highlight_seams = false;
    config::options::prompt_to_pickup = false;
    uiStubSetConfirmAnswer(false);
}

// ---- dump ---------------------------------------------------------------

static uint32_t mix(uint32_t h, int32_t v) {
    h ^= (uint32_t) v;
    h *= 16777619u;
    return h;
}

static uint32_t hashFloor() {
    uint32_t h = 2166136261u;
    for (int y = 0; y < MAX_HEIGHT; y++) {
        for (int x = 0; x < MAX_WIDTH; x++) {
            Tile_t const &t = dg.floor[y][x];
            h = mix(h, t.creature_id | (t.treasure_id << 8) | (t.feature_id << 16) | ((int) t.perma_lit_room << 24) | ((int) t.field_mark << 25) | ((int) t.permanent_light << 26) |
                           ((int) t.temporary_light << 27));
        }
    }
    return h;
}

static uint32_t hashItem(uint32_t h, Inventory_t const &item) {
    h = mix(h, item.id);
    h = mix(h, item.special_name_id);
    h = mix(h, (int32_t) item.flags);
    h = mix(h, item.category_id);
    h = mix(h, item.sprite);
    h = mix(h, item.misc_use);
    h = mix(h, item.cost);
    h = mix(h, item.sub_category_id);
    h = mix(h, item.items_count);
    h = mix(h, item.weight);
    h = mix(h, item.to_hit);
    h = mix(h, item.to_damage);
    h = mix(h, item.ac);
    h = mix(h, item.to_ac);
    h = mix(h, item.damage.dice);
    h = mix(h, item.damage.sides);
    h = mix(h, item.depth_first_found);
    h = mix(h, item.identification);
    return h;
}

static uint32_t hashTreasure() {
    uint32_t h = 2166136261u;
    for (int i = config::treasure::MIN_TREASURE_LIST_ID; i < game.treasure.current_id; i++) {
        h = hashItem(h, game.treasure.list[i]);
    }
    return h;
}

static uint32_t hashInventory() {
    uint32_t h = 2166136261u;
    for (auto const &item : py.inventory) {
        h = hashItem(h, item);
    }
    return h;
}

static uint32_t hashRecall() {
    uint32_t h = 2166136261u;
    for (auto const &memory : creature_recall) {
        h = mix(h, (int32_t) memory.movement);
        h = mix(h, (int32_t) memory.spells);
        h = mix(h, memory.kills);
        h = mix(h, memory.deaths);
        h = mix(h, memory.defenses);
        h = mix(h, memory.wake);
        h = mix(h, memory.ignore);
        for (auto attack : memory.attacks) {
            h = mix(h, attack);
        }
    }
    return h;
}

static void dumpStep(int step, const char *cmd) {
    std::fprintf(out,
                 "step %d %s rnd=%u turn=%d pos=%d,%d free=%d cmd=%d hp=%d/%d mana=%d.%u exp=%d.%u/%d lvl=%d au=%d st=%08x stats=%d,%d,%d,%d,%d "
                 "para=%d blind=%d conf=%d slow=%d pois=%d afraid=%d spd=%d cm=%d gen=%d dead=%d died=[%s] win=%d tid=%d mon=%d mult=%d "
                 "pack=%d/%d light=%d floor=%08x treas=%08x inv=%08x recall=%08x\n",
                 step, cmd, getRandomSeed(), dg.game_turn, py.pos.y, py.pos.x, (int) game.player_free_turn, game.command_count, py.misc.current_hp, py.misc.max_hp,
                 py.misc.current_mana, py.misc.current_mana_fraction, py.misc.exp, py.misc.exp_fraction, py.misc.max_exp, py.misc.level, py.misc.au, py.flags.status,
                 py.stats.used[PlayerAttr::A_STR], py.stats.used[PlayerAttr::A_INT], py.stats.used[PlayerAttr::A_WIS], py.stats.used[PlayerAttr::A_DEX],
                 py.stats.used[PlayerAttr::A_CON], py.flags.paralysis, py.flags.blind, py.flags.confused, py.flags.slow, py.flags.poisoned, py.flags.afraid,
                 py.flags.speed, (int) py.flags.confuse_monster, (int) dg.generate_new_level, (int) game.character_is_dead, game.character_died_from,
                 (int) game.total_winner, game.treasure.current_id, next_free_monster_id, monster_multiply_total, py.pack.unique_items, py.pack.weight,
                 py.inventory[PlayerEquipment::Light].misc_use, hashFloor(), hashTreasure(), hashInventory(), hashRecall());

    std::fprintf(out, "mons");
    for (int i = config::monsters::MON_MIN_INDEX_ID; i < next_free_monster_id; i++) {
        Monster_t const &m = monsters[i];
        std::fprintf(out, " %d:%d@%d,%d/%d/%d/%d/%d/%d/%d/%d", i, m.creature_id, m.pos.y, m.pos.x, m.hp, m.sleep_count, m.stunned_amount, m.confused_amount, m.speed,
                     m.distance_from_player, (int) m.lit);
    }
    std::fprintf(out, "\n");
}

// ---- script runner ------------------------------------------------------

static int step_number = 0;

static void withDirectionKey(int dir, void (*command)()) {
    uiStubSetScript(std::string(1, (char) ('0' + dir)), "");
    command();
    if (!uiStubScriptConsumed()) {
        std::fprintf(stderr, "direction key not consumed\n");
        std::exit(4);
    }
}

static void runToken(const std::string &token) {
    char op = token[0];
    int a = 0;
    int b = 0;
    int c = 0;
    (void) std::sscanf(token.c_str() + 1, "%d:%d:%d", &a, &b, &c);

    game.player_free_turn = false;
    bool takes_turn = true;

    switch (op) {
        case 'w':
            break;
        case 'm':
            playerMove(a, true);
            break;
        case 'b':
            withDirectionKey(a, playerBash);
            break;
        case 'o':
            withDirectionKey(a, playerOpenClosedObject);
            break;
        case 't':
            playerTunnel(a);
            break;
        case 'B':
            spellFireBolt(py.pos, a, c, b, "bolt");
            break;
        case 'L':
            spellFireBall(py.pos, a, c, b, "ball");
            break;
        case 'H':
            (void) spellChangeMonsterHitPoints(py.pos, a, b);
            break;
        case 'D':
            (void) spellDrainLifeFromMonster(py.pos, a);
            break;
        case 'X':
            (void) spellDispelCreature(a, b);
            break;
        case 'E':
            spellEarthquake();
            takes_turn = false;
            break;
        case 'Z':
            (void) monsterSleep(py.pos);
            break;
        case 'A':
            (void) spellAggravateMonsters(a);
            break;
        case 'c':
            py.flags.confuse_monster = true;
            takes_turn = false;
            break;
        case 'r':
            py.flags.rest = (int16_t) a;
            takes_turn = false;
            break;
        case 'p':
            py.flags.paralysis = (int16_t) a;
            takes_turn = false;
            break;
        case 'K':
            (void) compactMonsters();
            takes_turn = false;
            break;
        case 'U':
            updateMonsters(false);
            takes_turn = false;
            break;
        case 'x':
            py.flags.blind = (int16_t) a;
            py.flags.status |= config::player::status::PY_BLIND;
            takes_turn = false;
            break;
        case 'W':
            inventoryItemCopyTo(a, py.inventory[PlayerEquipment::Wield]);
            playerRecalculateBonuses();
            takes_turn = false;
            break;
        default:
            std::fprintf(stderr, "bad script token: %s\n", token.c_str());
            std::exit(5);
    }

    if (game.teleport_player && !dg.generate_new_level) {
        playerTeleport(100);
    }

    if (takes_turn && !game.player_free_turn && !dg.generate_new_level) {
        dg.game_turn++;
        updateMonsters(true);
    }

    dumpStep(++step_number, token.c_str());
}

static void runScript(const char *script) {
    std::string s(script);
    size_t pos = 0;
    while (pos < s.size() && !dg.generate_new_level) {
        size_t end = s.find(' ', pos);
        if (end == std::string::npos) {
            end = s.size();
        }
        std::string token = s.substr(pos, end - pos);
        pos = end + 1;
        if (token.empty()) {
            continue;
        }

        int repeat = 1;
        size_t star = token.find('*');
        if (star != std::string::npos) {
            repeat = std::atoi(token.c_str() + star + 1);
            token = token.substr(0, star);
        }

        for (int i = 0; i < repeat && !dg.generate_new_level; i++) {
            runToken(token);
        }
    }
}

// ---- scenarios ----------------------------------------------------------

typedef struct {
    const char *name;
    uint32_t seed;
    int race_id;
    bool is_male;
    int class_choice;
    Coord_t pos;
    int16_t depth;
    int setup;
    const char *monsters;
    const char *script;
} Scenario;

static void runScenario(Scenario const &s) {
    resetOptions();

    if (!createScriptedCharacter(s.seed, s.race_id, s.is_male, s.class_choice, 0)) {
        std::fprintf(stderr, "scenario %s: character creation script not consumed\n", s.name);
        std::exit(2);
    }

    buildArena(s.depth);
    py.pos = s.pos;
    dg.floor[s.pos.y][s.pos.x].creature_id = 1;

    setup(s.setup);
    placeMonsters(s.monsters);

    // generateCave()'s panel setup, then playDungeon()'s preamble:
    // panel.row/col = -1 and dungeonResetView().
    dg.panel = Panel_t{};
    dg.panel.max_rows = (int16_t) ((dg.height / SCREEN_HEIGHT) * 2 - 2);
    dg.panel.max_cols = (int16_t) ((dg.width / SCREEN_WIDTH) * 2 - 2);
    dg.panel.row = dg.panel.col = -1;
    dungeonResetView();

    std::fprintf(out, "scenario %s\n", s.name);
    step_number = 0;
    dumpStep(0, "start");

    runScript(s.script);

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

    initializeMonsterLevels();
    initializeTreasureLevels();

    const Coord_t P = Coord_t{11, 20};
    const Coord_t DOORS = Coord_t{11, 85};
    const Coord_t DARK = Coord_t{11, 120};

    const int TOUGH = S_TOUGH;
    const int FIGHTER = S_WEAPON | S_ARMOUR | S_TOUGH;
    const int LOADED = S_WEAPON | S_ARMOUR | S_TOUGH | S_ITEMS;

    // Human=0 ... Half-Troll=7; for race 0 class_choice is the class id
    // (0 Warrior, 1 Mage, 2 Priest, 3 Rogue, 4 Ranger, 5 Paladin).
    const Scenario scenarios[] = {
        // ---- melee: the player attacking ----
        {"melee_orc", 12345, 0, true, 0, P, 3, FIGHTER, "77@11,24", "w*3 m6*6 w*2"},
        {"melee_kobolds", 31337, 5, false, 0, P, 2, FIGHTER, "16@11,25 16@9,25 22@13,25", "w*4 m6*8 w*3"},
        {"melee_bare_hands", 987654321, 0, true, 3, P, 1, TOUGH | S_FEEBLE, "1@11,21", "m6*12"},
        {"melee_mage_dark", 4242, 0, false, 1, DARK, 3, S_WEAPON | S_TOUGH | S_DARK, "77@11,122", "U m6*8 w*2"},
        {"melee_arrows", 777, 0, true, 4, P, 3, S_ARROWS | S_TOUGH, "77@11,21", "m6*8"},
        {"melee_enchanted", 12345, 0, true, 5, P, 4, FIGHTER | S_ENCHANTED, "57@11,21 46@10,21", "m6*6 m9*6"},
        {"melee_kill_level", 31337, 0, true, 0, P, 3, FIGHTER | S_ENCHANTED, "47@11,21h1 18@10,21h1 81@12,21h1", "m6 m9 m3 w"},
        {"bash_monster", 987654321, 7, true, 0, P, 3, FIGHTER, "77@11,21", "b6*10 w*6"},
        {"bash_dark", 4242, 0, true, 0, DARK, 3, S_ARMOUR | S_TOUGH | S_DARK, "16@11,121", "b6*8"},
        {"confuse_touch", 777, 0, false, 0, P, 3, FIGHTER | S_CONFUSE | S_EXPERIENCED | S_ENCHANTED, "77@11,22h300", "w m6 w*5 c m6*2 w*3 c m6 c m6 c m6 c m6 c m6"},
        {"confuse_on_hit", 12345, 0, true, 2, P, 3, S_TOUGH | S_CONFUSE, "16@11,21 63@12,21", "w*4 c w*3 c w*3 c w*3 c w*3 c w*3"},
        {"protect_evil", 31337, 0, true, 0, P, 6, TOUGH | S_PROT_EVIL | S_EXPERIENCED, "77@11,21 16@10,21 29@12,21 37@12,19", "w*12"},
        {"death_by_orcs", 987654321, 3, false, 1, P, 5, S_FRAIL, "77@11,21 77@10,21 77@12,21", "w*10"},

        // ---- movement ----
        {"sleepers", 4242, 0, true, 0, P, 3, TOUGH, "77@11,30z200 16@5,30z50 22@15,30z500 37@11,40z120", "w*30"},
        {"sleepers_aggravate", 777, 0, true, 0, P, 3, TOUGH | S_AGGRAVATE, "77@11,30z200 16@5,30z50", "w*6"},
        {"sleepers_resting", 12345, 3, true, 2, P, 3, TOUGH, "77@11,30z30 16@5,30z30", "r40 w*20 r0 w*4"},
        {"sleepers_troll", 31337, 7, true, 0, P, 3, TOUGH, "77@11,30z30 16@5,25z30", "w*25"},
        {"random_movers", 987654321, 0, true, 0, P, 2, TOUGH, "11@11,30 5@9,30 30@13,30 41@5,30 70@17,30", "w*25"},
        {"fast_player", 4242, 0, false, 0, P, 2, TOUGH | S_FAST, "17@11,26 77@5,30 21@15,30", "w*20"},
        {"worms_breed", 777, 0, true, 0, P, 3, LOADED, "17@11,25 32@5,40 45@15,40 36@11,35", "w*80"},
        {"compact_mainloop", 12345, 0, true, 0, P, 3, TOUGH, "F8:107 278@20,165 17@11,25 17@9,25 53@13,25 13@11,30", "w*20 K w*10 K w*10"},
        {"compact_in_update", 31337, 0, true, 0, P, 3, TOUGH, "F8:110 17@11,25 17@9,25 53@13,25 13@11,30 13@12,30", "w*120"},
        {"eats_other", 987654321, 0, true, 0, P, 3, TOUGH | S_ITEMS, "13@11,22 13@10,22 20@11,24", "w*25"},
        {"picks_up", 4242, 0, false, 0, P, 1, TOUGH | S_FLOOR_ITEMS, "0@11,27 20@9,27", "w*20"},
        {"doors_kobold", 777, 0, true, 0, DOORS, 2, TOUGH, "16@11,66h30 31@11,63h30", "w*45"},
        {"doors_bashers", 12345, 0, true, 0, Coord_t{11, 73}, 2, TOUGH, "29@11,67h60 42@11,68h60 77@11,77", "w*40"},
        {"glyph", 31337, 0, true, 0, P, 3, TOUGH | S_GLYPH, "77@11,23 16@11,24", "w*15"},
        {"ghosts", 987654321, 0, true, 0, P, 5, TOUGH | S_ITEMS, "34@11,25 60@9,25 87@13,25", "w*30"},
        {"ghosts_seen", 4242, 0, true, 0, P, 5, TOUGH | S_ITEMS | S_SEE_INVIS, "34@11,25 60@9,25 12@13,22", "w*30"},
        {"out_of_wall", 777, 0, true, 0, Coord_t{8, 36}, 3, TOUGH, "77@8,40 21@15,46h3", "w*4"},
        {"dig_out", 12345, 0, false, 0, Coord_t{8, 36}, 3, TOUGH, "77@15,46h200 22@8,40", "w*6"},
        {"infravision", 31337, 0, true, 0, DARK, 3, TOUGH | S_DARK | S_INFRA, "77@11,124 16@11,130 12@9,123", "w*15"},
        {"blind_player", 987654321, 0, true, 0, P, 3, TOUGH | S_BLIND, "77@11,24", "w*8"},
        {"quylthulg", 4242, 0, true, 0, P, 20, TOUGH, "174@11,21", "w*60"},

        // ---- monster attack types ----
        {"atk_confusion", 777, 0, true, 0, P, 1, TOUGH, "8@11,21 74@10,21", "w*10"},
        {"atk_fear", 12345, 0, true, 0, P, 2, TOUGH, "26@11,21", "w*10"},
        {"atk_poison", 31337, 0, true, 0, P, 3, TOUGH, "38@11,21 71@12,21", "w*10"},
        {"atk_paralysis", 987654321, 0, true, 0, P, 5, TOUGH, "61@11,21 18@10,21", "w*14"},
        {"atk_paralysis_free", 4242, 0, true, 0, P, 5, TOUGH | S_FREE_ACTION, "61@11,21 18@10,21", "w*10"},
        {"atk_lose_stats", 777, 0, true, 0, P, 7, TOUGH, "73@11,21 47@10,21 45@12,21 87@10,20 227@12,20", "w*20"},
        {"atk_lose_stats_sustained", 12345, 0, true, 0, P, 7, TOUGH | S_SUSTAIN, "73@11,21 47@10,21 45@12,21 87@10,20 227@12,20", "w*30"},
        {"atk_shrieker", 31337, 0, true, 0, P, 2, TOUGH, "19@11,21 77@5,40z300 16@17,40z300", "w*12"},
        {"atk_disenchant", 987654321, 0, true, 0, P, 5, S_WEAPON | S_TOUGH | S_ENCHANTED, "67@11,21", "w*40"},
        {"atk_elements", 4242, 0, true, 0, P, 6, LOADED, "51@11,21 62@10,21 69@12,21 59@10,19", "w*20"},
        {"atk_elements_resist", 777, 0, true, 0, P, 6, LOADED | S_RESIST, "51@11,21 62@10,21 69@12,21 59@10,19", "w*12"},
        {"atk_lightning", 12345, 0, true, 0, P, 27, LOADED | S_MANY_ITEMS, "211@11,21", "w*30"},
        {"atk_blind", 31337, 0, true, 0, P, 7, TOUGH, "81@11,21", "w*10"},
        {"atk_steal_gold", 987654321, 0, true, 0, P, 2, TOUGH | S_POOR, "0@11,21 23@10,21 40@12,21", "w*12"},
        {"atk_steal_items", 4242, 0, true, 0, P, 2, TOUGH | S_ITEMS, "4@11,21 277@10,21", "w*14"},
        {"atk_paralysed_thief", 777, 0, true, 0, P, 2, TOUGH | S_ITEMS, "4@11,21 0@10,21", "p20 w*8"},
        {"atk_eat", 12345, 0, true, 0, P, 5, TOUGH | S_ITEMS, "20@11,21 60@10,21 49@12,21 64@10,20", "w*20"},
        {"atk_drain_exp", 31337, 0, true, 2, P, 12, TOUGH | S_EXPERIENCED, "119@11,21 193@10,21", "w*14"},
        {"atk_lich", 987654321, 0, true, 1, P, 34, LOADED | S_EXPERIENCED, "239@11,22", "w*20"},
        {"atk_insults", 4242, 0, true, 0, P, 3, TOUGH, "48@11,21 277@10,21", "w*10"},

        // ---- monster spells ----
        {"spell_novices", 777, 0, true, 1, P, 3, TOUGH, "25@11,26 24@9,26", "w*40"},
        {"spell_drain_mana", 12345, 0, false, 1, P, 3, TOUGH | S_EXPERIENCED | S_MANA, "18@11,22 47@10,23 67@12,23 39@13,22", "w*30"},
        {"spell_casters", 31337, 0, true, 0, P, 12, TOUGH, "102@11,26 121@9,26 120@13,26", "w*40"},
        {"spell_casters_free", 987654321, 0, true, 0, P, 12, TOUGH | S_FREE_ACTION, "102@11,26 121@9,26", "w*30"},
        {"spell_summoners", 4242, 0, true, 0, P, 13, TOUGH, "116@11,26 131@9,26 159@13,30", "w*40"},
        {"spell_undead", 777, 0, true, 0, P, 35, TOUGH, "246@11,26", "w*40"},
        {"breath_frost_gas", 12345, 0, true, 0, P, 16, LOADED, "137@11,24 156@9,24 13@11,21 13@10,21", "w*30"},
        {"breath_acid_light", 31337, 0, true, 0, P, 25, LOADED | S_MANY_ITEMS | S_ENCHANTED, "176@11,24 201@9,24 13@11,21", "w*30"},
        {"breath_fire", 987654321, 0, true, 0, P, 27, LOADED, "210@11,24 179@9,24 13@11,21 13@12,21", "w*30"},
        {"breath_dragon", 4242, 0, true, 0, P, 36, LOADED | S_RESIST, "253@11,24 13@11,21 13@10,22", "w*30"},

        // ---- the player's damage spells ----
        {"bolts", 777, 0, true, 1, P, 3, TOUGH, "11@11,24 15@11,27 56@11,30 69@11,33 77@11,36", "B6:5:20 B6:4:20 B6:1:15 B6:0:12 B6:3:30 B6:2:30 B6:6:30 B6:5:200"},
        {"bolts_miss", 12345, 0, true, 1, P, 3, TOUGH, "77@5,24", "B8:5:20 B2:5:20 B4:1:5"},
        {"balls", 31337, 0, true, 1, P, 3, TOUGH | S_FLOOR_ITEMS, "77@11,27 16@10,27 16@12,27 13@11,28 22@11,40", "L6:5:30 L6:1:30 L6:4:20 L6:3:25 L6:2:20 L6:6:60"},
        {"ball_wall", 987654321, 0, true, 1, Coord_t{11, 55}, 3, TOUGH, "16@10,59 16@12,59", "L6:5:40 L6:5:40"},
        {"monster_hp_spells", 4242, 0, true, 2, P, 5, TOUGH, "77@11,24 63@9,20 22@11,30", "H6:10 H6:-5 H6:100 D8 D6 D6 D2"},
        {"dispel", 777, 0, true, 2, P, 5, TOUGH, "63@11,24 63@9,24 77@13,24", "X8:30 X8:30 X4:60"},
        {"earthquake", 12345, 0, true, 0, P, 5, TOUGH | S_FLOOR_ITEMS, "77@11,24 16@9,15 34@13,20 174@11,14 8@12,21 8@12,19 8@10,19 77@9,21 16@13,23 16@12,25 22@10,17 29@8,20", "E E E E"},
        {"sleep_adjacent", 31337, 0, true, 2, P, 5, TOUGH, "77@11,21 16@10,21 8@12,21 63@12,19", "U Z w*4"},
        {"aggravate", 987654321, 0, true, 0, P, 3, TOUGH, "77@11,30z200 16@5,30z50 22@15,50", "A20 w*3"},
        {"balrog_win", 4242, 0, true, 0, P, 50, TOUGH, "278@11,24", "H6:5000"},

        // ---- added after a coverage pass ----
        {"undead_confused", 777, 0, true, 0, P, 7, TOUGH, "63@11,25c8 34@1,10c30 87@13,25c5", "w*15"},
        {"stun_recovery", 12345, 0, true, 0, P, 34, TOUGH, "77@11,25s3 239@9,25s6 22@13,25s1", "w*10"},
        {"glyph_break", 31337, 0, true, 0, Coord_t{11, 62}, 50, TOUGH | S_ITEMS | S_GLYPH, "277@11,64", "w*40"},
        {"glyph_under", 987654321, 0, true, 0, P, 3, TOUGH | S_GLYPH_UNDER, "8@11,21 77@10,21", "w*20"},
        {"atk_descriptions", 4242, 0, true, 0, P, 9, TOUGH | S_ITEMS, "9@11,21 94@10,21 97@12,21", "w*15"},
        {"atk_disenchant_plain", 777, 0, true, 0, P, 5, FIGHTER, "67@11,21", "w*15"},
        {"crits", 12345, 0, true, 0, P, 5, FIGHTER | S_ENCHANTED | S_EXPERIENCED, "62@11,21h30000", "W69 m6*80"},
        {"bash_quylthulg", 31337, 0, true, 0, P, 20, FIGHTER | S_EXPERIENCED, "174@11,21h30000", "b6*30"},
        {"bash_stun_cap", 987654321, 7, true, 0, P, 5, FIGHTER, "77@11,21h250", "b6*20"},
        {"ego_arrows", 4242, 0, true, 0, P, 30, TOUGH | S_EGO_ARROWS | S_EXPERIENCED, "216@11,21h300s20 63@10,21h300s20 58@12,21h300s20 77@10,20h300s20 51@12,20h300s20 11@11,19h300s20", "m9*3 m3*3 m8*3 m2*3 m4*3 m6*6"},
        {"bolts_resist", 777, 0, true, 1, P, 5, TOUGH, "56@11,22 69@11,26", "B6:4:30 B6:4:30 B6:4:30 B6:5:30 L6:5:30 L6:4:30"},
        {"breath_kill_order", 12345, 0, true, 0, P, 16, LOADED | S_NEAR_ITEMS, "13@11,21 13@12,21 137@11,24 13@10,21", "w*30"},
        {"breath_kills_player", 31337, 0, true, 0, P, 16, S_ITEMS | S_FRAIL, "137@11,23", "w*20"},
        {"cannibal_oozes", 987654321, 0, true, 0, P, 22, TOUGH, "186@11,25 186@9,25 13@10,26 13@12,26 17@11,27 17@10,24", "w*150"},

        {"atk_steal_nimble", 777, 0, true, 0, P, 2, TOUGH | S_ITEMS | S_NIMBLE, "4@11,21 0@10,21", "w*14"},
        {"atk_sustain_dex_wis", 12345, 0, true, 0, P, 7, TOUGH | S_SUSTAIN, "45@11,21 45@12,21 87@10,21 87@10,20", "w*30"},
        {"vision_lost", 31337, 0, true, 0, Coord_t{11, 58}, 3, TOUGH, "77@8,45z500 16@14,50z500", "U m6*5 x20 U"},
        {"balrog_melee", 987654321, 0, true, 0, P, 50, TOUGH | S_ITEMS, "278@11,21", "w*2"},
        {"breath_corner", 4242, 0, true, 0, Coord_t{2, 3}, 16, LOADED, "137@4,6 156@2,7", "w*25"},

        // ---- objects: chests and the M8-deferred traps ----
        {"chest_open", 777, 0, true, 0, P, 5, TOUGH | S_CHEST, "", "o6 m6"},
        {"chest_locked", 12345, 0, true, 3, P, 5, TOUGH | S_LOCKED_CHEST, "", "o6*6 m6"},
    };

    for (auto const &s : scenarios) {
        runScenario(s);
    }

    // The fire, acid and corroding-gas traps (types 13, 12, 10), deferred
    // in M8 until the damage primitives existed.
    const int traps[] = {10, 12, 13};
    const int setups[] = {LOADED, LOADED | S_RESIST, S_ITEMS | S_FRAIL};
    const uint32_t seeds[] = {12345, 31337, 987654321};
    char name[64];
    for (int t = 0; t < 3; t++) {
        for (int v = 0; v < 3; v++) {
            std::snprintf(name, sizeof(name), "trap_%d_v%d", traps[t], v);
            std::string trap_name(name);

            resetOptions();
            (void) createScriptedCharacter(seeds[v], 0, true, 0, 0);
            buildArena(3);
            py.pos = P;
            dg.floor[P.y][P.x].creature_id = 1;
            setup(setups[v]);
            dungeonSetTrap(Coord_t{P.y, P.x + 1}, traps[t] - 1);
            dg.panel = Panel_t{};
            dg.panel.max_rows = (int16_t) ((dg.height / SCREEN_HEIGHT) * 2 - 2);
            dg.panel.max_cols = (int16_t) ((dg.width / SCREEN_WIDTH) * 2 - 2);
            dg.panel.row = dg.panel.col = -1;
            dungeonResetView();

            std::fprintf(out, "scenario %s\n", trap_name.c_str());
            step_number = 0;
            dumpStep(0, "start");
            runScript("m6 m4 m6 m4 m6");
            std::fprintf(out, "final %s\n", trap_name.c_str());
            dumpPlayer();
        }
    }

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
