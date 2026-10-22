// Item-use dump harness: verifies the .NET migration's port of the item
// commands (M11 -- Umoria.Core.Items.PlayerEat / PlayerQuaff / Scrolls /
// Staves / PlayerThrow / InventoryCommands / InventoryActions, and the full
// ItemDescription in context) against the real compiled C++ game.
//
// Same build pattern as spell_casting_dump (M10): the real sources minus
// main.cpp and ui_io.cpp (replaced by the M6 headless stub) -- zero changes
// to src/. The arena, setup bits, monster spec language and hashes are
// M10's, copied. New here:
//   - every message printMessage() is given is logged through the stub and
//     dumped as "msg ..." lines after each step, so the text built from
//     itemDescription() is compared too, not just its game-state effect;
//   - a third line per step with the item-related player and screen state
//     and a hash of objects_identified[];
//   - the flavour names are shuffled per scenario (magicInitializeItemNames()
//     with a seed derived from the scenario's), starting from the pristine
//     tables each time -- the shuffle works on the globals in place, and the
//     C# side starts every scenario with a fresh ObjectIdentification;
//   - after the final player dump, the description of every carried and
//     worn item.
//
// Script tokens (space separated, `*N` repeats a token N times):
//   turn-taking:  w  wait             m<d>  playerMove(d, true)
//                 e<keys> playerEat()      q<keys> quaff()
//                 r<keys> scrollRead()     u<keys> staffUse()
//                 a<keys> wandAim()        t<keys> playerThrowItem()
//                 I<c><keys> inventoryExecuteCommand(c)
//                    <keys>: literal keys; ~ = <ESCAPE>; %N. = the letter of
//                    the first pack slot holding catalog object N
//   free:         {<keys>:<text> itemInscribe(), <text> the inscription
//                 y / n   what getInputConfirmation() answers from now on
//                 A<n>    what getInputConfirmationWithAbort() answers (1/0/-1)
//                 x<n> py.flags.blind = n, with PY_BLIND set
//                 f<n> py.flags.confused = n
//                 F<n> py.flags.food = n
//                 H<n> py.misc.current_hp = n
//                 M<n> py.misc.current_mana = n
//                 U    updateMonsters(false)
//                 K<n> inventoryDestroyItem(n), if n is in the pack
//                 P<id>:<count>[:<misc>] carry a catalog object
//                 G<id>:<level> carry one made magical (magicTreasureMagicalAbility())
//                 X<id>:<slot> wear a catalog object in a slot
//                 Z<slot> curse the item in a slot
//                 D<id> spell-identify the first pack slot holding catalog object id
//                 O<dy>,<dx>:<id> place a catalog object near the player
//
// Usage: item_use_dump [output_file]

#include "../../src/headers.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "../char_create_dump/char_create_common.h"
#include "../char_create_dump/ui_io_stub.h"

// defined in identification.cpp but not declared in any header
extern char magic_item_titles[MAX_TITLES][10];

static FILE *out = stdout;

// ---- arena (M9's, copied from spell_casting_dump) ----------------------

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

static uint32_t hashIdentified() {
    uint32_t h = 2166136261u;
    for (auto flag : objects_identified) {
        h = mix(h, flag);
    }
    return h;
}

static std::vector<std::string> step_messages;

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

    std::fprintf(out,
                 "itm keys=%d/%d food=%d image=%d fast=%d hero=%d shero=%d invuln=%d recall=%d infra=%d cmon=%d hres=%d cres=%d dinv=%d bless=%d pevil=%d "
                 "heavy=%d eq=%d ac=%d/%d hit=%d dam=%d screen=%d wear=%d-%d doing=%d prev=%d ident=%08x died=%s\n",
                 (int) keys_used, (int) keys_given, py.flags.food, py.flags.image, py.flags.fast, py.flags.heroism, py.flags.super_heroism, py.flags.invulnerability,
                 py.flags.word_of_recall, py.flags.timed_infra, (int) py.flags.confuse_monster, py.flags.heat_resistance, py.flags.cold_resistance,
                 py.flags.detect_invisible, py.flags.blessed, py.flags.protect_evil, (int) py.weapon_is_heavy, py.equipment_count, py.misc.ac, py.misc.magical_ac,
                 py.misc.plusses_to_hit, py.misc.plusses_to_damage, (int) game.screen.current_screen_id, game.screen.wear_low_id, game.screen.wear_high_id,
                 (int) game.doing_inventory_command, (int) py.prev_dir, hashIdentified(), game.character_died_from);

    std::fprintf(out, "mons");
    for (int i = config::monsters::MON_MIN_INDEX_ID; i < next_free_monster_id; i++) {
        Monster_t const &m = monsters[i];
        std::fprintf(out, " %d:%d@%d,%d/%d/%d/%d/%d/%d/%d/%d", i, m.creature_id, m.pos.y, m.pos.x, m.hp, m.sleep_count, m.stunned_amount, m.confused_amount, m.speed,
                     m.distance_from_player, (int) m.lit);
    }
    std::fprintf(out, "\n");

    for (auto const &message : step_messages) {
        std::fprintf(out, "msg %s\n", message.c_str());
    }
    step_messages.clear();
}

