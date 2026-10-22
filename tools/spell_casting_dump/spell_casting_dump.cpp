// Spell effects & casting dump harness: verifies the .NET migration's port of
// the rest of spells.cpp and the casting layer on top of it (M10 --
// Umoria.Core.Spells.DetectionSpells / MonsterStatusSpells / TerrainSpells /
// SpellEffects / SpellCasting, Character.PlayerMagic, PlayerSpells.GainSpells,
// PlayerCore.GetDirectionWithMemory) against the real compiled C++ game.
//
// Same build pattern as monster_combat_dump (M9): every function exercised
// is a global, so this links the real sources minus main.cpp and ui_io.cpp
// (replaced by the M6 headless stub) -- zero changes to src/. The arena,
// monster spec language, hashes and per-step dump are M9's; this adds a
// third line per step with the spell-related player state.
//
// The casting commands are driven through their real prompts: the key
// script for a token is handed to the stub's getKeyInput(), so the real
// inventoryGetInputForItemId() (book / item choice), spellGetId() (spell
// letter), getDirectionWithMemory() and getTileCharacter() (genocide) read
// it. getInputConfirmation() answers with the current y/n setting. The
// script need not be used up (a spell that fails never reads its direction
// key); how many keys were read is dumped instead.
//
// Script tokens (space separated, `*N` repeats a token N times -- only with
// digits after the `*`, which is also the spell menu's list key):
//   turn-taking:  w  wait            m<d>  playerMove(d, true)
//                 C<b><keys> getAndCastMagicSpell()    P<b><keys> pray()
//                 G<keys>    playerGainSpells() (study)
//                    <b>: 1-4 = the key for the b-th book in the pack's book
//                    range, 0 = no book key (the command fails earlier),
//                    ~ = <ESCAPE> at the book prompt.
//                    <keys>: literal keys; ~ = <ESCAPE>, & = the key for the
//                    first staff/wand in the pack (recharge).
//                 S<n>:<a>:<b> call spell n directly (see directSpell())
//   free:         y / n   what getInputConfirmation() answers from now on
//                 M<n> py.misc.current_mana = n
//                 x<n> py.flags.blind = n, with PY_BLIND set
//                 f<n> py.flags.confused = n
//                 U updateMonsters(false)
//                 K<n> inventoryDestroyItem(n), if n is in the pack
//                 B    destroy every spell book
//                 Q<n> the first staff/wand's charges = n
//                 I    spell-identify the first staff/wand
//
// Usage: spell_casting_dump [output_file]

#include "../../src/headers.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

#include "../char_create_dump/char_create_common.h"
#include "../char_create_dump/ui_io_stub.h"

static FILE *out = stdout;

// ---- arena (M9's, mirrored by SpellCastingTraceTests.BuildArena) -----------

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
    S_TOUGH = 1,
    S_DARK = 2,
    S_BLIND = 4,
    S_HURT = 8,
    S_BOOKS = 16,
    S_LEARN = 32,
    S_MANA = 64,
    S_STAFF = 128,
    S_TRAPS = 256,
    S_CURSED = 512,
    S_AILING = 1024,
    S_DRAINED = 2048,
    S_SUSTAIN = 4096,
    S_RUBBLE = 8192,
    S_WEAPON = 16384,
    S_ITEMS = 32768,
    S_NEAR_ITEMS = 65536,
    S_UNKNOWN = 131072,
    S_RUBBLE_ROW = 262144,
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

