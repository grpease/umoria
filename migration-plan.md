# Umoria C++ → .NET Migration: Full Migration Roadmap                                                             
 
## Context                                                                                                         
The proof-of-concept phase is complete: RNG, dice, a data-table extraction pipeline, core dungeon structs, and  dungeon_generate.cpp's room/tunnel/door/stair/streamer logic are ported to dotnet/ and verified byte-for-byte identical to the real C++ game via a golden-trace harness (3 seeds, level 1). A working MSVC+vcpkg C++ toolchain exists locally (build-msvc/) to produce baselines on demand. CI is green on Windows/Linux/macOS. This was deliberately scoped down — vault room types and monster/object placement were cut from dungeon generation because reading the real call graph revealed they share one global RNG stream with room layout, not a clean     
subsystem boundary.                                                                                             
																											  
That same lesson — file/function names don't tell you the real coupling; only the call graph does — is the      
operating principle for this roadmap. The remaining ~29,000 lines (player actions, monsters, items/magic,       
stores, save/load, full UI, main loop) get broken into pieces small enough to finish and verify in one sitting, 
in an order where each piece's real dependencies (not assumed ones) are satisfied before it starts.             
																											  
Three couplings were checked directly against the source for this plan (not assumed from file names):           
- monster.cpp:818-838 calls spellBreath() directly (monster breath attacks) — monster combat has a real,        
confirmed dependency on part of spells.cpp.                                                                   
- store_inventory.cpp:354 calls magicTreasureMagicalAbility() directly — store restocking has a real, confirmed 
dependency on the item-magic engine (same function dungeonPlaceRandomObjectAt uses).                          
- playerUpdateWordOfRecall() (game_run.cpp:862) only sets dg.generate_new_level/dg.current_level flags — it     
does not call level-generation code directly, so player status effects are safely isolated from dungeon       
generation (initially feared otherwise).                                                                      
																											  
## Milestone checklist

- [x] POC prerequisites — RNG/dice, data-table extraction pipeline (regex-based), core dungeon structs, dungeon_generate.cpp room/tunnel/door/stair/streamer logic (golden-trace verified, see Context)
- [x] M0 — Real data-table extractor (libclang-based)
- [x] M1 — Core structs: Creature_t, Monster_t, Inventory_t, Player_t
- [ ] M2 — Monster placement
- [ ] M3 — Item magic/enchantment engine
- [ ] M4 — Object/treasure placement *(partial: TreasureStub.cs exists only as a minimal door/stair/gold-slot placeholder, not real object/treasure placement)*
- [ ] M5 — Dungeon lighting & line-of-sight
- [ ] M6 — Character creation
- [ ] M7 — Player status-effects subsystem
- [ ] M8 — Player movement/action subsystem
- [ ] M9 — Monster AI & melee combat, together with damage-spell primitives
- [ ] M10 — Remaining spell effects + casting layer
- [ ] M11 — Items/inventory/identification
- [ ] M12 — Save/load (JSON)
- [ ] M13 — Stores/economy
- [ ] M14 — Full UI *(partial: minimal walkable Terminal.Gui loop exists from the POC; inventory/character/store/message-log screens not started)*
- [ ] M15 — Main loop integration

## How each milestone gets verified                                                                                
																											  
Two patterns, reused from the POC:                                                                              
1. RNG/state-deterministic logic (most of the game): add a small test-only hook to the relevant C++ file (same  
 pattern as generateCaveLayoutOnlyForTesting in dungeon_generate.cpp), extend                                 
 tools/golden_trace/golden_trace_harness.cpp to call it and dump the relevant state, capture baselines via    
 build-msvc/, compare against a C# xUnit test (same pattern as GoldenTraceTests.cs).                          
2. Inherently visual/interactive logic (UI screens): manual verification in a real terminal, same as the POC's  
 walkable-loop check — automated tests only cover the data-formatting logic underneath, not the rendering     
 itself.                                                                                                      
																											  
Every milestone below states which pattern applies and what's explicitly deferred out of it.                    
																											  
## Milestone roadmap                                                                                               
																											  
Size legend: S = small, well under a thousand lines and mechanically uniform; M = moderate, a session's worth   
of focused translation; L = large, expect to subdivide further once underway.                                   
																											  
### M0 — Real data-table extractor (libclang-based)                                                                 
																											  
