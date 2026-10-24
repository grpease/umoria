// UI screen scenarios (M14), shared by ui_screen_dump.cpp (#included into its
// scenario array) and UiScreenTraceTests.cs (parsed as text), so the two
// sides can't drift. Fields: name, seed, race, male, class, position, depth,
// setup bits, experience, monsters, script -- see ui_screen_dump.cpp. Only P
// / DARK / GEN, T and the S_* bits, `|`, numbers, true/false and strings are
// allowed here; the C# parser knows no more than that.
//
// Human = race 0; class_choice is the class id (0 Warrior, 1 Mage, 2 Priest,
// 3 Rogue, 4 Ranger, 5 Paladin). GEN levels are depth 1 (no vaults, which
// the port's generator leaves out).

// ---- the main screen ----
{"screen_arena_lit", 12345, 0, true, 0, P, 5, T, 0, "", "!"},
{"screen_arena_monsters", 31337, 0, true, 0, P, 5, T, 0, "16@11,25 77@9,30z30000", "! m6 m6"},
{"status_fields", 12345, 0, true, 0, P, 5, T, 0, "", "Xh D Xc5 Xf5 Xp5 D Xv1 D Xv-1 D Xv-2 D Xn2 D Xs D Xr5 D Xr-1 D Xz5 D Xw D Xk7 Xz0 D"},
{"status_blind", 777, 0, true, 0, P, 5, T, 0, "", "Xb20 D m6"},
{"walk_lit_room", 4242, 0, true, 0, P, 5, T, 0, "", "m6 m6 m3 m2 m2 m1 m4 m7 m8 m9"},
{"walk_dark_room", 987654321, 0, true, 0, DARK, 5, T, 0, "", "m6 m6 m6 m2 m2 m4 m4 m8"},
{"walk_no_light", 12345, 0, true, 0, DARK, 5, T | S_DARK, 0, "", "m6 m6 m2"},
{"walk_panel_shift", 31337, 0, true, 0, P, 5, T, 0, "", "m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6 m6"},
{"generated_level_a", 12345, 0, true, 0, GEN, 1, T, 0, "", "! m2 m6 m8 m4"},
{"generated_level_b", 7, 0, true, 0, GEN, 1, T, 0, "", "! m4 m4 m6 m6"},
{"generated_level_c", 999999, 0, false, 1, GEN, 1, T, 0, "", "!"},

// ---- message line ----
{"messages_combine_more", 12345, 0, true, 0, P, 5, T, 0, "", "MHello. MWorld. MThe_quick_brown_fox_jumps_over_the_lazy_dog_and_then_some. N:_ MOne MTwo:x_ N:$ MLast N:~"},
{"messages_long", 31337, 0, true, 0, P, 5, T, 0, "", "MThis_message_is_far_too_long_to_fit_on_the_message_line_of_an_eighty_column_screen_at_all MNext:_ N:_"},
{"message_history", 777, 0, true, 0, P, 5, T, 0, "", "MFirst MSecond:_ MThird:_ MFourth:_ N:_ H0 H0:_ H3:_ H30:_"},
{"message_history_wrap", 4242, 0, true, 0, P, 5, T, 0, "", "M1 M2:_ M3:_ M4:_ M5:_ M6:_ M7:_ M8:_ M9:_ M10:_ M11:_ M12:_ M13:_ M14:_ M15:_ M16:_ M17:_ M18:_ M19:_ M20:_ M21:_ M22:_ M23:_ M24:_ N:_ H0 H0:_"},

// ---- prompts ----
{"text_input", 12345, 0, true, 0, P, 5, T, 0, "", "T:Hello$ T:ab#c$ T:~ T:^Rxy$ T:abcdefghijklmnopqrstuvwxyz$ T:##a$ T:_spaced___$"},
{"confirm", 31337, 0, true, 0, P, 5, T, 0, "", "Y:y Y:n Y:_N Y:q Y:~"},

