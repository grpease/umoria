// Store scenarios (M13), shared by store_dump.cpp (#included into its
// scenario array) and StoreTraceTests.cs (parsed as text), so the two sides
// can't drift. Fields: name, seed, race, male, class, gold, script -- see
// store_dump.cpp for the script tokens. Only numbers, true/false and strings
// are allowed here; the C# parser knows no more than that.
//
// Seed 12345's Human Warrior carries rations (a), soft leather armour (b), a
// cloak (c), a stiletto (d) and torches (e); its town has 10/11/19/9/10/15
// items in stores 0-5.

// ---- buying ----
{"look", 12345, 0, true, 0, 5000, "S0:~ S1:~ S2:~ S3:~ S4:~ S5:~"},
{"buy_exact_then_fixed", 12345, 0, true, 0, 5000, "S0:pbpb~/4 M M"},
{"buy_sorry", 12345, 0, true, 0, 5000, "S0:pb~/10/4"},
{"buy_haggle", 12345, 0, true, 0, 5000, "S2:pa~/300/+50/+50/480/+1/+1/+1/+1/~"},
{"buy_haggle_accept", 12345, 0, true, 0, 5000, "S2:pa~/300/+50/+50/439 M"},
{"buy_accept_asking", 12345, 0, true, 0, 5000, "S2:pa~/300/489"},
{"buy_final_offer_rejected", 12345, 0, true, 0, 5000, "B2:20:0 S2:pa~/100/150/200/250/~ S2:pa~/~"},
{"buy_insults_kicked", 12345, 0, true, 0, 5000, "S2:pa~/300/+1/+1/+1/+1/+1/+1/+1/+1/+1/+1/+1/+1 S2:~ T6000 S2:~"},
{"buy_below_last_offer", 12345, 0, true, 0, 5000, "S2:pa~/300/200/~"},
{"buy_first_offer_rules", 12345, 0, true, 0, 5000, "S2:pa~/+5/-5/0/abc/_300/~"},
{"buy_return_quirk", 12345, 0, true, 0, 5000, "S2:pa~/300///310/~"},
{"buy_liar", 12345, 0, true, 0, 10, "S2:pa~/605"},
{"buy_good_bargainer", 12345, 0, true, 0, 5000, "B2:20:0 S2:pa~/"},
{"buy_liar_kicked", 12345, 0, true, 0, 10, "N2:8 S2:pa~/605 S2:~"},
{"buy_last_on_second_page", 12345, 0, true, 0, 5000, "A1:94 A1:96 B1:20:0 S1:bpa~/ S1:~"},
{"buy_full_pack", 12345, 0, true, 0, 5000, "P31:1 P32:1 P33:1 P34:1 P35:1 P36:1 P37:1 P38:1 P39:1 P40:1 P41:1 P42:1 P43:1 P44:1 P45:1 P46:1 P47:1 S2:pa~"},
{"buy_browse", 12345, 0, true, 0, 5000, "S2:bbb~ S0:b~"},
{"buy_second_page", 12345, 0, true, 0, 5000, "S2:bpc~/332 S2:bpg~~ S2:bpf~/~"},
{"buy_bad_letters", 12345, 0, true, 0, 5000, "S2:pz!a~/605"},
{"buy_escapes", 12345, 0, true, 0, 5000, "S2:p~pa~/~"},
{"buy_out_of_stock", 12345, 0, true, 0, 5000, "E0 S0:p~"},
{"buy_unknown_kinds", 777, 1, false, 1, 20000, "S5:~ S4:~ S3:~"},

// ---- selling ----
{"sell_nothing_wanted", 12345, 0, true, 0, 5000, "S5:s~"},
{"sell_masked_then_escape", 12345, 0, true, 0, 5000, "S2:sad~/~"},
{"sell_haggle", 12345, 0, true, 0, 5000, "S2:sd~/12/-1/-1/-1/-1///////~"},
{"sell_sorry", 12345, 0, true, 0, 5000, "S2:sd~/12/2/~"},
{"sell_offended", 12345, 0, true, 0, 5000, "H30:-5:0:0 D30 S2:sd~"},
{"sell_offended_kicked", 12345, 0, true, 0, 5000, "H30:-5:0:0 D30 N2:8 S2:sd S2:~"},
{"sell_no_room", 12345, 0, true, 0, 5000, "A2:31 A2:32 A2:33 A2:35 A2:36 A2:38 S2:sd~"},
{"sell_too_fine", 12345, 0, true, 0, 5000, "P349:1:20 D349 S0:s%349.~/300"},
{"sell_unknown_potion", 12345, 0, true, 0, 5000, "P243:2 S4:s%243.~/40/3/2/1/1/1/1/~"},
{"sell_good_bargainer", 12345, 0, true, 0, 5000, "B2:20:0 S2:sd~/"},
{"sell_second_page", 12345, 0, true, 0, 5000, "P80:10:9 S2:s%80.~/1 P80:10:9 S2:bs%80.~/1 S2:bsd~/1"},
{"sell_insults_kicked", 12345, 0, true, 0, 5000, "S2:sd~/16/16/16/16/16/16/16/16/16/16/16/16"},

// ---- the rest ----
{"charisma_prices", 12345, 0, true, 0, 5000, "C3 S2:~ C18 S2:~ C118 S2:~"},
{"inventory_keys", 12345, 0, true, 0, 5000, "P164:1:3 V0 S0:w%164.~~ S0:i~~ V1 S0:~"},
{"store_door", 12345, 0, true, 0, 5000, "O0,1:372 m6:~ m4: T0 m6: T100 m4: m6:~"},