- Scope: data_creatures.cpp (665 lines), data_treasure.cpp (544, the game_objects[] item catalog),              
data_player.cpp (501), data_recall.cpp (91), data_store_owners.cpp (131), data_stores.cpp (56). These are     
struct-literal tables (nested Dice_t{...}, bitflags, strings) — the POC's regex script only handled flat      
numeric arrays (normal_table), so this needs the originally-planned libclang-based extractor built for real.  
- Size: M-L (tooling, not game logic).                                                                          
- Depends on: nothing new.                                                                                      
- Verify: a small C++ dump program (same spirit as the golden-trace harness) prints every field of every row; a 
C# test asserts the generated tables match field-for-field.                                                   
- Defer: nothing — all six files need to land together or later milestones stall on hand-transcription.         
																											  
### M1 — Core structs: Creature_t, Monster_t, Inventory_t, Player_t                                                 
																											  
- Scope: model monster.h/player.h/inventory.h in C#, wired to M0's data.                                        
- Size: M. Depends on: M0.                                                                                      
- Verify: unit tests constructing known creatures/items from the ported tables, checked against hand-verified   
C++ values (e.g. row 367 = open door).                                                                        
- Defer: no behavior/methods yet, pure data modeling.                                                           
																											  
### M2 — Monster placement                                                                                          
																											  
- Scope: monster_manager.cpp in full (popm, monsterPlaceNew, monsterGetOneSuitableForLevel,                     
monsterPlaceNewWithinDistance, placeMonsterAdjacentTo, compactMonsters, ~285 lines) — confirmed               
self-contained, does not need monster.cpp's AI/combat code. Wire into DungeonGenerator where the test-only    
hook currently stops.                                                                                         
- Size: S-M. Depends on: M0, M1.                                                                                
- Verify: extend generateCaveLayoutOnlyForTesting to run through monster placement, dump monster list           
(position, creature_id, hp, sleeping flag) alongside the existing floor-grid dump; new baselines for the same 
3 level-1 seeds.                                                                                              
- Defer: monsterPlaceWinning()/dungeonPlaceVaultMonster() — both only reachable via the still-excluded          
vault/unusual-room branch.                                                                                    
																											  
### M3 — Item magic/enchantment engine                                                                              
																											  
- Scope: magicTreasureMagicalAbility (treasure.cpp:854) and its ~20 helpers (magicalSword, magicalArmor,        
processRings, processAmulets, magicalChests, wandMagic, staffMagic, boots/gloves/helms/cloak variants,        
cursed-item variants). Discovered (not assumed) to be a hard prerequisite for dungeon object placement,       
confirmed also required by store restocking (store_inventory.cpp:354).                                        
- Size: L (~900 lines, but mechanically uniform — each function rolls a bonus and maybe sets a flag).           
- Depends on: M0, M1.                                                                                           
- Verify: a narrower test-only hook calling magicTreasureMagicalAbility(item_id, level) directly across a fixed 
seed/level/item_id matrix, dumping resulting Inventory_t fields (plusses, flags, cost) — no running dungeon   
needed.                                                                                                       
- Defer: store-specific pricing (priceAdjust()) stays with stores (M14).                                        
																											  
### M4 — Object/treasure placement (closes generateCave() verification gap)                                         
																											  
- Scope: dungeonAllocateAndPlaceObject, dungeonPlaceRandomObjectAt, dungeonPlaceRandomObjectNear,               
dungeonPlaceGold, dungeonSetTrap, dungeonPlaceRubble, and completing game_objects.cpp's                       
popt/pusht/itemGetRandomObjectId/compactObjects (partially stubbed already via TreasureList.Allocate()).      
- Size: M. Depends on: M2 (shares the same RNG sequence inside dungeonGenerate()), M3 (object placement calls   
into it directly).                                                                                            
- Verify: extend the golden-trace harness to exercise the real dungeonGenerate() (not the truncated test-only   
hook) for level 1, dumping floor grid + monster list + treasure list. This is the milestone where             
generateCaveLayoutOnlyForTesting can likely be retired — the only remaining exclusion after this is vaults.   
- Defer: vault-containing unusual rooms — re-confirm the current_level > randomNumber(300) reachability         
argument still holds once this milestone's RNG draws are in (one-line sanity check, not expected to change).  
																											  
### M5 — Dungeon lighting & line-of-sight                                                                           
																											  
- Scope: dungeon_los.cpp (los(), look()), plus dungeonLightRoom, dungeonLiteSpot, dungeonMoveCharacterLight,    
dungeonMoveCreatureRecord, dungeonDeleteMonster*, dungeonSummonObject, dungeonDeleteObject from dungeon.cpp.  
- Size: M. Depends on: M1 only (geometry, not RNG — independent of M2-M4).                                      
- Verify: los()/look() are pure deterministic functions of grid state — direct unit tests comparing C++ vs C#   
output across a battery of hand-built grids, no seed/RNG harness needed.                                      
- Defer: none — foundational for both monster AI (sight) and player exploration UI.                             
																											  