static void dumpDescriptions() {
    for (int i = 0; i < PLAYER_INVENTORY_SIZE; i++) {
        if (py.inventory[i].category_id == TV_NOTHING) {
            continue;
        }
        obj_desc_t description = {'\0'};
        itemDescription(description, py.inventory[i], true);
        std::fprintf(out, "desc %d %s\n", i, description);
    }
}

// ---- script runner ------------------------------------------------------

static int step_number = 0;

// Turns a token's key text into the stub's key script (see the header).
static std::string resolveKeys(const std::string &token, size_t start) {
    std::string keys;
    for (size_t k = start; k < token.size(); k++) {
        char c = token[k];
        if (c == '~') {
            keys += (char) ESCAPE;
        } else if (c == '%') {
            size_t end = token.find('.', k);
            int id = std::atoi(token.substr(k + 1, end - k - 1).c_str());
            int slot = -1;
            for (int i = 0; i < py.pack.unique_items; i++) {
                if (py.inventory[i].id == id) {
                    slot = i;
                    break;
                }
            }
            if (slot < 0) {
                std::fprintf(stderr, "token %s: object %d not in the pack\n", token.c_str(), id);
                std::exit(8);
            }
            keys += (char) ('a' + slot);
            k = end;
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

static char inventory_command = 0;

static void runInventoryCommand() {
    inventoryExecuteCommand(inventory_command);
}

static void carryObject(int object_id, int count, int16_t misc_use) {
    carry(object_id, count, misc_use);
}

static void runToken(const std::string &token) {
    char op = token[0];
    int a = 0;
    int b = 0;
    int c = -1;

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
        case 'e':
            withKeys(resolveKeys(token, 1), playerEat);
            break;
        case 'q':
            withKeys(resolveKeys(token, 1), quaff);
            break;
        case 'r':
            withKeys(resolveKeys(token, 1), scrollRead);
            break;
        case 'u':
            withKeys(resolveKeys(token, 1), staffUse);
            break;
        case 'a':
            withKeys(resolveKeys(token, 1), wandAim);
            break;
        case 't':
            withKeys(resolveKeys(token, 1), playerThrowItem);
            break;
        case 'I':
            // (a fresh command: re-entering a left-open one is the main
            // loop's, M15)
            game.doing_inventory_command = 0;
            inventory_command = token[1];
            withKeys(resolveKeys(token, 2), runInventoryCommand);
            break;
        case '{': {
            size_t colon = token.find(':');
            std::string keys = resolveKeys(token.substr(0, colon), 1);
            uiStubSetScript(keys, token.substr(colon + 1));
            itemInscribe();
            keys_used = uiStubKeysConsumed();
            keys_given = keys.size();
            takes_turn = false;
            break;
        }
        case 'y':
            uiStubSetConfirmAnswer(true);
            takes_turn = false;
            break;
        case 'n':
            uiStubSetConfirmAnswer(false);
            takes_turn = false;
            break;
        case 'A':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            uiStubSetConfirmWithAbortAnswer(a);
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
        case 'F':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            py.flags.food = (int16_t) a;
            takes_turn = false;
            break;
        case 'H':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            py.misc.current_hp = (int16_t) a;
            takes_turn = false;
            break;
        case 'M':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            py.misc.current_mana = (int16_t) a;
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
        case 'P':
            (void) std::sscanf(token.c_str() + 1, "%d:%d:%d", &a, &b, &c);
            carryObject(a, b, (int16_t) c);
            takes_turn = false;
            break;
        case 'G': {
            (void) std::sscanf(token.c_str() + 1, "%d:%d", &a, &b);
            // treasure slot 0 is never a real object (and isn't hashed)
            inventoryItemCopyTo(a, game.treasure.list[0]);
            magicTreasureMagicalAbility(0, b);
            Inventory_t item = game.treasure.list[0];
            inventoryItemCopyTo(config::dungeon::objects::OBJ_NOTHING, game.treasure.list[0]);
            (void) inventoryCarryItem(item);
            takes_turn = false;
            break;
        }
        case 'X':
            (void) std::sscanf(token.c_str() + 1, "%d:%d", &a, &b);
            if (py.inventory[b].category_id == TV_NOTHING) {
                py.equipment_count++;
            }
            inventoryItemCopyTo(a, py.inventory[b]);
            playerRecalculateBonuses();
            takes_turn = false;
            break;
        case 'Z':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            py.inventory[a].flags |= config::treasure::flags::TR_CURSED;
            takes_turn = false;
            break;
        case 'D':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            for (int i = 0; i < py.pack.unique_items; i++) {
                if (py.inventory[i].id == a) {
                    spellItemIdentifyAndRemoveRandomInscription(py.inventory[i]);
                    break;
                }
            }
            takes_turn = false;
            break;
        case 'O':
            (void) std::sscanf(token.c_str() + 1, "%d,%d:%d", &a, &b, &c);
            (void) placeObject(Coord_t{py.pos.y + a, py.pos.x + b}, c);
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

        // `*N` repeats, but only with digits after it ('*' is also the
        // inventory menus' list key)
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

// ---- flavour names --------------------------------------------------------

template <int N>
struct SavedNames {
    const char *names[N];
};

static SavedNames<MAX_COLORS> saved_colors;
static SavedNames<MAX_MUSHROOMS> saved_mushrooms;
static SavedNames<MAX_WOODS> saved_woods;
static SavedNames<MAX_METALS> saved_metals;
static SavedNames<MAX_ROCKS> saved_rocks;
static SavedNames<MAX_AMULETS> saved_amulets;

template <int N>
static void save(SavedNames<N> &saved, const char *(&table)[N]) {
    std::memcpy(saved.names, table, sizeof(saved.names));
}

template <int N>
static void restore(SavedNames<N> const &saved, const char *(&table)[N]) {
    std::memcpy(table, saved.names, sizeof(saved.names));
}

static void shuffleNames(uint32_t magic_seed) {
    restore(saved_colors, colors);
    restore(saved_mushrooms, mushrooms);
    restore(saved_woods, woods);
    restore(saved_metals, metals);
    restore(saved_rocks, rocks);
    restore(saved_amulets, amulets);

    game.magic_seed = (int32_t) magic_seed;
    magicInitializeItemNames();
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
    uiStubSetConfirmWithAbortAnswer(0);

    if (!createScriptedCharacter(s.seed, s.race_id, s.is_male, s.class_choice, 0)) {
        std::fprintf(stderr, "scenario %s: character creation script not consumed\n", s.name);
        std::exit(2);
    }

    // startMoria(): magicInitializeItemNames() right after creation
    shuffleNames(s.seed ^ 0x5a5a5a5au);

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

    step_messages.clear();

    std::fprintf(out, "scenario %s\n", s.name);
    step_number = 0;
    keys_used = 0;
    keys_given = 0;
    dumpStep(0, "start");

    runScript(s.script);

    std::fprintf(out, "final %s\n", s.name);
    dumpPlayer();
    dumpDescriptions();
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
    uiStubSetMessageLog(&step_messages);

    save(saved_colors, colors);
    save(saved_mushrooms, mushrooms);
    save(saved_woods, woods);
    save(saved_metals, metals);
    save(saved_rocks, rocks);
    save(saved_amulets, amulets);

    initializeMonsterLevels();
    initializeTreasureLevels();

    const Coord_t P = Coord_t{11, 20};
    const Coord_t DARK = Coord_t{11, 120};

    const int T = S_TOUGH;

    // Human=0; class_choice is the class id (0 Warrior, 1 Mage, 2 Priest,
    // 3 Rogue, 4 Ranger, 5 Paladin).
    const Scenario scenarios[] = {
#include "item_use_scenarios.inc"
    };

    for (auto const &s : scenarios) {
        runScenario(s);
    }

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
