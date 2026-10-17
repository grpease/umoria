// Shared pieces of the character-creation harness (M6), split out so the
// player status-effects harness (M7, tools/status_effects_dump) can start
// every scenario from a real, fully created character and dump it in the
// same format. See char_create_dump.cpp for the why behind each step.

#include "../../src/headers.h"

#include <cstdio>
#include <cstring>
#include <string>

#include "char_create_common.h"
#include "ui_io_stub.h"

static FILE *out = stdout;

void playerDumpSetOutput(FILE *file) {
    out = file;
}

static void field(const char *name, long long value) {
    std::fprintf(out, "%s\t%lld\n", name, value);
}

static void fieldIndexed(const char *name, int index, long long value) {
    std::fprintf(out, "%s.%d\t%lld\n", name, index, value);
}

static void fieldString(const char *name, const char *value) {
    std::fprintf(out, "%s\t[%s]\n", name, value);
}

void dumpPlayer() {
    field("rnd_seed", (long long) getRandomSeed());

    fieldString("misc.name", py.misc.name);
    field("misc.gender", py.misc.gender);
    field("misc.au", py.misc.au);
    field("misc.max_exp", py.misc.max_exp);
    field("misc.exp", py.misc.exp);
    field("misc.exp_fraction", py.misc.exp_fraction);
    field("misc.age", py.misc.age);
    field("misc.height", py.misc.height);
    field("misc.weight", py.misc.weight);
    field("misc.level", py.misc.level);
    field("misc.max_dungeon_depth", py.misc.max_dungeon_depth);
    field("misc.chance_in_search", py.misc.chance_in_search);
    field("misc.fos", py.misc.fos);
    field("misc.bth", py.misc.bth);
    field("misc.bth_with_bows", py.misc.bth_with_bows);
    field("misc.mana", py.misc.mana);
    field("misc.max_hp", py.misc.max_hp);
    field("misc.plusses_to_hit", py.misc.plusses_to_hit);
    field("misc.plusses_to_damage", py.misc.plusses_to_damage);
    field("misc.ac", py.misc.ac);
    field("misc.magical_ac", py.misc.magical_ac);
    field("misc.display_to_hit", py.misc.display_to_hit);
    field("misc.display_to_damage", py.misc.display_to_damage);
    field("misc.display_ac", py.misc.display_ac);
    field("misc.display_to_ac", py.misc.display_to_ac);
    field("misc.disarm", py.misc.disarm);
    field("misc.saving_throw", py.misc.saving_throw);
    field("misc.social_class", py.misc.social_class);
    field("misc.stealth_factor", py.misc.stealth_factor);
    field("misc.class_id", py.misc.class_id);
    field("misc.race_id", py.misc.race_id);
    field("misc.hit_die", py.misc.hit_die);
    field("misc.experience_factor", py.misc.experience_factor);
    field("misc.current_mana", py.misc.current_mana);
    field("misc.current_mana_fraction", py.misc.current_mana_fraction);
    field("misc.current_hp", py.misc.current_hp);
    field("misc.current_hp_fraction", py.misc.current_hp_fraction);
    for (int i = 0; i < 4; i++) {
        std::fprintf(out, "misc.history.%d\t[%s]\n", i, py.misc.history[i]);
    }

    for (int i = 0; i < 6; i++) {
        fieldIndexed("stats.max", i, py.stats.max[i]);
        fieldIndexed("stats.current", i, py.stats.current[i]);
        fieldIndexed("stats.modified", i, py.stats.modified[i]);
        fieldIndexed("stats.used", i, py.stats.used[i]);
    }

    field("flags.status", py.flags.status);
    field("flags.rest", py.flags.rest);
    field("flags.blind", py.flags.blind);
    field("flags.paralysis", py.flags.paralysis);
    field("flags.confused", py.flags.confused);
    field("flags.food", py.flags.food);
    field("flags.food_digested", py.flags.food_digested);
    field("flags.protection", py.flags.protection);
    field("flags.speed", py.flags.speed);
    field("flags.fast", py.flags.fast);
    field("flags.slow", py.flags.slow);
    field("flags.afraid", py.flags.afraid);
    field("flags.poisoned", py.flags.poisoned);
    field("flags.image", py.flags.image);
    field("flags.protect_evil", py.flags.protect_evil);
    field("flags.invulnerability", py.flags.invulnerability);
    field("flags.heroism", py.flags.heroism);
    field("flags.super_heroism", py.flags.super_heroism);
    field("flags.blessed", py.flags.blessed);
    field("flags.heat_resistance", py.flags.heat_resistance);
    field("flags.cold_resistance", py.flags.cold_resistance);
    field("flags.detect_invisible", py.flags.detect_invisible);
    field("flags.word_of_recall", py.flags.word_of_recall);
    field("flags.see_infra", py.flags.see_infra);
    field("flags.timed_infra", py.flags.timed_infra);
    field("flags.see_invisible", py.flags.see_invisible);
    field("flags.teleport", py.flags.teleport);
    field("flags.free_action", py.flags.free_action);
    field("flags.slow_digest", py.flags.slow_digest);
    field("flags.aggravate", py.flags.aggravate);
    field("flags.resistant_to_fire", py.flags.resistant_to_fire);
    field("flags.resistant_to_cold", py.flags.resistant_to_cold);
    field("flags.resistant_to_acid", py.flags.resistant_to_acid);
    field("flags.regenerate_hp", py.flags.regenerate_hp);
    field("flags.resistant_to_light", py.flags.resistant_to_light);
    field("flags.free_fall", py.flags.free_fall);
    field("flags.sustain_str", py.flags.sustain_str);
    field("flags.sustain_int", py.flags.sustain_int);
    field("flags.sustain_wis", py.flags.sustain_wis);
    field("flags.sustain_con", py.flags.sustain_con);
    field("flags.sustain_dex", py.flags.sustain_dex);
    field("flags.sustain_chr", py.flags.sustain_chr);
    field("flags.confuse_monster", py.flags.confuse_monster);
    field("flags.new_spells_to_learn", py.flags.new_spells_to_learn);
    field("flags.spells_learnt", py.flags.spells_learnt);
    field("flags.spells_worked", py.flags.spells_worked);
    field("flags.spells_forgotten", py.flags.spells_forgotten);
    for (int i = 0; i < 32; i++) {
        fieldIndexed("flags.spells_learned_order", i, py.flags.spells_learned_order[i]);
    }

    for (int i = 0; i < PLAYER_MAX_LEVEL; i++) {
        fieldIndexed("base_hp_levels", i, py.base_hp_levels[i]);
    }
    for (int i = 0; i < PLAYER_MAX_LEVEL; i++) {
        fieldIndexed("base_exp_levels", i, py.base_exp_levels[i]);
    }

    field("temporary_light_only", py.temporary_light_only);
    field("pack.unique_items", py.pack.unique_items);
    field("pack.weight", py.pack.weight);
    field("pack.heaviness", py.pack.heaviness);
    field("equipment_count", py.equipment_count);
    field("weapon_is_heavy", py.weapon_is_heavy);
    field("carrying_light", py.carrying_light);

    for (int i = 0; i < PLAYER_INVENTORY_SIZE; i++) {
        Inventory_t const &item = py.inventory[i];
        std::fprintf(out,
                     "inventory.%d\tid=%u sn=%u insc=[%s] flags=%u cat=%u sprite=%u misc=%d cost=%d sub=%u count=%u "
                     "weight=%u to_hit=%d to_dam=%d ac=%d to_ac=%d dice=%ux%u depth=%u ident=%u\n",
                     i, item.id, item.special_name_id, item.inscription, item.flags, item.category_id, item.sprite,
                     item.misc_use, item.cost, item.sub_category_id, item.items_count, item.weight, item.to_hit,
                     item.to_damage, item.ac, item.to_ac, item.damage.dice, item.damage.sides,
                     item.depth_first_found, item.identification);
    }

    for (int i = 0; i < OBJECT_IDENT_SIZE; i++) {
        if (objects_identified[i] != 0) {
            fieldIndexed("objects_identified", i, objects_identified[i]);
        }
    }
}