### M6 — Character creation                                                                                         
																											  
- Scope: character.cpp (517 lines) — stat rolling, race/class selection, starting inventory.                    
- Size: M. Depends on: M0, M1, M3 (starting inventory), M4 (if starting gold uses the same object tables —      
confirm when read).                                                                                           
- Verify: golden-trace the fully-created Player_t + starting inventory for fixed seeds. Treat with the same     
care as dungeon generation — stat rolls consume RNG in a specific order; read the actual call graph before    
assuming it doesn't reach into store pricing or anything from later milestones.                               
- Defer: nothing — needed in full before player-action milestones can be tested with a real character.          
																											  
### M7 — Player status-effects subsystem                                                                            
																											  
- Scope: carved out of game_run.cpp (lines ~252-950): poison, confusion, fear, blindness, heroism,              
regeneration, hunger, speed/slowness, paralysis, resistances, word-of-recall, status flags.                   
- Size: M (~700 lines, uniform countdown/threshold checks, several consume RNG for damage-over-time).           
- Depends on: M1, M6 (needs a real Player_t to tick).                                                           
- Verify: confirmed isolated from dungeon generation (playerUpdateWordOfRecall only sets flags, doesn't call    
level-gen directly) — a test-only hook applies a scripted sequence of ticks to a fixed starting Player_t for  
a fixed seed, dumps resulting state each tick.                                                                
- Defer: the actual level-regeneration playDungeon performs when it sees dg.generate_new_level == true — that's 
M16's job, not this milestone's.                                                                              
																											  
### M8 — Player movement/action subsystem                                                                           
																											  
- Scope: player_move.cpp (548), player_run.cpp (411), player_tunnel.cpp (176), player_bash.cpp (230),           
player_traps.cpp (219), player_stats.cpp (505). Subdivide internally: movement+running first, then            
tunnel/bash/traps, stats last.                                                                                
- Size: L combined (~2089 lines), each sub-piece S-M.                                                           
- Depends on: M5 (los/lighting), M6 (Player_t), M2/M4 (bumping into a monster/object tile).                     
- Verify: golden-trace a scripted command string against a fixed dungeon + seed, dump player                    
position/dungeon-mutation state per step.                                                                     
- Defer: combat resolution on bump-attack (needs M9); check player_traps.cpp's call graph before assuming it's  
self-contained — some trap effects likely overlap with spells.cpp's stat-loss family.                         
																											  
### M9 — Monster AI & melee combat, together with damage-spell primitives                                           
																											  
- Scope: monster.cpp (1745 lines: AI movement, melee combat) plus the bolt/ball/breath damage-spell group from  
spells.cpp (spellFireBolt, spellFireBall, spellBreath, spellEarthquake, etc.) — these two are genuinely       
coupled in both directions (monster breath attacks call spellBreath directly, confirmed at                    
monster.cpp:818-838; player-cast damage spells need monster.cpp's HP/death handling), so build and verify     
them together, not strictly sequentially. Start with melee-only AI (no spells.cpp dependency), then add       
ranged/breath attacks once the damage-spell primitives exist.                                                 
- Size: L — expect to split further into "AI movement," "melee combat," "damage-spell primitives" once          
underway.                                                                                                     
- Depends on: M2, M5, M8.                                                                                       
- Verify: golden-trace a fixed multi-turn sequence (player moves predictably, monsters act), dump monster       
positions/HP/player HP per turn.                                                                              
- Defer: monster status-effect spells (confuse/sleep/polymorph/clone/teleport/genocide) and player self-effect  
spells — those go in M10.                                                                                     
																											  
### M10 — Remaining spell effects + casting layer                                                                   
																											  
- Scope: spells.cpp's remaining groups — detection/utility (treasure/object/trap/door detection, mapping,       
identify, no monster interaction), monster-status effects, player self-effect spells (stat loss/restore,      
curing) — plus the casting layer on top: mage_spells.cpp, player_magic.cpp, player_pray.cpp (spell selection, 
cost, failure chance).                                                                                        
- Size: M-L. Depends on: M1, M5, M6, M7, and M9 for anything touching monster HP.                               
- Verify: per-group golden-trace hooks (fixed seed, fixed caster position/direction, dump                       
affected-tile/monster-state deltas).                                                                          
- Defer: nothing material; this closes out spells.cpp.                                                          
																											  
### M11 — Items/inventory/identification                                                                            
																											  