// ---- inventory ----
{"inventory_lists", 12345, 0, true, 0, P, 5, T | S_ITEMS | S_WEAPON, 0, "", "ii:~ ie:~ i?:~ ii:e?i~ ix:~"},
{"inventory_wear", 31337, 0, true, 0, P, 5, T | S_ITEMS, 0, "", "iw:*~ iw:~ it:*~ id:~"},
{"inventory_empty", 777, 0, true, 0, P, 5, T, 0, "", "ie:~ ii:~"},
{"item_prompt", 4242, 0, true, 0, P, 5, T | S_ITEMS | S_WEAPON, 0, "", "G0,8:*~ G0,8:a G0,8:zb G0,40:/*~ G0,40:*/~ G0,40:B"},

// ---- character screen ----
{"character_sheet", 12345, 0, true, 0, P, 5, T, 0, "", "C:~ D C:x_ D"},
{"character_rename", 31337, 0, false, 2, P, 5, T, 3000, "", "C:cBobbin$ D"},
{"level_up", 777, 0, true, 1, P, 5, T, 0, "", "E100 E1500:____ E40000:__________________"},

// ---- spells ----
{"cast_list", 12345, 0, true, 1, P, 5, T | S_BOOKS | S_LEARN | S_MANA, 3000, "", "c:a*~ c:a~"},
{"pray_list", 31337, 0, true, 2, P, 5, T | S_BOOKS | S_LEARN | S_MANA, 3000, "", "p:a*~"},
{"study_list", 4242, 0, true, 1, P, 5, T | S_BOOKS, 3000, "", "s:a~ s:~"},

// ---- map screens ----
{"display_map", 12345, 0, true, 0, GEN, 1, T, 0, "", "V:_"},
{"locate_on_map", 7, 0, true, 0, GEN, 1, T, 0, "", "L:6~ L:2486~"},

// ---- stores ----
{"store_screens", 12345, 0, true, 0, P, 5, T, 0, "", "O Xg5000 S0:~ S1:b~ S5:~"},
{"store_inventory_keys", 31337, 0, true, 0, P, 5, T | S_ITEMS, 0, "", "O S0:i~~"},

// ---- state changes under an open screen ----
{"inventory_wear_continue", 4242, 0, true, 0, P, 5, T | S_ITEMS, 0, "", "iw:e I:y~ ie:~ it:a I:n ii:~"},
{"inventory_screen_commands", 987654321, 0, true, 0, P, 5, T | S_ITEMS | S_WEAPON, 0, "", "ii:w*f~ ie:t*a~ ii:d*~ ie:x~"},
{"inventory_drop_stack", 12345, 0, true, 0, P, 5, T | S_ITEMS, 0, "", "id:a~ id:ay~ I:~"},
{"status_ticks", 31337, 0, true, 0, P, 5, T, 0, "", "Xp3 Xc2 Xf2 U1 U1:_ U1:___ U1:___ U1:___"},
{"status_blind_ticks", 777, 0, true, 0, P, 5, T, 0, "", "Xb2 U1 U1 U1:_"},
{"status_hunger", 4242, 0, true, 0, P, 5, T, 0, "", "Xd2001 U2:_ Xd1001 U2:__"},
{"status_rest", 12345, 0, true, 0, P, 5, T | S_HURT, 0, "", "Xr3 D U1 U1 U1:_ U1"},
{"cast_detect_monsters", 987654321, 0, true, 1, DARK, 5, T | S_BOOKS | S_LEARN | S_MANA, 3000, "107@11,140z30000 16@5,130z30000", "c:ab__ c:ab__"},
{"cast_light_area", 12345, 0, true, 1, DARK, 5, T | S_BOOKS | S_LEARN | S_MANA, 3000, "", "c:ad__"},
{"store_buy", 31337, 0, true, 0, P, 5, T, 0, "", "O Xg5000 S1:pa1000$_~"},
{"store_sell", 4242, 0, true, 0, P, 5, T | S_ITEMS, 0, "", "O S0:sa1$__~"},