int raceClassCount(int race_id) {
    // Number of classes this race may choose, in the same order
    // displayRaceClasses() lists them (bit order of classes_bit_field).
    int class_count = 0;
    for (int i = 0; i < PLAYER_MAX_CLASSES; i++) {
        if ((character_races[race_id].classes_bit_field & (1u << i)) != 0u) {
            class_count++;
        }
    }
    return class_count;
}

bool createScriptedCharacter(uint32_t seed, int race_id, bool is_male, int class_choice, int rerolls) {
    // Fresh state, as startMoria() sees it for a new game.
    py = Player_t{};
    std::memset(objects_identified, 0, sizeof(objects_identified));
    playerInitializeBaseExperienceLevels();
    py.flags.spells_learnt = 0;
    py.flags.spells_worked = 0;
    py.flags.spells_forgotten = 0;

    std::string keys;
    keys += (char) ('a' + race_id);
    keys += is_male ? 'm' : 'f';
    keys.append((size_t) rerolls, ' ');
    keys += ESCAPE;
    keys += (char) ('a' + class_choice);
    keys += ' '; // "[ press any key to continue, or Q to exit ]"
    uiStubSetScript(keys, "Tester");

    setRandomSeed(seed);

    characterCreate();

    // Rest of startMoria()'s new-character branch (game_run.cpp),
    // minus date_of_birth (wall clock) and the screen clears.
    initializeCharacterInventory();
    py.flags.food = 7500;
    py.flags.food_digested = 2;

    if (classes[py.misc.class_id].class_to_use_mage_spells == config::spells::SPELL_TYPE_MAGE) {
        playerCalculateAllowedSpellsCount(PlayerAttr::A_INT);
        playerGainMana(PlayerAttr::A_INT);
    } else if (classes[py.misc.class_id].class_to_use_mage_spells == config::spells::SPELL_TYPE_PRIEST) {
        playerCalculateAllowedSpellsCount(PlayerAttr::A_WIS);
        playerGainMana(PlayerAttr::A_WIS);
    }

    py.temporary_light_only = false;
    py.weapon_is_heavy = false;
    py.pack.heaviness = 0;

    return uiStubScriptConsumed();
}