- Scope: inventory.cpp, identification.cpp (956), scrolls.cpp, staves.cpp — wielding, using, item-use dispatch. 
- Size: L (~2000+ lines combined), split by item category, each S-M.                                            
- Depends on: M3, M10 (detection/self-effect spells), M9 (combat-adjacent item uses, e.g. staff of              
teleport-other).                                                                                              
- Verify: mixed — inventory bookkeeping (stacking, weight) is golden-traceable; anything printing flavor text   
or prompting the player needs manual verification once minimal UI exists, so sequence part of this after M15  
starts rather than strictly before it.                                                                        
																											  
### M12 — Save/load (JSON)                                                                                          
																											  
- Scope: new JSON-based format per the earlier compatibility-breaking decision (not porting game_save.cpp's XOR 
binary format), game_files.cpp, scores.cpp.                                                                   
- Size: M. Depends on: M1, M6 (needs a complete-enough state to serialize meaningfully) — low RNG coupling, so  
flexible on exact timing.                                                                                     
- Verify: round-trip test (serialize, deserialize, assert struct equality) — no C++ baseline needed since the   
format is intentionally new.                                                                                  
- Defer: original save-file compatibility, by prior decision.                                                   
																											  
### M13 — Stores/economy                                                                                            
																											  
- Scope: store.cpp (1202), store_inventory.cpp (374).                                                           
- Size: M-L. Depends on: M3 (confirmed: store_inventory.cpp:354 calls magicTreasureMagicalAbility directly),    
M11 (inventory), M0 (store data tables).                                                                      
- Verify: store inventory generation is RNG-sequenced and golden-traceable; buy/sell transactions are simple    
arithmetic, unit-testable without RNG.                                                                        
- Defer: none known; check whether store restocking shares RNG draws with anything running concurrently         
mid-dungeon-turn before finalizing the harness design.                                                        
																											  
### M14 — Full UI                                                                                                   
																											  
- Scope: replace the minimal Terminal.Gui loop with inventory screen, character sheet, store screens, message   
log, status line — ui.cpp, ui_io.cpp, ui_inventory.cpp as reference for behavior, not line-for-line           
translation (curses doesn't map 1:1, same as the POC's render loop).                                          
- Size: L in line count, but mostly Terminal.Gui idiom rather than algorithmic porting. Build per-screen        
(inventory screen needs only M11, store screen only needs M13) rather than as one gated block.                
- Depends on: whichever subsystem each screen surfaces.                                                         
- Verify: manual/visual per screen, same as the POC's walkable-loop check; any pure display-string formatting   
logic underneath can still be unit-tested.                                                                    
																											  
### M15 — Main loop integration                                                                                     
																											  
- Scope: game_run.cpp's playDungeon/startMoria (the actual per-turn loop and command dispatch, once M7's        
status-effect carve-out is removed this is mostly thin glue calling into already-ported subsystems),          
game_death.cpp, final character-creation wiring.                                                              
- Size: S-M — confirms it's integration glue, not hidden logic, once M7 is carved out separately.               
- Depends on: effectively everything above.                                                                     
- Verify: full end-to-end golden trace (multi-turn scripted playthrough, fixed seed, dump full state each turn) 
plus manual interactive playtesting as the final acceptance gate.                                             
																											  
Out of scope for the whole roadmap: wizard.cpp (debug commands), recall.cpp (monster-memory text, could be      
picked up opportunistically alongside M0 if desired), original binary save format.                              
																											  
## Suggested execution order                                                                                       
																											  
M0 → M1 → M2 → M3 → M4 → M5 → M6 → M7 → M8 → M9 (interleaved with M9's spell-primitive slice) → M10 → M11 → M12 
→ M13 → M14 (incrementally, per-screen, overlapping with M11/M13) → M15.                                        
																											  
Pause after M4 and again after M9/M10: those are the two points where a hidden-coupling surprise is most likely 
(confirmed real for M3→M4 and M9→M10; worth a fresh call-graph check before committing to the next block each   
time), matching the same discipline that caught the original vault/monster coupling in the POC.                 
																											  
## Verification                                                                                                    
																											  
- Every milestone's "done" bar: new golden-trace (or unit-test, for pure-geometry/pure-data pieces) passes,     
dotnet test green, no regression in previously-passing golden traces.                                         
- Re-run build-msvc's golden_trace_harness to refresh baselines whenever a milestone extends the test-only      
hook's scope.                                                                                                 
- Push each milestone to the fork branch and confirm the GitHub Actions matrix stays green.                     
- UI milestones (M14) additionally require a manual interactive check in a real terminal, same as the POC.