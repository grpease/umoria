// Data-table dump harness: a headless CLI that prints every field of every
// row of the six data_*.cpp static tables (creatures, treasure, player,
// recall, store owners, stores), for byte-for-byte comparison against the
// .NET port's mechanically-extracted C# tables (see
// dotnet/tools/CppModelExtractor/extract_data_tables.py).
//
// This is the independent half of that verification: the Python extractor
// parses these same six files with libclang to produce the C# tables, so
// comparing the C# tables against *that same parse* would just check the
// extractor agrees with itself. This program instead prints what the real
// MSVC-compiled, linked, and executed C++ game actually holds in memory --
// a genuinely separate ground truth.
//
// Output format: one line per scalar leaf, "table.row.field<TAB>value".
// Nested Dice_t fields flatten to "field.dice"/"field.sides"; fixed-size
// array fields flatten to "field[0]".."field[n]". Strings print verbatim
// (or "<null>" for a null pointer); all other values print as plain
// decimal, regardless of how the original source wrote the literal (hex,
// char, symbolic constant) -- only the resulting value is being verified.
//
// Usage: data_table_dump [output_file]
// With no output_file, writes to stdout.

#include "../../src/headers.h"

#include <cstdio>

namespace {

FILE *out = stdout;

void value(const char *v) {
    if (v == nullptr) {
        std::fprintf(out, "<null>");
    } else {
        std::fprintf(out, "%s", v);
    }
}

void value(uint32_t v) { std::fprintf(out, "%u", v); }
void value(uint16_t v) { std::fprintf(out, "%u", v); }
void value(uint8_t v) { std::fprintf(out, "%u", v); }
void value(int32_t v) { std::fprintf(out, "%d", v); }
void value(int16_t v) { std::fprintf(out, "%d", v); }

template <typename T>
void field(const char *table, int row, const char *name, T v) {
    std::fprintf(out, "%s.%d.%s\t", table, row, name);
    value(v);
    std::fprintf(out, "\n");
}

void dice_field(const char *table, int row, const char *name, Dice_t const &d) {
    std::fprintf(out, "%s.%d.%s.dice\t%u\n", table, row, name, d.dice);
    std::fprintf(out, "%s.%d.%s.sides\t%u\n", table, row, name, d.sides);
}

template <typename T, int N>
void array_field(const char *table, int row, const char *name, T (&arr)[N]) {
    for (int i = 0; i < N; i++) {
        std::fprintf(out, "%s.%d.%s[%d]\t", table, row, name, i);
        value(arr[i]);
        std::fprintf(out, "\n");
    }
}

template <typename T, int N>
void dump_scalar_array(const char *table, T (&arr)[N]) {
    for (int i = 0; i < N; i++) {
        field(table, i, "value", arr[i]);
    }
}

template <typename T, int Rows, int Cols>
void dump_scalar_2d_array(const char *table, T (&arr)[Rows][Cols]) {
    for (int r = 0; r < Rows; r++) {
        for (int c = 0; c < Cols; c++) {
            std::fprintf(out, "%s.%d.value[%d]\t", table, r, c);
            value(arr[r][c]);
            std::fprintf(out, "\n");
        }
    }
}

void dump_creatures_list() {
    const char *t = "CreaturesList";
    for (int i = 0; i < MON_MAX_CREATURES; i++) {
        Creature_t const &c = creatures_list[i];
        field(t, i, "name", c.name);
        field(t, i, "movement", c.movement);
        field(t, i, "spells", c.spells);
        field(t, i, "defenses", c.defenses);
        field(t, i, "kill_exp_value", c.kill_exp_value);
        field(t, i, "sleep_counter", c.sleep_counter);
        field(t, i, "area_affect_radius", c.area_affect_radius);
        field(t, i, "ac", c.ac);
        field(t, i, "speed", c.speed);
        field(t, i, "sprite", c.sprite);
        dice_field(t, i, "hit_die", c.hit_die);
        array_field(t, i, "damage", c.damage);
        field(t, i, "level", c.level);
    }
}

void dump_monster_attacks() {
    const char *t = "MonsterAttacks";
    for (int i = 0; i < MON_ATTACK_TYPES; i++) {
        MonsterAttack_t const &a = monster_attacks[i];
        field(t, i, "type_id", a.type_id);
        field(t, i, "description_id", a.description_id);
        dice_field(t, i, "dice", a.dice);
    }
}

void dump_game_objects() {
    const char *t = "GameObjects";
    for (int i = 0; i < MAX_OBJECTS_IN_GAME; i++) {
        DungeonObject_t const &o = game_objects[i];
        field(t, i, "name", o.name);
        field(t, i, "flags", o.flags);
        field(t, i, "category_id", o.category_id);
        field(t, i, "sprite", o.sprite);
        field(t, i, "misc_use", o.misc_use);
        field(t, i, "cost", o.cost);
        field(t, i, "sub_category_id", o.sub_category_id);
        field(t, i, "items_count", o.items_count);
        field(t, i, "weight", o.weight);
        field(t, i, "to_hit", o.to_hit);
        field(t, i, "to_damage", o.to_damage);
        field(t, i, "ac", o.ac);
        field(t, i, "to_ac", o.to_ac);
        dice_field(t, i, "damage", o.damage);
        field(t, i, "depth_first_found", o.depth_first_found);
    }
}

void dump_character_races() {
    const char *t = "CharacterRaces";
    for (int i = 0; i < PLAYER_MAX_RACES; i++) {
        Race_t const &r = character_races[i];
        field(t, i, "name", r.name);
        field(t, i, "str_adjustment", r.str_adjustment);
        field(t, i, "int_adjustment", r.int_adjustment);
        field(t, i, "wis_adjustment", r.wis_adjustment);
        field(t, i, "dex_adjustment", r.dex_adjustment);
        field(t, i, "con_adjustment", r.con_adjustment);
        field(t, i, "chr_adjustment", r.chr_adjustment);
        field(t, i, "base_age", r.base_age);
        field(t, i, "max_age", r.max_age);
        field(t, i, "male_height_base", r.male_height_base);
        field(t, i, "male_height_mod", r.male_height_mod);
        field(t, i, "male_weight_base", r.male_weight_base);
        field(t, i, "male_weight_mod", r.male_weight_mod);
        field(t, i, "female_height_base", r.female_height_base);
        field(t, i, "female_height_mod", r.female_height_mod);
        field(t, i, "female_weight_base", r.female_weight_base);
        field(t, i, "female_weight_mod", r.female_weight_mod);
        field(t, i, "disarm_chance_base", r.disarm_chance_base);
        field(t, i, "search_chance_base", r.search_chance_base);
        field(t, i, "stealth", r.stealth);
        field(t, i, "fos", r.fos);
        field(t, i, "base_to_hit", r.base_to_hit);
        field(t, i, "base_to_hit_bows", r.base_to_hit_bows);
        field(t, i, "saving_throw_base", r.saving_throw_base);
        field(t, i, "hit_points_base", r.hit_points_base);
        field(t, i, "infra_vision", r.infra_vision);
        field(t, i, "exp_factor_base", r.exp_factor_base);
        field(t, i, "classes_bit_field", r.classes_bit_field);
    }
}

void dump_character_backgrounds() {
    const char *t = "CharacterBackgrounds";
    for (int i = 0; i < PLAYER_MAX_BACKGROUNDS; i++) {
        Background_t const &b = character_backgrounds[i];
        field(t, i, "info", b.info);
        field(t, i, "roll", b.roll);
        field(t, i, "chart", b.chart);
        field(t, i, "next", b.next);
        field(t, i, "bonus", b.bonus);
    }
}

void dump_classes() {
    const char *t = "Classes";
    for (int i = 0; i < PLAYER_MAX_CLASSES; i++) {
        Class_t const &c = classes[i];
        field(t, i, "title", c.title);
        field(t, i, "hit_points", c.hit_points);
        field(t, i, "disarm_traps", c.disarm_traps);
        field(t, i, "searching", c.searching);
        field(t, i, "stealth", c.stealth);
        field(t, i, "fos", c.fos);
        field(t, i, "base_to_hit", c.base_to_hit);
        field(t, i, "base_to_hit_with_bows", c.base_to_hit_with_bows);
        field(t, i, "saving_throw", c.saving_throw);
        field(t, i, "strength", c.strength);
        field(t, i, "intelligence", c.intelligence);
        field(t, i, "wisdom", c.wisdom);
        field(t, i, "dexterity", c.dexterity);
        field(t, i, "constitution", c.constitution);
        field(t, i, "charisma", c.charisma);
        field(t, i, "class_to_use_mage_spells", c.class_to_use_mage_spells);
        field(t, i, "experience_factor", c.experience_factor);
        field(t, i, "min_level_for_spell_casting", c.min_level_for_spell_casting);
    }
}

void dump_magic_spells() {
    const char *t = "MagicSpells";
    for (int cls = 0; cls < PLAYER_MAX_CLASSES - 1; cls++) {
        for (int lvl = 0; lvl < 31; lvl++) {
            Spell_t const &s = magic_spells[cls][lvl];
            char name[64];
            std::snprintf(name, sizeof(name), "%d.%d", cls, lvl);
            field(t, 0, (std::string(name) + ".level_required").c_str(), s.level_required);
            field(t, 0, (std::string(name) + ".mana_required").c_str(), s.mana_required);
            field(t, 0, (std::string(name) + ".failure_chance").c_str(), s.failure_chance);
            field(t, 0, (std::string(name) + ".exp_gain_for_learning").c_str(), s.exp_gain_for_learning);
        }
    }
}

void dump_store_owners() {
    const char *t = "StoreOwners";
    for (int i = 0; i < MAX_OWNERS; i++) {
        Owner_t const &o = store_owners[i];
        field(t, i, "name", o.name);
        field(t, i, "max_cost", o.max_cost);
        field(t, i, "max_inflate", o.max_inflate);
        field(t, i, "min_inflate", o.min_inflate);
        field(t, i, "haggles_per", o.haggles_per);
        field(t, i, "race", o.race);
        field(t, i, "max_insults", o.max_insults);
    }
}

void dump_class_rank_titles() {
    const char *t = "ClassRankTitles";
    for (int cls = 0; cls < PLAYER_MAX_CLASSES; cls++) {
        for (int lvl = 0; lvl < PLAYER_MAX_LEVEL; lvl++) {
            std::fprintf(out, "%s.%d.value[%d]\t", t, cls, lvl);
            value(class_rank_titles[cls][lvl]);
            std::fprintf(out, "\n");
        }
    }
}

template <typename T, int N>
void dump_string_array(const char *table, T (&arr)[N]) {
    for (int i = 0; i < N; i++) {
        field(table, i, "value", arr[i]);
    }
}

} // namespace