static void setup(int param, int32_t exp) {
    if ((param & S_DARK) == 0) {
        equipCopyOfFirst(TV_LIGHT, TV_LIGHT, PlayerEquipment::Light);
        py.carrying_light = true;
    }
    if ((param & S_WEAPON) != 0) {
        equipObject(37, PlayerEquipment::Wield); // & Broad Sword
        py.inventory[PlayerEquipment::Wield].to_hit = 2;
    }
    if ((param & S_CURSED) != 0) {
        equipObject(103, PlayerEquipment::Body); // Soft Leather Armor
        equipObject(140, PlayerEquipment::Left); // Resist Fire (ring)
        py.inventory[PlayerEquipment::Body].flags |= config::treasure::flags::TR_CURSED;
        py.inventory[PlayerEquipment::Left].flags |= config::treasure::flags::TR_CURSED;
        if ((param & S_WEAPON) != 0) {
            py.inventory[PlayerEquipment::Wield].flags |= config::treasure::flags::TR_CURSED;
        }
    }
    if ((param & (S_WEAPON | S_CURSED)) != 0) {
        playerRecalculateBonuses();
    }
    if ((param & S_TOUGH) != 0) {
        for (int i = 0; i < PLAYER_MAX_LEVEL; i++) {
            py.base_hp_levels[i] = (uint16_t) (600 + 10 * i);
        }
        py.misc.max_hp = 600;
        py.misc.current_hp = 600;
    }
    if ((param & S_ITEMS) != 0) {
        carry(268, 3, -1); // & Flask~ of Oil
        carry(237, 2, -1); // Cure Light Wounds (potion)
        carry(176, 2, -1); // Identify (scroll)
        carry(140, 1, -1); // Resist Fire (ring)
    }
    if ((param & S_STAFF) != 0) {
        carry(293, 1, 5); // Light (staff)
        carry(282, 1, 6); // Magic Missile (wand)
    }
    if ((param & S_BOOKS) != 0) {
        int first = classes[py.misc.class_id].class_to_use_mage_spells == config::spells::SPELL_TYPE_MAGE ? 319 : 323;
        for (int book = first; book < first + 3; book++) {
            carry(book, 1, -1);
        }
    }
    if (exp > 0) {
        // playerGainLevel() halves the excess on each level gained, so
        // repeat until the level settles
        int level;
        do {
            level = py.misc.level;
            py.misc.exp = exp;
            displayCharacterExperience();
        } while (py.misc.level != level);
    }
    if ((param & S_DRAINED) != 0) {
        py.misc.exp = py.misc.exp / 3;
    }
    if ((param & S_LEARN) != 0) {
        Spell_t const *spells = magic_spells[py.misc.class_id - 1];
        int order = 0;
        py.flags.spells_learnt = 0;
        for (int i = 0; i < 31; i++) {
            if (spells[i].level_required <= py.misc.level) {
                py.flags.spells_learnt |= 1L << i;
                py.flags.spells_learned_order[order++] = (uint8_t) i;
            }
        }
        py.flags.new_spells_to_learn = 0;
    }
    if ((param & S_HURT) != 0) {
        py.misc.current_hp = (int16_t) (py.misc.max_hp / 4);
    }
    if ((param & S_AILING) != 0) {
        py.flags.poisoned = 30;
        py.flags.afraid = 20;
        for (int stat = 0; stat < 6; stat++) {
            py.stats.current[stat] = (uint8_t) (py.stats.max[stat] - 2);
            playerSetAndUseStat(stat);
        }
    }
    if ((param & S_SUSTAIN) != 0) {
        py.flags.sustain_str = true;
        py.flags.sustain_int = true;
        py.flags.sustain_wis = true;
        py.flags.sustain_dex = true;
        py.flags.sustain_con = true;
        py.flags.sustain_chr = true;
    }
    if ((param & S_BLIND) != 0) {
        py.flags.blind = 50;
        py.flags.status |= config::player::status::PY_BLIND;
    }
    if ((param & S_TRAPS) != 0) {
        dungeonSetTrap(Coord_t{py.pos.y, py.pos.x + 3}, 0);
        dungeonSetTrap(Coord_t{py.pos.y - 2, py.pos.x + 5}, 4);
        dungeonSetTrap(Coord_t{py.pos.y + 1, py.pos.x - 1}, 7);
        placeDoor(Coord_t{py.pos.y + 1, py.pos.x + 2}, config::dungeon::objects::OBJ_SECRET_DOOR, 0);
        int chest = placeObject(Coord_t{py.pos.y - 2, py.pos.x - 2}, 331); // & Large Steel Chest
        game.treasure.list[chest].flags |= config::treasure::chests::CH_LOCKED | 0x00000010;
        chest = placeObject(Coord_t{py.pos.y - 1, py.pos.x + 1}, 331);
        game.treasure.list[chest].flags |= 0x00000020;
        (void) placeObject(Coord_t{py.pos.y + 3, py.pos.x - 5}, config::dungeon::objects::OBJ_DOWN_STAIR);
        (void) placeObject(Coord_t{py.pos.y - 3, py.pos.x - 5}, config::dungeon::objects::OBJ_UP_STAIR);
        (void) placeObject(Coord_t{py.pos.y - 3, py.pos.x + 6}, config::dungeon::objects::OBJ_GOLD_LIST);
        (void) placeObject(Coord_t{py.pos.y + 4, py.pos.x + 4}, 237);
        (void) placeObject(Coord_t{py.pos.y - 4, py.pos.x + 2}, 21);
    }
    if ((param & S_NEAR_ITEMS) != 0) {
        (void) placeObject(Coord_t{py.pos.y - 1, py.pos.x - 1}, 237);
        (void) placeObject(Coord_t{py.pos.y + 1, py.pos.x + 1}, 268);
        (void) placeObject(Coord_t{py.pos.y, py.pos.x - 1}, 176);
    }
    if ((param & S_UNKNOWN) != 0) {
        // two stacks each of two unknown potion kinds -- dungeon-found and
        // store-bought don't stack -- for identify's merge (one kind with
        // the dungeon stack first, one with it second), and a cursed ring
        // for its "damned" inscription
        carry(243, 2, -1); // Constitution (potion)
        Inventory_t bought{};
        inventoryItemCopyTo(243, bought);
        itemIdentifyAsStoreBought(bought);
        (void) inventoryCarryItem(bought);
        inventoryItemCopyTo(251, bought); // Dexterity (potion)
        itemIdentifyAsStoreBought(bought);
        (void) inventoryCarryItem(bought);
        carry(251, 2, -1);
        Inventory_t ring{};
        inventoryItemCopyTo(140, ring); // Resist Fire (ring)
        ring.flags |= config::treasure::flags::TR_CURSED;
        (void) inventoryCarryItem(ring);
    }
    if ((param & S_RUBBLE_ROW) != 0) {
        for (int x = py.pos.x + 2; x <= py.pos.x + 16; x++) {
            dungeonPlaceRubble(Coord_t{py.pos.y, x});
        }
    }
    if ((param & S_RUBBLE) != 0) {
        dungeonPlaceRubble(Coord_t{py.pos.y, py.pos.x + 2});
        dungeonPlaceRubble(Coord_t{py.pos.y - 2, py.pos.x});
        dungeonPlaceRubble(Coord_t{py.pos.y + 2, py.pos.x});
    }
    if ((param & S_MANA) != 0) {
        // last: level gain recomputes mana
        py.misc.mana = 200;
        py.misc.current_mana = 200;
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

// Keys read by the last casting command, out of those scripted. Dumped
// rather than required to match: a spell that fails its roll never reads
// its direction key, but both sides must read the same number.
static size_t keys_used = 0;
static size_t keys_given = 0;

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
                 "step %d %s rnd=%u turn=%d pos=%d,%d free=%d cmd=%d hp=%d/%d mana=%d.%u/%d exp=%d.%u/%d lvl=%d au=%d st=%08x stats=%d,%d,%d,%d,%d,%d "
                 "para=%d blind=%d conf=%d slow=%d pois=%d afraid=%d spd=%d gen=%d dead=%d win=%d tid=%d mon=%d mult=%d "
                 "pack=%d/%d light=%d floor=%08x treas=%08x inv=%08x recall=%08x\n",
                 step, cmd, getRandomSeed(), dg.game_turn, py.pos.y, py.pos.x, (int) game.player_free_turn, game.command_count, py.misc.current_hp, py.misc.max_hp,
                 py.misc.current_mana, py.misc.current_mana_fraction, py.misc.mana, py.misc.exp, py.misc.exp_fraction, py.misc.max_exp, py.misc.level, py.misc.au,
                 py.flags.status, py.stats.used[PlayerAttr::A_STR], py.stats.used[PlayerAttr::A_INT], py.stats.used[PlayerAttr::A_WIS], py.stats.used[PlayerAttr::A_DEX],
                 py.stats.used[PlayerAttr::A_CON], py.stats.used[PlayerAttr::A_CHR], py.flags.paralysis, py.flags.blind, py.flags.confused, py.flags.slow,
                 py.flags.poisoned, py.flags.afraid, py.flags.speed, (int) dg.generate_new_level, (int) game.character_is_dead, (int) game.total_winner,
                 game.treasure.current_id, next_free_monster_id, monster_multiply_total, py.pack.unique_items, py.pack.weight,
                 py.inventory[PlayerEquipment::Light].misc_use, hashFloor(), hashTreasure(), hashInventory(), hashRecall());

    std::fprintf(out, "spl keys=%d/%d learnt=%08x worked=%08x forgot=%08x new=%d fast=%d bless=%d dinv=%d pevil=%d invuln=%d hres=%d cres=%d prev=%d\n",
                 (int) keys_used, (int) keys_given, py.flags.spells_learnt, py.flags.spells_worked, py.flags.spells_forgotten, py.flags.new_spells_to_learn, py.flags.fast, py.flags.blessed,
                 py.flags.detect_invisible, py.flags.protect_evil, py.flags.invulnerability, py.flags.heat_resistance, py.flags.cold_resistance, (int) py.prev_dir);

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

static char firstInRange(int lo, int hi, const std::string &token) {
    int i, j;
    if (!inventoryFindRange(lo, hi, i, j)) {
        std::fprintf(stderr, "token %s: nothing in category range %d..%d\n", token.c_str(), lo, hi);
        std::exit(8);
    }
    return (char) ('a' + i);
}

// Turns a token's key text into the stub's key script (see the header).
static std::string resolveKeys(const std::string &token, size_t start, int book_category) {
    std::string keys;
    size_t k = start;
    if (book_category != 0) {
        char book = token[k++];
        if (book == '~') {
            keys += (char) ESCAPE;
        } else if (book >= '1' && book <= '4') {
            keys += (char) (firstInRange(book_category, TV_NEVER, token) + (book - '1'));
        } else if (book != '0') {
            std::fprintf(stderr, "bad book key in %s\n", token.c_str());
            std::exit(9);
        }
    }
    for (; k < token.size(); k++) {
        char c = token[k];
        if (c == '~') {
            keys += (char) ESCAPE;
        } else if (c == '&') {
            // with nothing left to recharge the spell returns before the
            // prompt, so the key is never read
            int i, j;
            keys += inventoryFindRange(TV_STAFF, TV_WAND, i, j) ? (char) ('a' + i) : 'a';
        } else {
            keys += c;
        }
    }
    return keys;
}

static void withKeys(const std::string &keys, void (*command)()) {
    uiStubSetScript(keys, "");
    command();
    keys_used = uiStubKeysConsumed();
    keys_given = keys.size();
}

static void directSpell(int n, int a, int b) {
    switch (n) {
        case 1:
            (void) spellDetectTreasureWithinVicinity();
            break;
        case 2:
            (void) spellDetectObjectsWithinVicinity();
            break;
        case 3:
            (void) spellDetectInvisibleCreaturesWithinVicinity();
            break;
        case 4:
            (void) spellDarkenArea(py.pos);
            break;
        case 5:
            (void) spellSurroundPlayerWithTraps();
            break;
        case 6:
            (void) spellSurroundPlayerWithDoors();
            break;
        case 7:
            spellLightLine(py.pos, a);
            break;
        case 8:
            spellStarlite(py.pos);
            break;
        case 9:
            (void) spellDisarmAllInDirection(py.pos, a);
            break;
        case 10:
            (void) spellDestroyDoorsTrapsInDirection(py.pos, a);
            break;
        case 11:
            (void) spellBuildWall(py.pos, a);
            break;
        case 12:
            (void) spellCloneMonster(py.pos, a);
            break;
        case 13:
            (void) spellMassGenocide();
            break;
        case 14:
            (void) spellSpeedAllMonsters(a);
            break;
        case 15:
            (void) spellMassPolymorph();
            break;
        case 16:
            spellLoseSTR();
            break;
        case 17:
            spellLoseINT();
            break;
        case 18:
            spellLoseWIS();
            break;
        case 19:
            spellLoseDEX();
            break;
        case 20:
            spellLoseCON();
            break;
        case 21:
            spellLoseCHR();
            break;
        case 22:
            (void) spellEnchantItem(py.inventory[PlayerEquipment::Wield].to_hit, (int16_t) a);
            break;
        case 23:
            (void) spellRemoveCurseFromAllWornItems();
            break;
        case 24:
            (void) spellRestorePlayerLevels();
            break;
        case 25:
            (void) spellChangePlayerHitPoints(a);
            break;
        case 26:
            (void) spellSpeedMonster(py.pos, a, b);
            break;
        case 27:
            (void) playerCureConfusion();
            break;
        case 28:
            (void) playerCureBlindness();
            break;
        case 29:
            (void) spellDetectEvil();
            break;
        case 30:
            (void) spellDetectMonsters();
            break;
        case 31:
            spellDestroyArea(py.pos);
            break;
        case 32:
            (void) spellLightArea(py.pos);
            break;
        case 33:
            (void) spellWallToMud(py.pos, a);
            break;
        case 34:
            spellMapCurrentArea();
            break;
        case 35:
            (void) spellSleepAllMonsters();
            break;
        case 36:
            (void) spellTurnUndead();
            break;
        case 37:
            (void) spellTeleportAwayMonsterInDirection(py.pos, a);
            break;
        case 38:
            (void) spellPolymorphMonster(py.pos, a);
            break;
        case 39:
            (void) spellConfuseMonster(py.pos, a);
            break;
        case 40:
            (void) spellSleepMonster(py.pos, a);
            break;
        case 41:
            spellCreateFood();
            break;
        case 42:
            spellWardingGlyph();
            break;
        case 43:
            (void) spellDestroyAdjacentDoorsTraps();
            break;
        case 44:
            (void) spellDetectTrapsWithinVicinity();
            break;
        case 45:
            (void) spellDetectSecretDoorssWithinVicinity();
            break;
        case 46:
            (void) spellSlowPoison();
            break;
        default:
            std::fprintf(stderr, "bad direct spell %d\n", n);
            std::exit(5);
    }
}

static void runToken(const std::string &token) {
    char op = token[0];
    int a = 0;
    int b = 0;
    int c = 0;

    game.player_free_turn = false;
    bool takes_turn = true;
    keys_used = 0;
    keys_given = 0;

    switch (op) {
        case 'w':
            break;
        case 'm':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            playerMove(a, true);
            break;
        case 'C':
            withKeys(resolveKeys(token, 1, TV_MAGIC_BOOK), getAndCastMagicSpell);
            break;
        case 'P':
            withKeys(resolveKeys(token, 1, TV_PRAYER_BOOK), pray);
            break;
        case 'G':
            withKeys(resolveKeys(token, 1, 0), playerGainSpells);
            break;
        case 'S':
            (void) std::sscanf(token.c_str() + 1, "%d:%d:%d", &a, &b, &c);
            directSpell(a, b, c);
            break;
        case 'y':
            uiStubSetConfirmAnswer(true);
            takes_turn = false;
            break;
        case 'n':
            uiStubSetConfirmAnswer(false);
            takes_turn = false;
            break;
        case 'M':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            py.misc.current_mana = (int16_t) a;
            takes_turn = false;
            break;
        case 'x':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            py.flags.blind = (int16_t) a;
            py.flags.status |= config::player::status::PY_BLIND;
            takes_turn = false;
            break;
        case 'f':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            py.flags.confused = (int16_t) a;
            takes_turn = false;
            break;
        case 'U':
            updateMonsters(false);
            takes_turn = false;
            break;
        case 'K':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            // (only within the pack: on an empty one inventoryDestroyItem()
            // writes py.inventory[-1])
            if (a < py.pack.unique_items) {
                inventoryDestroyItem(a);
            }
            takes_turn = false;
            break;
        case 'B': {
            int i, j;
            while (inventoryFindRange(TV_MAGIC_BOOK, TV_PRAYER_BOOK, i, j)) {
                inventoryDestroyItem(i);
            }
            takes_turn = false;
            break;
        }
        case 'Q': {
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            int i, j;
            if (inventoryFindRange(TV_STAFF, TV_WAND, i, j)) {
                py.inventory[i].misc_use = (int16_t) a;
            }
            takes_turn = false;
            break;
        }
        case 'I': {
            int i, j;
            if (inventoryFindRange(TV_STAFF, TV_WAND, i, j)) {
                spellItemIdentifyAndRemoveRandomInscription(py.inventory[i]);
            }
            takes_turn = false;
            break;
        }
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

        // `*N` repeats, but only with digits after it: '*' is also the
        // spell menu's list key
        int repeat = 1;
        size_t star = token.rfind('*');
        if (star != std::string::npos && star + 1 < token.size() && token.find_first_not_of("0123456789", star + 1) == std::string::npos) {
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
    int32_t exp;
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

    setup(s.setup, s.exp);
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
    keys_used = 0;
    keys_given = 0;
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

    const int T = S_TOUGH;
    const int CASTER = S_TOUGH | S_BOOKS | S_LEARN | S_MANA;
    const int MAXEXP = 9999999;

    // Human=0; class_choice is the class id (1 Mage, 2 Priest, 3 Rogue,
    // 4 Ranger, 5 Paladin).
    const Scenario scenarios[] = {
        // ---- the casting layer ----
        {"mage_study", 12345, 0, true, 1, P, 3, T, 0, "77@11,25", "Ga C1a6 C1a6 C1a6 G w"},
        {"priest_study", 31337, 0, true, 2, P, 3, T, 0, "", "G G w"},
        {"mage_study_blind", 777, 0, true, 1, P, 3, T | S_BLIND, 0, "", "G"},
        {"mage_study_confused", 4242, 0, false, 1, P, 3, T, 0, "", "f5 G"},
        {"mage_study_levels", 987654321, 0, true, 1, P, 3, T | S_BOOKS, 40000, "", "Gacb~ G~ Ga~"},
        {"priest_study_levels", 12345, 0, false, 2, P, 3, T, 40000, "", "G G"},
        {"cast_refusals", 31337, 0, true, 1, P, 3, T | S_LEARN, 3000, "", "f5 C0 f0 x5 C0"},
        {"cast_in_dark", 777, 0, true, 1, DARK, 3, T | S_LEARN | S_DARK, 3000, "", "C0 P0"},
        {"priest_casts_mage", 4242, 0, true, 2, P, 3, T | S_LEARN, 3000, "", "C0 P~"},
        {"mage_prays", 987654321, 0, true, 1, P, 3, T | S_LEARN, 3000, "", "P0"},
        {"cast_menu_keys", 12345, 0, true, 1, P, 3, T | S_LEARN, 3000, "77@11,25", "C~ C1~ C1z~ C1Z~ C1*a6 C1g~ C1a~ y C1A6 n C1A~"},
        {"cast_low_mana", 31337, 0, true, 1, P, 3, T | S_LEARN, 6000, "77@11,25", "M1 n C1g6 y C1g6 M0 C1a6 M2 C1a6 w*3"},
        {"pray_low_mana", 777, 0, false, 2, P, 3, T | S_LEARN | S_HURT, 6000, "", "M0 n P1b y P1b P1c M1 P1b P1b"},
        {"cast_fail_often", 4242, 0, true, 1, P, 3, T | S_LEARN | S_MANA, 3000, "77@11,25h300", "C1a6*12"},
        {"unknown_book", 987654321, 0, true, 1, P, 3, T | S_BOOKS, 0, "", "C2"},

        // ---- every mage spell ----
        {"mage_book1", 12345, 0, true, 1, P, 5, CASTER | S_HURT | S_TRAPS, MAXEXP, "77@11,25 16@5,20 34@15,22", "M999 C1a6*3 M999 C1b*3 M999 C1d*3 M999 C1e*3 M999 C1f*3 M999 C1g6*3 M999 C1c*3"},
        {"mage_book2", 31337, 0, true, 1, P, 5, CASTER | S_CURSED | S_AILING | S_TRAPS, MAXEXP, "77@11,25 16@11,30 63@5,20 22@15,20", "M999 C2a6*3 M999 C2b6*3 M999 C2c*3 M999 C2d8*3 M999 C2e*3 M999 C2g*3 M999 C2h2*3 M999 C2i4*3 M999 C2f*3"},
        {"mage_book3", 777, 0, true, 1, P, 5, CASTER | S_STAFF | S_ITEMS, MAXEXP, "77@11,25 16@11,30 22@12,21 13@10,19", "M999 C3a*3 M999 C3b&*3 M999 C3c*3 M999 C3d6*3 M999 C3ea*3 M999 C3f*3 M999 C3g6*3 M999 C3h6*3 M999 C3a*3"},
        {"mage_book4", 4242, 0, true, 1, P, 5, CASTER | S_STAFF, MAXEXP, "77@11,25 16@11,28 16@9,28 278@15,30z30000 22@5,24", "M999 C4a6*3 M999 C4b&*3 M999 C4c6*3 M999 C4d*3 M999 C4e6*3 M999 C4gk*6 M999 C4gB*6 M999 C4f*3"},
        {"mage_teleport_self", 987654321, 0, false, 1, P, 5, CASTER, MAXEXP, "77@11,25", "C2f C1c C2f M0 y C4e6 C4e6"},
        {"mage_wall_to_mud", 12345, 0, true, 1, Coord_t{11, 58}, 5, CASTER | S_RUBBLE, MAXEXP, "149@11,55", "C2i6 C2i4 C2i8 C2i2 C2i6 C2i6"},
        {"mage_recharge_until_broken", 31337, 0, true, 1, P, 5, CASTER | S_STAFF, MAXEXP, "", "C4b&*8 C3b&*4"},
        {"mage_create_food", 777, 0, true, 1, P, 5, CASTER, MAXEXP, "", "C3a C3a m6 C3a"},
        {"mage_identify", 4242, 0, true, 1, P, 5, CASTER | S_ITEMS | S_STAFF, MAXEXP, "", "C3ea C3eb C3ec C3ed C3e~"},
        {"rogue_spells", 987654321, 0, true, 3, P, 5, CASTER, MAXEXP, "77@11,25", "M999 C1a6~*3 M999 C1b~*3 M999 C1c~*3 M999 C2a6~*3 M999 C3e~*3"},

        // ---- every prayer ----
        {"priest_book1", 12345, 0, false, 2, P, 5, CASTER | S_HURT | S_AILING | S_TRAPS, MAXEXP, "77@11,25 34@9,23", "M999 P1a*3 M999 P1b*3 M999 P1c*3 M999 P1d*3 M999 P1e*3 M999 P1f*3 M999 P1g*3 M999 P1h*3"},
        {"priest_book2", 31337, 0, true, 2, P, 5, CASTER | S_HURT | S_CURSED | S_AILING, MAXEXP, "77@11,25 16@11,21 22@10,21", "M999 P2a6*3 M999 P2c*3 M999 P2d*3 M999 P2e*3 M999 P2f*3 M999 P2g*3 M999 P2h*3 M999 P2b*3"},
        {"priest_book3", 777, 0, true, 2, P, 5, CASTER | S_HURT | S_AILING, MAXEXP, "77@11,25 63@11,28 87@9,25 16@13,24", "M999 P3a*3 M999 P3b6*3 M999 P3c*3 M999 P3d*3 M999 P3e*3 M999 P3e*3 M999 P3i*3 M999 P3h*3 M999 P3g*3 M999 P3f*3"},
        {"priest_book4", 4242, 0, true, 2, P, 5, CASTER | S_HURT | S_AILING, MAXEXP, "77@11,25 63@11,28 87@9,25 16@13,24 278@15,30z30000", "M999 P4a*3 M999 P4b*3 M999 P4c*3 M999 P4d*3 M999 P4e*3 M999 P4f*3 M999 P4f*3"},
        {"paladin_prayers", 987654321, 0, true, 5, P, 5, CASTER | S_HURT, MAXEXP, "77@11,25", "M999 P1b*3 M999 P1c*3 M999 P2a6*3"},
        {"priest_glyph_food", 12345, 0, true, 2, P, 5, CASTER, MAXEXP, "", "P4e P2f m6 P2f P4e"},

        // ---- monster-status spells, directly ----
        {"status_rays", 31337, 0, true, 0, P, 5, T, 0, "77@11,24 16@11,30 22@11,36 63@11,42", "S39:6 S40:6 S26:6:-1 S26:6:2 S39:6 S40:6 S37:6 S38:6 S12:6 w*3"},
        {"status_nosleep", 777, 0, true, 0, P, 5, T, 0, "8@11,23 27@11,26c4 64@9,20", "S40:6 S39:6 S39:6 S40:8 S39:8"},
        {"status_resisters", 4242, 0, true, 0, P, 30, T, 0, "239@11,24 205@9,20", "S39:6 S40:6 S26:6:-1 S38:6 S38:8 S39:8 S40:8"},
        {"status_miss", 987654321, 0, true, 0, P, 5, T, 0, "77@5,24", "S39:6 S40:8 S26:2:1 S37:4 S38:6 S12:6"},
        {"status_all", 12345, 0, true, 0, P, 5, T, 0, "77@11,24 16@11,30z200 63@9,24 87@13,24 8@12,21 22@5,40", "U S35 S14:-1 S14:1 S36 S35 S14:-1"},
        {"status_all_dark", 31337, 0, true, 0, DARK, 5, T | S_DARK, 0, "77@11,124 16@11,130 63@9,124", "S35 S14:-1 S14:1 S36"},
        {"mass_genocide", 777, 0, true, 0, P, 5, T, 0, "77@11,24 16@11,30 278@15,30 22@5,58", "S13 w"},
        {"mass_polymorph", 4242, 0, true, 0, P, 5, T, 0, "77@11,24 16@11,30 278@15,30 22@5,58", "S15 w S15"},
        {"clone", 987654321, 0, true, 0, P, 5, T, 0, "77@11,24z100 17@9,20", "S12:6 S12:6 S12:8 S12:8 w*2"},

        // ---- terrain, light and object spells, directly ----
        {"light_line_corridor", 12345, 0, true, 0, DOORS, 5, T | S_DARK, 0, "17@11,90 41@11,95 18@11,83", "S7:6 S7:4 S7:8 S7:6"},
        {"starlite_dark_room", 31337, 0, true, 0, DARK, 5, T | S_DARK, 0, "17@11,124 36@7,116 18@15,124 47@11,116 77@8,123", "S8 S8"},
        {"starlite_lit_room", 777, 0, true, 0, P, 5, T, 0, "17@11,24 36@7,16 18@15,24", "S8"},
        {"disarm_ray_doors", 4242, 0, true, 0, DOORS, 5, T, 0, "", "S9:4 S9:4 m4 m4 S9:4"},
        {"destroy_doors_ray", 987654321, 0, true, 0, DOORS, 5, T, 0, "", "S10:4 S10:6"},
        {"disarm_ray_traps", 12345, 0, true, 0, P, 5, T | S_TRAPS, 0, "", "S9:6 S9:7 S9:3 S9:9 S10:8 S10:1"},
        {"build_wall", 31337, 0, true, 0, P, 5, T | S_NEAR_ITEMS, 0, "77@11,24 163@5,20 250@17,20 18@11,14", "S11:6 S11:8 S11:2 S11:4 S11:9"},
        {"wall_to_mud_stone", 777, 0, true, 0, Coord_t{11, 58}, 5, T | S_RUBBLE, 0, "149@11,55 167@9,58 166@13,58", "S33:4 S33:8 S33:2 S33:6 S33:4"},
        {"surround", 4242, 0, true, 0, P, 5, T | S_NEAR_ITEMS, 0, "77@11,24", "S5 S43 S6 S43 S6"},
        {"surround_traps_step", 987654321, 0, true, 0, P, 5, T, 0, "", "S5 m6"},
        {"destroy_area", 12345, 0, true, 0, P, 5, T | S_TRAPS, 0, "77@11,24 16@11,40 278@15,30 22@5,50", "S31 w"},
        {"destroy_area_town", 31337, 0, true, 0, P, 0, T, 0, "77@11,24", "S31"},
        {"darken_room", 777, 0, true, 0, P, 5, T, 0, "", "S4 S32 S4"},
        {"darken_corridor", 4242, 0, true, 0, DOORS, 5, T, 0, "", "S4 S32 S4 S7:6 S4"},
        {"light_area_town", 987654321, 0, true, 0, P, 0, T, 0, "", "S32 x5 S32 S4"},
        {"map_area", 12345, 0, true, 0, Coord_t{11, 90}, 5, T | S_TRAPS, 0, "", "S34 S34"},
        {"detect_all", 31337, 0, true, 0, P, 5, T | S_TRAPS, 0, "34@9,23 77@11,30 60@13,24 16@5,10 16@11,150 34@9,150", "S1 S2 S44 S45 S3 S29 S30 S1 S45"},
        {"detect_none", 777, 0, true, 0, DARK, 5, T | S_DARK, 0, "", "S1 S2 S44 S45 S3 S29 S30"},
        {"create_food_glyph", 4242, 0, true, 0, P, 5, T, 0, "", "S41 S41 m6 S42 S42 m4 S42"},

        // ---- player self-effects, directly ----
        {"self_effects", 987654321, 0, true, 0, P, 5, T | S_HURT | S_AILING | S_CURSED | S_WEAPON | S_DRAINED, 40000,
         "", "S25:4 S25:15 S25:40 S25:1000 S25:5 S16 S17 S18 S19 S20 S21 S22:10*6 S22:0 S23 S23 S24 S24 S27 S28 f5 S27 x5 S28"},
        {"self_sustained", 12345, 0, true, 0, P, 5, T | S_SUSTAIN, 0, "", "S16 S17 S18 S19 S20 S21"},
        {"enchant_high", 31337, 0, true, 0, P, 5, T | S_WEAPON, 0, "", "S22:3*12 S22:15*200"},

        // ---- added after a coverage pass ----
        {"identify_merge", 12345, 0, true, 1, P, 5, CASTER | S_UNKNOWN, MAXEXP, "",
         "M999 C3ea~*2 M999 C3eb~*2 M999 C3ec~*2 M999 C3ed~*2 M999 C3ee~*2 M999 C3ef~*2 M999 C3eg~*2 M999 C3eh~*2 M999 C3ei~*2 M999 C3ej~*2 M999 C3ek~*2 M999 C3el~*2 M999 C3em~*2"},
        {"pray_refusals", 31337, 0, true, 2, P, 5, CASTER, MAXEXP, "", "x5 P0 x0 f5 P0 f0 B P0 K0*40 P0"},
        {"mage_no_books", 777, 0, true, 1, P, 5, CASTER, MAXEXP, "", "B C0"},
        {"unknown_prayer_book", 4242, 0, false, 2, P, 3, T | S_BOOKS, 0, "", "P2"},
        {"mage_study_missing", 987654321, 0, true, 1, P, 3, T, 40000, "", "Gaaaaaaaaaaa~"},
        {"mage_study_dark", 12345, 0, true, 1, DARK, 3, T | S_DARK, 0, "", "G"},
        {"turn_undead_high", 31337, 0, true, 0, P, 30, T, MAXEXP, "63@11,24 87@9,22 224@13,24 34@11,17 205@15,20", "U S36 S36 S36"},
        {"holy_word", 777, 0, true, 2, P, 5, CASTER | S_HURT | S_AILING, MAXEXP, "", "M999 P4f*12"},
        {"genocide", 4242, 0, true, 1, P, 5, CASTER, MAXEXP, "16@11,24 16@11,30 16@5,40 278@15,30z30000", "M999 C4gk*8 M999 C4gB*4 M999 C4g~*6"},
        {"confuse_resist", 987654321, 0, true, 0, P, 5, T, 0, "166@11,24 153@9,20", "S39:6*6 S40:6*4 S39:8*4"},
        {"recharge_paths", 12345, 0, true, 1, P, 5, CASTER | S_STAFF, MAXEXP, "", "M999 C3b~*4 Q60 M999 C3b& I M999 C3b&*3 Q55 M999 C4b& M999 C4b&"},
        {"slow_poison", 31337, 0, true, 0, P, 5, T | S_AILING, 0, "", "S46*7"},
        {"slow_poison_none", 777, 0, true, 0, P, 5, T, 0, "", "S46"},
        {"wall_to_mud_walls", 4242, 0, true, 0, P, 5, T, 0, "", "S33:7 S33:1 S33:9 S33:3 S33:8 S33:4"},
        {"wall_to_mud_rubble_row", 987654321, 0, true, 0, P, 5, T | S_RUBBLE_ROW, 0, "", "S33:6*16"},
        {"surround_doors_items", 12345, 0, true, 0, P, 5, T | S_NEAR_ITEMS, 0, "", "S6"},
        {"destroy_adjacent_chest", 31337, 0, true, 0, P, 5, T | S_TRAPS, 0, "", "S43 S43"},
        {"destroy_ray_chest", 777, 0, true, 0, P, 5, T | S_TRAPS, 0, "", "S10:7 S10:9"},
    };

    for (auto const &s : scenarios) {
        runScenario(s);
    }

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
