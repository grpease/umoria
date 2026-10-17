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
- [x] M2 — Monster placement
- [x] M3 — Item magic/enchantment engine
- [x] M4 — Object/treasure placement
- [x] M5 — Dungeon lighting & line-of-sight
- [x] M6 — Character creation
- [x] M7 — Player status-effects subsystem
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
popt/itemGetRandomObjectId. Done.                                                                              
- Size: M. Depends on: M2 (shares the same RNG sequence inside dungeonGenerate()), M3 (object placement calls   
into it directly).                                                                                            
- Verify: extended the golden-trace harness to exercise the real dungeonGenerate() (not the truncated test-only 
hook) for level 1, dumping floor grid + monster list + treasure list. generateCaveLayoutOnlyForTesting was     
retired — the only remaining exclusion is vaults.                                                              
- Defer: vault-containing unusual rooms (re-confirmed current_level > randomNumber(300) still never true at    
level 1, so the golden trace stays a genuine comparison). Also NOT ported: pusht()/compactObjects() --         
confirmed unreachable through M4 (compactObjects() needs Player.Pos (M6) and dungeonLiteSpot()/                
dungeonDeleteObject() (M5, pure rendering); its only caller, popt(), is also the original's own "extremely     
rare" path at LEVEL_MAX_OBJECTS=175). Revisit once M5/M6 land.  
																											  
### M5 — Dungeon lighting & line-of-sight

- Scope: los() (dungeon_los.cpp), dungeonLightRoom/dungeonLiteSpot/dungeonMoveCreatureRecord/
dungeonDeleteMonster*/dungeonSummonObject/dungeonDeleteObject (dungeon.cpp), plus pusht() (game_objects.cpp),
which became portable now that dungeonLiteSpot/dungeonDeleteObject exist. Done.
- Size: M. Depends on: M1 only (geometry, not RNG — independent of M2-M4).
- Verify: los() is a pure deterministic function of grid state as assumed — cross-checked against the real
compiled C++ game via a new tools/los_dump harness (6 hand-built grids x 10x10 all-pairs = 60,000 cases, no
seed/RNG needed). The rest (dungeonDeleteObject, dungeonSummonObject, the monster-removal primitives,
dungeonLightRoom's state mutation) are direct unit tests, not golden-traced: confirmed via grep that none of
them are reachable from dungeonGenerate() itself (they're called from player actions/spells/monster AI, all
later milestones), so there's no existing harness call site to extend yet.
- Defer, with real coupling found (not assumed -- same discipline as the Context section's three couplings):
  - look()/lookRay()/lookSee() (dungeon_los.cpp) was listed alongside los() on the assumption both were "pure
    deterministic functions of grid state." That held for los() but not look(): lookSee() prompts for
    keypresses, prints messages, and reads Player.pos/flags.blind/flags.image (M6), monster.lit + creature
    sprites (display), and the treasure list's item descriptions. It's an interactive player command, not
    geometry. Deferred to whichever milestone builds player commands/the terminal UI.
  - dungeonMoveCharacterLight (+ sub1MoveLight/sub3MoveLight) reads Player.flags.blind/carrying_light/
    running_tracker/temporary_light_only (M6) and config::options::run_print_self (UI) -- a real M6/M8
    dependency. Naturally belongs with M8's player-movement port, which already lists M5 as a prerequisite.
  - compactObjects() (popt()'s only caller at capacity) still needs Player.Pos (M6) to measure distance from
    the player -- unchanged from M4's finding, just reconfirmed now that its other blocker (dungeonLiteSpot/
    dungeonDeleteObject) is resolved.
  - compactMonsters() turned out NOT to need Player.Pos (that was M4/M2's assumption, carried over from
    popm()'s comment) -- it needs hack_monptr (monster.h), a global tied to monster.cpp's updateMonsters() AI
    loop. Deferred to M9, not M5/M6; corrected in MonsterManager.Popm()'s comment.
  - caveGetTileSymbol/panelPutTile/coordInsidePanel (terminal rendering) have no .NET equivalent yet -- no UI
    milestone is scheduled. dungeonLiteSpot is ported as a documented no-op (ScreenRendering.LiteSpot) at
    each real call site, so wiring up a real renderer later is a one-line change per caller.

																											  
### M6 — Character creation                                                                                         
																											  
- Scope: character.cpp (517 lines) in full, plus the new-character tail of startMoria() (game_run.cpp:
initializeCharacterInventory(), food, initial spell/mana setup). Done -- Umoria.Core.Character.CharacterCreation;
the prompts' answers (race, gender, re-roll count, class menu position, name) come in as CharacterChoices.
- Real call graph (read, not assumed) -- character.cpp is small, but what it calls isn't:
  - playerSetAndUseStat() (player_stats.cpp) fans out into playerRecalculateBonuses() (player.cpp, which also
    reads equipped items via inventoryCollectAllItemFlags()), playerCalculateHitPoints(), and -- for casters --
    playerCalculateAllowedSpellsCount()/playerGainMana() (player.cpp's spell-allowance layer). All ported
    (PlayerStatLogic, PlayerBonuses, PlayerSpells). None consume RNG; the spell-allowance path prints
    "You can learn some new spells now.", routed through a new documented no-op ScreenRendering.PrintMessage
    (same pattern as M5's LiteSpot).
  - Starting inventory needs inventoryCarryItem() (inventory.cpp, category-sorted insert/stacking) and the
    object-knowledge slice of identification.cpp (objects_identified[], objectPositionOffset,
    itemIdentifyAsStoreBought, itemSetColorlessAsIdentified). Ported as Umoria.Core.Items.PlayerInventory/
    ObjectIdentification; the rest of both files stays with M11.
  - M4 dependency answered: starting gold does NOT touch the object tables -- it's pure stat/social-class
    arithmetic plus one randomNumber(25). Starting items come from class_base_provisions (M0 data) copied via
    M4's InventoryCatalog.CopyFromCatalog, with no magicTreasureMagicalAbility() call, so no M3 dependency either.
  - No store-pricing reach: confirmed. startMoria()'s storeInitializeOwners() does draw RNG, but BEFORE
    characterCreate(), so it's an ordering concern for M15's full startup trace, not part of character creation.
- Model change: M1's Player/PlayerMisc/PlayerStats/PlayerFlags/PlayerPack went from positional record structs to
mutable classes with zero-valued defaults (`new Player()` == `Player_t{}`). Every player function mutates the C++
global py in place; with struct semantics nested writes (py.Misc.Level = 1) don't compile (CS1612).
- Verify: new tools/char_create_dump harness drives the REAL, unmodified characterCreate() -- unlike earlier
harnesses it can't just avoid initscr(), because characterCreate() is interleaved with curses prompts and
putString() abort()s headless. Instead that target swaps src/ui_io.cpp for tools/char_create_dump/ui_io_stub.cpp:
output calls are no-ops, getKeyInput()/getStringInput() replay a scripted key sequence (and fail loudly if the
script isn't consumed exactly). No test-only copy of the creation flow was added to the game. Only C++ change:
initializeCharacterInventory() lost its `static` (same precedent as M2/M4's initializeMonsterLevels/
initializeTreasureLevels). Matrix: 3 seeds x 8 races x 2 genders x every class each race allows = 186 cases,
re-roll count cycling 0/1/2; dumps every Player_t field (except date_of_birth, wall clock), all 34 inventory slots,
objects_identified[], and the final RNG state (~49.6k lines). Byte-for-byte match on first run; covers all 6
classes, the caster spell-allowance path, and 4-line histories. Same mutate-a-baseline sanity check as M0-M5
(edited the test-output copy, not the checked-in CRLF baseline, per M5's lesson).
- Defer:
  - magicInitializeItemNames() (flavor-name shuffling, runs right after creation in startMoria()) -- item naming,
    M11. Doesn't touch Player_t.
  - playerGainSpells() (actually learning spells: a menu for mages, random picks for priests) -- only reachable
    from the in-game study command, not creation. Casting layer, M10.
  - The display side of creation (race/class menus, character sheet printing, help screens) -- M14.
  - Seeding: each traced case calls setRandomSeed() directly, like golden_trace_harness, rather than
    startMoria()'s seedsInitialize() + storeInitializeOwners() sequence -- belongs to M13/M15.

### M7 — Player status-effects subsystem

- Scope: the per-turn player updates carved out of game_run.cpp: light fuel, heroism/super heroism,
food/hunger/fainting/starvation, hp/mana regeneration, blindness, confusion, fear, poison (con-scaled damage),
fast/slow, resting, hallucination, paralysis, protection from evil, invulnerability, blessing, heat/cold
resistance, detect invisible, timed infravision, word of recall, the status-flag housekeeping, encumbrance
(playerStrength) and the detect-enchantment roll; plus playDungeon()'s level-entry preamble. Done --
Umoria.Core.Character.PlayerStatusEffects (EnterLevel / UpdateTimedEffects / UpdateStatusUpkeep, split where
playDungeon() puts random teleportation between them).
- Real call graph (read, not assumed):
  - The status code leans on small, widely shared player.cpp helpers -- playerDisturb(), playerSearchOn/Off(),
    playerRestOff(), playerChangeSpeed() (also shifts every monster's speed), playerTakesHit() (sets
    game.character_is_dead / died-from and dg.generate_new_level), playerCarryingLoadLimit(), playerStrength(),
    and player_run.cpp's playerEndRunning(). Ported as PlayerCore.
  - playerDisturb() / blindness / light changes reach dungeonResetView() (ui.cpp) and
    dungeonMoveCharacterLight() + sub1/sub3MoveLight() (dungeon.cpp), which M5 deferred for want of a Player.
    They mutate tile light flags and py.temporary_light_only, so they're ported now (DungeonLighting.ResetView /
    MoveCharacterLight; their panel redraws are omitted).
  - updateMonsters(false) ("light, but don't move creatures") is called after every visibility change. Its
    per-monster work is monsterUpdateVisibility() -- M9. PlayerCore.UpdateMonstersWithoutMoving() is exact with
    no live monsters and throws NotSupportedException otherwise (tested), rather than silently skipping it.
  - Two "display" routines turned out to mutate game state, found by the trace rather than by assumption:
    ui.cpp's printCharacterStudyInstruction() clears PY_STUDY, and printCharacterMovementState() maintains
    PY_REPEAT. ui_io.cpp's printMessage() zeroes game.command_count for every non-null message (cancels a
    repeated command). All three state halves are ported (PlayerCore.UpdateMovementStateFlags /
    PlayerCore.PrintMessage); the display halves stay no-ops. M6's PlayerSpells still calls the display-only
    ScreenRendering.PrintMessage -- fine at creation time, but its in-game callers (M10) must use PlayerCore's.
  - Word of recall confirmed isolated from level generation: it only sets dg.current_level,
    dg.generate_new_level and one turn of paralysis.
- Model changes: Dungeon gained GameTurn and GenerateNewLevel (previously omitted as main-loop state); new
Model.GameState (Game_t subset: character_is_dead, died_from, total_winner, teleport_player, command_count,
config::options) and Character.PlayerWorld (bundles py/dg/game/monsters/treasure for player-side code).
- Verify: new tools/status_effects_dump harness. The status functions are all `static` in game_run.cpp, so
instead of un-static-ing two dozen game functions, that target drops game_run.cpp from its source list and
#includes it -- zero changes to src/ this milestone. Reuses M6's headless ui_io stub (now also reproducing
printMessage()'s command_count reset) and M6's creation code, factored out into
tools/char_create_dump/char_create_common.cpp (M6's baseline regenerates byte-identical). 18 scenarios, each a
real created character on a hand-built test level (lit room, dark doorway, open dark area; no monsters or floor
objects) with a setup and optional per-tick pokes, running playDungeon()'s preamble then the status portion of its
loop body: timed_effects (every timer at once, plus fear vs heroism, rest, strength loss/gain), starvation (hunger ->
weak -> faint (RNG) -> death), rest_regen (rest-until-done, search mode, poison blocking hp regen), light_source
(torch fading (RNG), going out, relighting, running, blind with no light), recall_down/recall_up,
detect_enchantment (level 40, 1200 turns, an unidentified +3 weapon gets ID_MAGIK), message_interrupt
(command_count/PY_REPEAT interplay), and poison at all 9 constitution adjustments plus invulnerable. One state line
per tick (incl. RNG state and lit/field-marked tile counts) and the full M6 player dump at the end: 6.5k lines.
Two mismatches surfaced and were fixed during bring-up (PY_STUDY clearing, then the PY_REPEAT/command_count
halves); final run is byte-for-byte. Same mutate-the-test-copy sanity check as before.
- Defer:
  - playDungeon() itself -- turn loop, storeMaintenance() (M13), the new-monster roll and updateMonsters(true) /
    compactMonsters() (M9), the keypress interrupt (input), random teleportation (playerTeleport, M8), command
    input -- and acting on dg.generate_new_level: M16 (as planned).
  - Hallucination vs rendering RNG (UI coupling, for M14): dungeon.cpp's caveGetTileSymbol() calls
    randomNumber() for every tile it renders while the player hallucinates and isn't blind, so every redraw
    (drawDungeonPanel(), panelPutTile() in the light/run code) consumes game RNG in proportion to screen
    geometry. The M7 scenarios only hallucinate while blind (which short-circuits before the draw) so the port
    stays exact. Before M14/M16, decide: reproduce those draws exactly (port caveGetTileSymbol + panel geometry +
    every redraw site) or give hallucination glyphs their own cosmetic RNG and accept a C++-divergent RNG stream
    during hallucination.

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
- Open decision from M7: hallucination's random glyphs (caveGetTileSymbol) draw from the game RNG on every
rendered tile -- see M7's Defer list. Settle it before (or with) the dungeon view.
																											  
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