int main(int argc, char *argv[]) {
    if (argc >= 2) {
        out = std::fopen(argv[1], "w");
        if (out == nullptr) {
            std::fprintf(stderr, "could not open output file: %s\n", argv[1]);
            return 1;
        }
    }

    dump_creatures_list();
    dump_monster_attacks();
    dump_game_objects();
    dump_string_array("SpecialItemNames", special_item_names);
    dump_class_rank_titles();
    dump_character_races();
    dump_character_backgrounds();
    dump_classes();
    dump_scalar_2d_array("ClassLevelAdj", class_level_adj);
    dump_magic_spells();
    dump_string_array("SpellNames", spell_names);
    dump_scalar_2d_array("ClassBaseProvisions", class_base_provisions);
    dump_string_array("RecallDescriptionAttackType", recall_description_attack_type);
    dump_string_array("RecallDescriptionAttackMethod", recall_description_attack_method);
    dump_string_array("RecallDescriptionHowMuch", recall_description_how_much);
    dump_string_array("RecallDescriptionMove", recall_description_move);
    dump_string_array("RecallDescriptionSpell", recall_description_spell);
    dump_string_array("RecallDescriptionBreath", recall_description_breath);
    dump_string_array("RecallDescriptionWeakness", recall_description_weakness);
    dump_store_owners();
    dump_string_array("SpeechSaleAccepted", speech_sale_accepted);
    dump_string_array("SpeechSellingHaggleFinal", speech_selling_haggle_final);
    dump_string_array("SpeechSellingHaggle", speech_selling_haggle);
    dump_string_array("SpeechBuyingHaggleFinal", speech_buying_haggle_final);
    dump_string_array("SpeechBuyingHaggle", speech_buying_haggle);
    dump_string_array("SpeechInsultedHagglingDone", speech_insulted_haggling_done);
    dump_string_array("SpeechGetOutOfMyStore", speech_get_out_of_my_store);
    dump_string_array("SpeechHagglingTryAgain", speech_haggling_try_again);
    dump_string_array("SpeechSorry", speech_sorry);
    dump_scalar_2d_array("RaceGoldAdjustments", race_gold_adjustments);
    dump_scalar_2d_array("StoreChoices", store_choices);
    // data_tables.cpp (added M11: the flavour names magicInitializeItemNames()
    // shuffles, unshuffled here since this harness never calls it)
    dump_string_array("Colors", colors);
    dump_string_array("Mushrooms", mushrooms);
    dump_string_array("Woods", woods);
    dump_string_array("Metals", metals);
    dump_string_array("Rocks", rocks);
    dump_string_array("Amulets", amulets);
    dump_string_array("Syllables", syllables);
    dump_scalar_2d_array("BlowsTable", blows_table);
    dump_scalar_array("NormalTable", normal_table);

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
