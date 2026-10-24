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
- [x] M8 — Player movement/action subsystem
- [x] M9 — Monster AI & melee combat, together with damage-spell primitives
- [x] M10 — Remaining spell effects + casting layer
- [x] M11 — Items/inventory/identification
- [x] M12 — Save/load (JSON)
- [x] M13 — Stores/economy
- [x] M14 — Full UI *(core UI layer ported + screen-traced byte-for-byte via tools/ui_screen_dump (fake curses); Terminal.Gui frontend + temporary UI bench; bolt/throw animations deferred; manual terminal check pending)*
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
    (M10: checked -- every in-game path into those messages has already zeroed command_count, by a key read --
    the cast/pray/study commands read a book or menu letter first -- or by an earlier message; the new
    PlayerSpells.GainSpells uses PlayerCore.PrintMessage. Revisit if a later caller reaches
    playerCalculateAllowedSpellsCount() with neither.)
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
    compactMonsters() (M9), the keypress interrupt (input), random teleportation (playerTeleport -- ported in M8), command
    input -- and acting on dg.generate_new_level: M16 (as planned).
  - Hallucination vs rendering RNG (UI coupling, for M14): dungeon.cpp's caveGetTileSymbol() calls
    randomNumber() for every tile it renders while the player hallucinates and isn't blind, so every redraw
    (drawDungeonPanel(), panelPutTile() in the light/run code) consumes game RNG in proportion to screen
    geometry. The M7 scenarios only hallucinate while blind (which short-circuits before the draw) so the port
    stays exact. Before M14/M16, decide: reproduce those draws exactly (port caveGetTileSymbol + panel geometry +
    every redraw site) or give hallucination glyphs their own cosmetic RNG and accept a C++-divergent RNG stream
    during hallucination.

### M8 — Player movement/action subsystem

- Scope (as built): the player's step and everything it sets off, plus the direction commands. player_move.cpp
(playerMove, carry, every trap effect), player_run.cpp (the running algorithm), player_tunnel.cpp,
player_bash.cpp, player_traps.cpp (disarming, chest traps) and the rest of player_stats.cpp (stat
gain/loss/restore/boost, attack blows, charisma). Also, from player.cpp: playerMovePosition, playerTeleport (M7's
deferral), playerSearch, playerTestBeingHit, opening/closing doors and playerTunnelWall. Done --
Umoria.Core.Character.PlayerMovement / PlayerRunning / PlayerTraps / PlayerTunnel / PlayerDoors, plus PlayerStatLogic's
new half.
- Real call graph (read, not assumed):
  - The file names undersell player_move.cpp: it holds all 18 trap effects (playerStepsOnTrap is file-static there),
    while player_traps.cpp is disarming and chest traps. Trap effects reach far: monsterSummon (monster_manager.cpp),
    dungeonPlaceRandomObjectAt/dungeonSetTrap/dungeonPlaceRubble (M4), playerStatRandomDecrease (this milestone), the
    item-destroying damage*() family in inventory.cpp, and storeEnter (M13).
  - Running is driven by caveGetTileSymbol(): playerCanSeeDungeonWall()/playerSeeNothing() ask for the glyph a square
    would be drawn with. So the "renderer" M5 deferred is game logic here, and is ported as Dungeon.TileSymbol,
    hallucination RNG draws included.
  - The screen panel (ui.cpp coordOutsidePanel/coordInsidePanel, Panel_t) is game state, not just display. Every move
    re-centres it, config::options::find_bound ends a run when it shifts, and coordInsidePanel() gates the "You have
    found something!" message in playerTunnelWall(), whose printMessage() zeroes command_count. Added to the Dungeon
    model, set up by DungeonGenerator like generateCave(), and checked in DungeonLighting.ResetView like
    dungeonResetView().
  - A fourth "display" routine that mutates state: ui.cpp's displayCharacterExperience() is where level gain happens
    (playerGainLevel(): level, hit points, exp trimming, spells/mana) and max_exp is kept. Ported as
    PlayerCore.DisplayCharacterExperience. getDirectionWithMemory()'s state half (py.prev_dir) is
    PlayerCore.RememberDirection; the commands take the direction as a parameter.
  - itemDescription() turned out to be game state too, through one door: a trap's description is the death label
    (game.character_died_from, e.g. "a trap door."). Traps, gold, stairs and store entrances are its trivial
    catalog-name categories, so that subset is ported (Items.ItemDescription); everything else throws.
  - playerRandomMovement() rolls randomNumber(4) on every step, confused or not.
  - Faithfully kept quirks: the covered-pit trap replaces itself with an open pit without freeing its own treasure slot;
    playerAreaAffect() ignores the "stop" result and keeps examining squares; playerSearchOff() lowers speed even when
    not searching.
- Model changes: Dungeon.Panel (Panel_t); GameState.PlayerFreeTurn; GameOptions' running/pickup options (find_bound,
run_cut_corners, run_examine_corners, run_ignore_doors, highlight_seams, prompt_to_pickup -- config.cpp's comments on
find_bound and run_print_self are swapped, the C# ones describe the code); PlayerWorld gained ObjectPlacement,
ObjectIdentification, the running state (player_run.cpp's file statics) and a Confirm hook for the yes/no prompt.
- Verify: new tools/player_actions_dump harness. Every function it drives is already global, so it links the real
sources minus main.cpp/ui_io.cpp (the M6 headless stub, which gained a scriptable getInputConfirmation() answer; the
direction prompts read their digit from its key script) -- zero changes to src/. A hand-built 66x198 level (lit and dark
rooms, long corridors crossing panel boundaries, a running maze with an enclosed corner, a T, a crossing, a dead end,
an angled corridor and magma seams, a door corridor with plain/locked/stuck/open/broken doors, a dig corridor with
magma, quartz, permanent rock, rubble, a secret door and a lit room behind it) is mirrored by
PlayerActionsTraceTests. 99 scenarios (2.9k steps) run a scripted command string (move, run, tunnel, bash, open, close, disarm,
search, search mode, teleport) and dump one line per step: position, flags, panel, RNG and FNV hashes of the whole
floor, treasure list, monster list and inventory. They cover walking/lighting, 20 running layouts and option sets,
confusion, searching, pickup (gold, stacking, weight prompt, full pack), doors (picking, bashing, breaking, closing,
monsters in the way), tunnelling (hands, weapon, pick, heavy, confused, rubble, secret door, gold in a vein), every
portable trap type plus free-fall/free-action/sustain/dying/asleep/armoured/invulnerable/resting variants, floor and
chest disarming (level gain, the experience cap, chest traps going off, summoning), teleport, plus stat-change and
adjustment tables: 31k lines, byte-for-byte. A coverage pass over the new C# drove the later scenarios. The first full
run matched; the only failures in bring-up were scripts that bumped into a monster (C++ attacks, the port refuses) --
the test records exceptions inline so the diff pins them.
- Defer:
  - Bump attacks and bashing a monster (playerAttackMonster(), playerBashAttack() -> monsterTakeHit()): M9. Both keep
    the "too afraid" refusal and otherwise throw NotSupportedException (tested).
  - Opening a chest: openClosedChest() drops the contents through monsterDeath(): M9 (throws, tested).
  - Corroding gas, fire and acid traps: damageCorrodingGas()/damageFire()/damageAcid() destroy pack items
    (inventoryDamageItem(), inventoryDestroyItem(): M11) and are the same primitives monster breath uses -- M9's
    damage-spell slice (throw, tested). The poison gas trap's damagePoisonedGas() is self-contained and ported.
  - The town's store entrances: storeEnter(), M13 (threw until M13; now Stores.StoreCommands.Enter).
  - Message and prompt text that describes a real item (pickup, "You have found <trap>", the pickup prompts):
    itemDescription(), M11. The messages still zero command_count; only the text is missing.
  - Hallucination: running's glyph lookups draw exactly the C++ RNG, but every move also redraws through
    panelPutTile(caveGetTileSymbol()) in sub1/sub3MoveLight, and those rendering draws are still omitted -- so
    hallucinating while moving diverges until M14's decision (M7's Defer list). The harness only hallucinates while
    standing still.
  - The command layer around these: repeat counts and use_last_direction, playerRestOn()'s prompt, and the
    paralysis/turn bookkeeping between commands -- M15 (executeInputCommands). PlayerSpells' in-game messages still
    use the display-only PrintMessage (see M7); only playerGainLevel() reaches them here, right after its own
    command-zeroing message, so that is exact.

### M9 — Monster AI & melee combat, together with damage-spell primitives

- Scope (as built): all of monster.cpp -- updateMonsters() and every monster move (waking, stun recovery, normal/random/
confused/fleeing-undead movement, escaping walls, doors, glyphs, eating other monsters, picking up objects, multiplying),
melee on the player (all 24 attack types), monster spells and breath, monsterTakeHit()/monsterDeath()/monsterSleep() --
plus what they reach: spells.cpp's damage primitives (spellFireBolt, spellFireBall, spellBreath, spellChangeMonsterHitPoints,
spellDrainLifeFromMonster, spellDispelCreature, spellEarthquake) and spellTeleportAwayMonster/spellTeleportPlayerTo/
spellAggravateMonsters/spellLoseEXP; inventory.cpp's damage group (damageFire/Cold/LightningBolt/Acid/CorrodingGas/
PoisonedGas, damageMinusAC, inventoryDamageItem, inventoryDestroyItem, the set*() predicates, the item-eating and
disenchanting attacks, inventoryFindRange); player.cpp's playerAttackMonster and helpers, playerSavingThrow,
playerGainKillExperience, playerTestAttackHits, playerDiedFromString, playerAdjustBonusesForItem; player_bash.cpp's
playerBashAttack; player_magic.cpp's itemMagicAbilityDamage (slays/brands); monster_manager.cpp's compactMonsters and
monsterSummonUndead. Done -- Umoria.Core.Creatures.MonsterAi / MonsterAttacks / MonsterSpells / MonsterLife,
Character.PlayerCombat, Items.InventoryDamage, Spells.DamageSpells / SpellEffects / MagicSpellFlags.
- Real call graph (read, not assumed):
  - The plan expected one coupling (monster breath -> spellBreath). There are more, all one level deep: monster attacks reach
    inventory.cpp (item theft, eating food/light/charges, disenchanting, the elemental damage primitives that destroy pack
    items) and spells.cpp (thieves teleport away, experience drain, aggravation); monster spells reach teleport-to/away,
    monsterSummon/monsterSummonUndead and spellBreath; every damage spell and both player attacks kill through
    monsterTakeHit() -> monsterDeath() -> dungeonSummonObject() (M4/M5). Nothing reached M10's casting layer or M11's
    item-use dispatch, so the slice closed cleanly.
  - inventoryDamageItem()/inventoryDestroyItem() (deferred by M8 to "M9/M11") turned out self-contained, so M8's fire, acid
    and corroding-gas traps are unblocked here, not in M11. damagePoisonedGas() moved from PlayerTraps to InventoryDamage.
  - creature_recall[] (monster memory) is game state, not just recall display: monsterTakeHit() and spellBreath() read its
    treasure bits back before rewriting them, and nearly every encounter updates it. Added as Model.Generated.Recall,
    owned by PlayerWorld (it outlives levels, like objects_identified[]).
  - hack_monptr ("a horrible hack") is how compactMonsters()/monsterTakeHit() avoid compacting the monster array under
    updateMonsters()'s scan: monsters at or below it are only removed from the level. It lives on MonsterManager.HackMonptr;
    Popm() now calls the real CompactMonsters() instead of throwing.
  - "Display" calls re-checked per M7's lesson: printCharacterGoldValue/CurrentMana/Winner/Level/Title are pure display;
    putQIO() only sets screen_has_changed (a UI redraw hint); every printMessage() on these paths zeroes command_count and
    is routed through PlayerCore.PrintMessage. The insult attack's text choice draws RNG, so it is kept.
  - Undefined behaviour in the original: monsterMoveOutOfWall() fills only as many directions[] entries as there are open
    squares, but makeMove() always reads five -- with fewer than five it reads uninitialized stack memory. The port zeroes
    the array (direction 0 maps to the boundary corner, a skipped attempt), matching the original whenever the garbage isn't
    a valid 1-9. The harness never puts a monster in that position (earthquake is a free token for this reason).
  - Platform quirk: monsterAttackingUpdate()'s wake-up test `1L << (29 - stealth_factor)` is a 32-bit long on the
    reference MSVC build, so a stealth factor of -2 shifts into the sign bit and nothing ever notices the player. The port's
    int shift reproduces MSVC (a 64-bit-long Linux build would differ).
  - game.wizard_mode is always false (wizard.cpp is out of scope), so monsterUpdateVisibility()'s wizard-sight branch isn't
    ported.
- Model changes: Model.Generated.Recall (Recall_t) + PlayerWorld.CreatureRecall; MonsterManager.HackMonptr, a PrintMessage
hook (PlayerWorld wires it to PlayerCore.PrintMessage) for compactMonsters()'s message, CompactMonsters(), SummonUndead();
PlayerWorld switched from a primary constructor to an explicit one; MonsterConfig gained the full move/spell/defense flag
sets and the sight/spell/multiply limits; TreasureConfig gained OBJECT_BOLTS_MAX_RANGE, OBJECTS_RUNE_PROTECTION,
TR_STATS and TR_EGO_WEAPON. PlayerCore.UpdateMonstersWithoutMoving is now the real updateMonsters(false).
- Verify: new tools/monster_combat_dump harness, same build pattern as M8 (real sources minus main.cpp/ui_io.cpp plus the
headless stub) -- zero changes to src/. A hand-built arena (lit room with a granite pillar and an enclosed 3x3 block, a
corridor with closed/locked/stuck/secret doors, a dark room) is mirrored by MonsterCombatTraceTests. Each scenario places
monsters from a spec string (creature, position, sleep/hp/confused/stunned overrides, or a fill of N copies) and runs a
script where every turn-taking command is followed by playDungeon()'s `game_turn++; updateMonsters(true)`; two lines per
step (player/RNG state with hashes of floor, treasure, inventory and monster memory, then every monster). 104 scenarios,
32k lines: player melee (weapons, bare hands, missiles and ego missiles with every slay/brand, criticals, bashing,
stunning, the confusing touch), protection from evil, death; sleeping/stealth/aggravation/resting, random movers, a fast
player, breeding, both compaction paths (main loop and from inside updateMonsters() at the 125-monster cap), cannibal
breeders, eating other monsters, picking up objects, door opening/bashing, glyphs (held, broken, under the player), phasing
ghosts, escaping and dying in rock, infravision, invisibility, blindness, losing sight of a monster, Quylthulgs; every
monster attack type incl. sustains/resistances/free action; monster spells (teleports, wounds, hold, blind, confuse, fear,
slow, drain mana, both summons, all five breaths, a breath killing the player, breath ordering vs. monster ids); the
player's bolts/balls of every element incl. resistances, monster-HP/drain-life/dispel/earthquake/sleep/aggravate, killing
the Balrog (total_winner); chests dropping their contents; and the fire/acid/corroding-gas traps. The first run matched;
a coverage pass then drove ~20 more scenarios (it found e.g. protection from evil never triggering at level 1 and mana
drain never firing at 0 mana), and they matched too. Coverlet was then blocked by Smart App Control, so the last few
targets were confirmed from the trace instead.
- Defer:
  - To M10 with the rest of spells.cpp: the monster-status spells (sleep/confuse/slow/speed monster, polymorph, clone,
    teleport-other, genocide, turn undead) and the terrain/light spells that hurt monsters as a side effect
    (spellLightLine, spellWallToMud, spellBuildWall, spellDestroyArea). monsterSleep() itself is ported (MonsterLife.Sleep).
  - To M11: damageMinusAC()'s "Your <armour> ..." messages carry the bare catalog name until itemDescription() exists
    (they still zero command_count). player_throw.cpp (thrown objects, which also kill through monsterTakeHit()) is M11's.
  - To M15: the main loop's monster glue -- the per-turn compactMonsters() check, MON_CHANCE_OF_NEW new monsters, and
    paralysis/rest skipping the command. The harness emulates only `game_turn++; updateMonsters(true)` and calls
    compactMonsters() from a token.
  - Hallucination still excluded (M7/M8's open decision): dungeonLiteSpot() redraws via caveGetTileSymbol(), which
    draws RNG while hallucinating, and monster visibility changes call it.

### M10 — Remaining spell effects + casting layer

- Scope (as built): the rest of spells.cpp -- detection and area light (treasure/object/trap/secret-door/invisible/
evil/all-monster detection, light and darken area, magic mapping), the monster-status spells (speed/slow, confuse,
sleep, polymorph, clone, teleport-other, their all-in-sight forms, mass genocide, genocide, turn undead), the terrain
and object spells (light line, starlite, stone to mud, build wall, door/trap destruction and disarming in a line or
adjacent, surround with traps/doors, destruction, glyph of warding, create food) and the player self-effects (hit point
change, the six stat-loss spells, slow poison, enchant, remove curse, restore levels, identify, recharge) -- plus the
casting layer: spells.cpp's spellGetId()/castSpellGetId(), all of mage_spells.cpp and player_pray.cpp, the rest of
player_magic.cpp, and playerGainSpells() (the study command, deferred by M6). Done -- Umoria.Core.Spells.DetectionSpells
/ MonsterStatusSpells / TerrainSpells / SpellEffects / SpellCasting, Character.PlayerMagic, PlayerSpells.GainSpells.
spells.cpp is now fully ported.
- Real call graph (read, not assumed):
  - The casting layer is mostly prompts, and the prompts have game-state effects: getKeyInput() zeroes
    game.command_count on every key, getDirectionWithMemory() writes py.prev_dir and sets player_free_turn on
    <ESCAPE>, inventoryGetInputForItemId() sets player_free_turn on <ESCAPE> and fails without reading a key on an
    empty pack. Those halves are ported in PlayerCore (GetCommand, GetInputConfirmation, GetItemId,
    GetDirectionWithMemory); the keys themselves come from two new PlayerWorld hooks, ReadCommand and SelectItem
    (Confirm already existed). M8's pickup prompts now go through GetInputConfirmation too.
  - spellIdentifyItem() reaches identification.cpp's itemIdentify(), which is game state, not display: it merges a
    store-bought stack with a dungeon-found one and inscribes cursed items. Ported with it (ObjectIdentification), plus
    the small spellItemRemoveIdentification()/itemIdentificationClearEmpty()/itemAppendToInscription() helpers that
    recharge reaches. itemDescription() is still M11: identify's, stone-to-mud's and genocide's messages carry the bare
    catalog name, like M9's armour messages (they still zero command_count).
  - "Display" calls re-checked per M7's lesson: printCharacterCurrentMana/CurrentHitPoints, displaySpellsList and
    panelPutTile are pure display. drawDungeonPanel() (magic mapping) redraws through caveGetTileSymbol(), which draws
    RNG only while hallucinating -- still the open M14 decision.
  - Quirks kept as written: spellDestroyDoorsTrapsInDirection()'s loop runs `distance <= range || open space` (its
    siblings use `&&`), so it carries on past bolt range to the first closed space; spellPolymorphMonster()'s
    "morphed = true" re-test is a no-op and an unaffected monster doesn't stop the ray; the mage remove-curse spell
    clears TR_CURSED without recalculating bonuses (the priest one, and the scroll's spellRemoveCurseFromAllWornItems(),
    differ); playerGainSpells()' bank shift reads one entry past the last valid one (uninitialized, never used again --
    the port's bank has a spare zeroed slot); playerGainLevel() halves excess experience per level gained, so one big
    experience grant doesn't reach the level it "should".
  - getAndClearFirstBit() moved from MonsterSpells to a shared Umoria.Core.Helpers.
- Model changes: PlayerWorld.ReadCommand/SelectItem; GameState.UseLastDirection (read by getDirectionWithMemory(), set
by M15's command loop); MonsterManager.MonsterLevels (polymorph's random-creature pick).
- Verify: new tools/spell_casting_dump harness, same build pattern as M9 (real sources minus main.cpp/ui_io.cpp plus the
headless stub) -- zero changes to src/. M9's arena and monster spec language; casting tokens carry a key script that
both sides feed to the real prompts (book letter, spell letter incl. upper-case confirm, '*' list, unknown letters,
<ESCAPE>, direction, genocide's monster letter, the recharge/identify item), and the number of keys each command read is
dumped -- a spell that fails its roll never reads its direction, so "same keys consumed" is part of the check. Three
lines per step (player/RNG state with hashes, spell state -- learnt/worked/forgotten/new, fast, bless, detect
invisible, protection, invulnerability, resistances, prev_dir -- and every monster) and the full player at the end. 84
scenarios, 24.6k lines: study (mage menu, random priest prayers, missing books, blind/dark/confused), every refusal path
of both cast commands, low-mana confirmations and fainting, every spell in all four mage and four priest books (each
succeeds at least once -- checked via spells_worked), Rogue and Paladin casters, and each spells.cpp function called
directly against chosen monsters (light-sensitive worms/bats/eyes, stone golems, earth spirit, Xorn, undead, sleep-immune
jellies, the Balrog for the genocide/polymorph exemptions) and terrain (the door corridor, rubble, traps, chests, room
walls). Byte-for-byte on the first run; a coverage pass (coverlet, once, before Smart App Control blocked it again) then
drove 18 more scenarios -- identify's merge in both stack orders, the "damned" inscription, turn undead succeeding,
holy word stacking invulnerability, recharge's auto-failure and identified-wand paths, chests hit by each disarm spell,
stone to mud through walls and a rubble row -- also byte-for-byte; the last few targets were confirmed from the trace.
- Harness/stub notes: the M6 stub's header said getKeyInput() zeroes command_count but the code didn't; it now does
(and getInputConfirmation() too, which reads through getKeyInput() in the real game). Every older baseline regenerates
byte-identical, since no earlier harness ever reached a prompt with a count pending. A destroy-pack-item token is
guarded to the pack: on an empty pack inventoryDestroyItem() writes py.inventory[-1].
- Not covered: identifying a worn item -- with a non-empty pack inventoryGetInputForItemId() only offers the pack, and
the empty-pack equipment menu maps letters to worn slots, which is UI (M14). The two `default:` arms of the cast
switches are unreachable.
- Defer:
  - To M11: the item callers of these spells (wands, staffs, scrolls, potions -- e.g. starlite, door/trap destruction,
    enchant, surround with traps/doors are only reachable from items); itemDescription() for the messages above.
  - To M14: the equipment-menu half of inventoryGetInputForItemId() and the roguelike-keys mapping in
    getDirectionWithMemory(); the '*' spell list display; hallucination (as before).
  - To M15: the study and cast commands' key dispatch, and setting game.use_last_direction for counted commands.

### M11 — Items/inventory/identification

- Scope (as built): the rest of identification.cpp -- magicInitializeItemNames() (the flavour-name shuffle and made-up
scroll titles), the full itemDescription(), itemChargesRemainingDescription()/itemTypeRemainingCountDescription(),
itemSetAsTried(), itemRemoveMagicNaming(), itemInscribe()/itemReplaceInscription() -- plus every item-use command:
player_eat.cpp, player_quaff.cpp, scrolls.cpp, staves.cpp (staffs and wands), player_throw.cpp, inventoryDropItem() and
player.cpp's equipment helpers (playerTakeOff(), the worn-item predicates), and ui_inventory.cpp's command logic
(inventoryExecuteCommand(): show inventory/equipment, wear/wield, take off, drop, exchange weapons). Done --
Umoria.Core.Items.ItemDescription / ObjectIdentification / PlayerEat / PlayerQuaff / Scrolls / Staves / PlayerThrow /
InventoryActions / InventoryCommands / ItemUse.
- Data: the flavour-name tables (colors, mushrooms, woods, metals, rocks, amulets, syllables) live in data_tables.cpp,
which M0's libclang extractor didn't cover -- the POC's regex-era DataTables.cs (normal_table only) did. The extractor
now takes data_tables.cpp as a seventh file (DataTablesData.cs: the seven name tables, blows_table, normal_table), the
old DataTables.cs is gone, and M8's hand-copied blows_table is replaced by the generated one. data_table_dump dumps the
new tables (615 more fields), byte-for-byte. The shuffle mutates the tables in place in C++, so the port keeps
per-game copies on ObjectIdentification.
- Real call graph (read, not assumed):
  - ui_inventory.cpp is not just UI: wearing, taking off, dropping and exchanging live there, mixed with the screen state
    the command loop branches on (game.screen's current screen and wear range, game.doing_inventory_command) and with
    prompts that carry state (every key read zeroes command_count). Ported whole except the drawing (item lists, help,
    headers, the screen's left/bottom positions); keys come through PlayerCore.GetCommand, as M10's prompts do. New
    GameState fields: MagicSeed, DoingInventoryCommand, Screen (InventoryScreen), ScreenHasChanged.
  - drawCavePanel() (the blindness mushroom) is not pure display: printCharacterStatsBlock() reaches
    printCharacterMovementState() (PY_REPEAT upkeep, when searching/resting) and printCharacterStudyInstruction()
    (clears PY_STUDY) -- PlayerCore.DrawCavePanel. printCharacterHungerStatus() and printCharacterCurrentMana() are pure
    display.
  - Found while wiring messages: M6's PlayerSpells allowance update printed through the world-less
    ScreenRendering.PrintMessage, so "You can learn some new spells now." and the forget/remember messages didn't zero
    command_count -- reachable in play from INT/WIS potions and level gain. Fixed by threading an optional message sink
    through PlayerSpells.CalculateAllowedSpellsCount and the PlayerStatLogic stat-change family; in-game callers pass the
    new PlayerWorld.Print. (No earlier trace could see it: none had a command count pending.)
  - getInputConfirmationWithAbort() and getStringInput() read through getKeyInput() too, so they zero command_count;
    the stub now does that, and their answers come from two new hooks (PlayerWorld.ConfirmWithAbort, ReadString).
  - Quirks kept as written: itemDescription()'s digging-tool damage prints sides twice ("(%dd%d)", sides, sides);
    inventoryItemIsCursedMessage() says "wielding" only for the head slot; scrollRead() tests TV_SCROLL2 through a
    pointer to the slot first chosen even after identify's merge moves the scroll; scroll/staff summon loops re-roll
    randomNumber() on every pass of the loop test; inventoryItemIdOfCursedEquipment() checks hands/head in a different
    order for its random pick than for the cursed one; the restore mushrooms only ever set `identified`, never clear it.
  - Old messages now carry their real text: M8's pickup / "You have found" / gold, M9's armour damage, M10's identify
    (with playerItemWearingDescription() for worn items) and stone-to-mud.
- Verify -- two new harnesses, zero changes to src/:
  - tools/item_description_dump (M3's direct pattern): for three magic seeds (cumulatively, as the shuffle works in
    place), the shuffled tables and titles, then every catalog row described in 19 states (plain, no-prefix, none, five,
    tried, store-bought, made magical at level 40, known plusses, known kind, inscribed, magik/empty/damned, the
    SHOW_HIT_DAM / NO_SHOW_P1 / SHOW_P1 overrides). 24,570 lines, byte-for-byte on the first run.
  - tools/item_use_dump (M10's pattern, arena/setup/hashes reused; the C# side reuses SpellCastingTraceTests' helpers,
    now internal): the stub gained an opt-in message log, so every printMessage() text is compared, plus a line of
    item/screen state and an objects_identified hash per step, and every carried item's description at the end. The
    scenario list is one file, tools/item_use_dump/item_use_scenarios.inc, #included by the C++ and parsed by the C# test,
    so the two can't drift. 60 scenarios / ~19.8k lines: every food, potion, scroll, staff and wand kind; refusals (blind,
    dark, confused, empty pack, none of the kind, <ESCAPE> at each prompt); device failures and empty charges;
    identification by use, "tried" marks, identify's shop/dungeon merge; enchant/curse with and without equipment;
    throwing with and without the matching launcher, unlit targets, kills, misses into walls; wear/replace, the ring-hand
    prompt (bell, L/R verify), upper-case verify, cursed equipment, heavy weapons, exchange, drop one/all/abort, throw-off
    via '/', full pack, inscriptions and inscription-digit selection, the screen keys (i, e, ?, *). Byte-for-byte on the
    first run; three unreached branches (identify merge, restore mana, "nothing to wear") were then scripted and also
    matched. Every older baseline regenerates byte-identical with the stub changes.
- Not covered: "You can't carry it.  Drop it?" in take-off is unreachable (a full pack returns earlier unless an inventory
  command is being re-entered -- M15); hallucination stays excluded (eating the mushroom ends its scenario).
- Defer:
  - To M12: game.magic_seed, objects_identified[] and inscriptions are save-file state (the flavour names are re-derived
    from the seed, not saved).
  - To M13: inventoryTakeOneItem() (only the store's buy/sell use it).
  - To M14: drawing the inventory/equipment/help screens and their positions; screen_has_changed (set by the terminal
    layer and monster visibility changes); the equipment-menu half of inventoryGetInputForItemId() (still); hallucination
    (drawCavePanel()'s caveGetTileSymbol() RNG, as before).
  - To M15: dispatching the item commands; re-entering a left-open inventory command at the start of a turn
    (doing_inventory_command and the "Continuing with inventory command?" prompt); calling
    ObjectIdentification.MagicInitializeItemNames(game.magic_seed) after character creation, as startMoria() does
    (seedsInitialize() isn't ported yet).

### M12 — Save/load (JSON)

- Scope: new JSON-based format per the earlier compatibility-breaking decision (not porting game_save.cpp's XOR
  binary format), game_files.cpp, scores.cpp.
- Size: M. Depends on: M1, M6 (needs a complete-enough state to serialize meaningfully) -- low RNG coupling, so
  flexible on exact timing.
- Done -- Umoria.Core.Persistence.GameSave / SaveFile (save/load), HighScores (scores.cpp), CharacterSheet
  (game_files.cpp's character file), plus Items.ItemValue (storeItemValue(), pulled forward from M13).
- Format: SaveFile holds exactly what svWrite() writes, grouped the way svWrite() orders it -- monster memories and
  options (always restored), the character (skipped for a dead one), the level (absent for a dead one, which is how
  loadGame() tells a resurrection apart). The player's misc/stats/flags blocks are serialized whole (svWrite() writes
  every field of them); tiles are svWrite()'s packed byte as two hex digits, one string per row; byte arrays are number
  arrays, not base64. The scoreboard is a JSON list (HighScoreFile), highest first.
- Real call graph (read, not assumed):
  - The logic around the bytes is game logic, and it's ported as written: saveChar()'s pre-save cleanup
    (playerDisturb(1, 0), playerChangeSpeed(-heaviness)); the file rules (O_EXCL -- never replace a file this game
    didn't load unless a wizard agrees; saveGame()'s delete-or-rename retry loop); svWrite()'s hangup rule (eof_flag
    clears character_is_dead); svWrite() saving playerCalculateTotalPoints() into the max_score slot; loadGame()'s
    dead / resurrect / retired paths and its post-restore fixups (weapon_is_heavy, heaviness, playerStrength(),
    "(alive and well)", character_generated).
  - saveChar() draws randomNumber(256) for the XOR key. Meaningless for JSON, but kept so the RNG stream after a save
    (a failed one plays on) stays the original's.
  - The RNG state is NOT saved by the original: a restored game continues from startMoria()'s fresh
    seedsInitialize(). magic_seed and town_seed are saved; the flavour names are re-derived from the former.
  - playerCalculateTotalPoints() needs storeItemValue() (store_inventory.cpp, M13's file). It's pure (no RNG, no store
    state), so it moved into Items.ItemValue now; M13 reuses it.
  - New state: GameState.TownSeed, CharacterGenerated, CharacterSaved, WizardMode, ToBeWizard, NoScore, PanicSave,
    EofFlag; GameOptions.DisplayCounts, UseRoguelikeKeys, ShowInventoryWeights, ErrorBeepSound (the four the save
    file stores that no ported code read yet). game_save.cpp's file statics (save file name, from_save_file,
    start_time) live on the GameSave instance; the clock is injectable.
  - Quirks kept: loadGame()'s `(!game.noscore) & 0x04` is never true, so "already on the scoreboard" never prints;
    recordNewHighScore() strips a leading "a"/"an" by testing only the first letters ("ant lion" -> "t lion"); the
    score file cap only limits how far down an entry can land, it never trims the file. Not kept: loadGame()'s
    off-by-one coordinate check (`x > MAX_WIDTH`), and HighScore_t's fixed name/died_from buffers (strcpy overflowed
    on a cause of death over 24 characters).
- Verify -- tools/persistence_dump (M11's stub pattern, zero changes to src/; the stub gained an opt-in log of
  putStringClearToEOL() lines). The JSON files have no counterpart, so it compares everything the original defines
  around them, in four sections, each its own C# test (PersistenceTraceTests):
  - storeItemValue() for all 420 catalog objects in 10 identification states (4,200 lines);
  - outputPlayerCharacterToFile()'s text and playerCalculateTotalPoints() for 63 created characters in four variants
    (worn kit, stats over 18, level 40 / over 40, empty equipment and pack, negative skills), plus the
    replace-existing-file prompt;
  - recordNewHighScore() over 17 scenarios from a shared tools/persistence_dump/score_scenarios.inc (ties,
    "(saved)" duplicates above and below, the shift that drops a duplicate, articles, noscore and panic, 45 entries
    over three pages), reading the file back and capturing showScoresScreen()'s lines;
  - a saveGame() -> fresh-process reset -> loadGame() round trip in 9 scenarios (alive, dead, hangup, resurrect,
    resurrect declined, retired, winner, panic save, wizard), dumping the full restored state, messages and RNG
    state, so the port must save and restore exactly the same fields and apply the same fixups.
  ~13.4k lines; the first three sections matched byte-for-byte on the first run. The round trip differed in one way:
  a fresh Game_t{} has treasure.current_id 0 until treasureLinker() runs, while TreasureList starts linked -- never
  observable in play (generateCave() links first), so the harness's reset links it. Mutate-and-restore checks on both
  sides: a baseline copy edit fails one line; dropping TownSeed from the restore fails all six full restores. Every
  older stub baseline regenerates byte-identical. GameSaveTests covers what has no C++ counterpart: the JSON shape,
  version and bounds checks, damaged files, the overwrite/rename rules, the store-maintenance day count, the score
  cap. 67 tests pass.
- Not ported: the rest of game_files.cpp -- initializeScoreFile() (file opening; HighScores reads on demand),
  displaySplashScreen() / displayTextHelpFile() / displayDeathFile() (text pagers, M14), outputRandomLevelObjectsToFile()
  (a wizard command, out of scope with wizard.cpp).
- Defer:
  - To M13: the stores' save state (owner, counters, inventory -- svWrite() writes them; add a `stores` section to
    SaveFile) and loadGame()'s storeMaintenance() calls (GameSave.PendingStoreMaintenanceDays holds the count).
  - To M14: the message history (last_message_id, messages[] -- saved by svWrite(); the port has no history yet); the
    score screen's paging and keys around HighScores.FormatScoreLine; the restore status lines; the text-file pagers.
  - To M15: see M15's "From M12".

### M13 — Stores/economy

- Scope: store.cpp (1202), store_inventory.cpp (374).
- Size: M-L. Depends on: M3 (confirmed: store_inventory.cpp:354 calls magicTreasureMagicalAbility directly),
M11 (inventory), M0 (store data tables).
- Done -- Umoria.Core.Stores.Store (Store_t / InventoryRecord_t), StoreInventory (store_inventory.cpp: storeMaintenance(),
  storeItemSellPrice(), storeCheckPlayerItemsCount(), storeCarryItem(), storeDestroyItem(), storeItemCreate()) and
  StoreCommands (store.cpp: storeInitializeOwners(), storeEnter() and everything under it -- buying, selling, haggling,
  insults, the store screen's text). Config.StoreConfig holds store.h's and config::stores' constants. The data tables
  (owners, speech, store_choices, race_gold_adjustments) were already extracted by M0. Also added:
  Items.InventoryActions.TakeOneItem (inventoryTakeOneItem(), M11's deferral), Helpers.InsertNumberIntoString /
  StringToNumber, and a mask/message overload of PlayerCore.GetItemId.
- Real call graph (read, not assumed):
  - Restocking shares the game RNG and the treasure list: storeItemCreate() borrows a game.treasure.list slot
    (popt()/pusht()) and runs magicTreasureMagicalAbility() at LEVEL_TOWN_OBJECTS, so missiles_counter moves too. The
    callers are townGeneration() (every town visit), playDungeon() (every 1000 turns below the town) and loadGame()
    (once per day of save-file age) -- nothing runs concurrently with it mid-turn, which answers the roadmap's question.
  - Haggling is game logic all the way down: owner comments are random picks, each counter-offer draws
    randomNumber(5), insults and the good/bad purchase record persist in the store (and the save file), and being
    thrown out closes the store until game_turn + 2500 + randomNumber(2500). Every prompt reads through getKeyInput(),
    so each zeroes command_count. store.cpp's file-static store_last_increment lives on PlayerWorld.StoreLastIncrement.
  - inventoryGetInputForItemId()'s mask path prints its message for an out-of-range letter as well as a masked one
    ("I do not buy such items." for either), so GetItemId now range-checks the slot the SelectItem hook returns instead
    of trusting the hook to filter (older callers' hooks never return one out of range, so nothing else changes).
  - storeEnter() ends in drawCavePanel() (PY_STUDY upkeep, PlayerCore.DrawCavePanel) and its i/e/t/w/x keys run the
    real inventory commands until none is left open (Items.InventoryCommands).
  - The store screen's text is computed state (prices are charisma-adjusted at display time, "[Fixed]" after a sale,
    paging between the two halves of the stock decides which letter buys what), so it goes through a new
    PlayerWorld.ScreenWrite sink: the store's putString()/putStringClearToEOL() lines plus GetItemId's prompt line.
    Drawing them is M14's.
  - Quirks kept as written: storeGetHaggle()'s `increment` isn't reset between re-prompts (an empty line with no
    increment set, then "7", adds 7 rather than offering 7); "0" or non-numbers re-prompt; stringToNumber() leaves the
    old value on bad input; displaySingleCost() prints a haggled price without displayStoreInventory()'s clamp or field
    width; storeCheckPlayerItemsCount() checks the store's room despite its name; a "Final Offer" clamp is effectively
    only reachable from the good-bargainer shortcut (a counter-offer can't drop below the player's own offer).
  - startMoria()'s priceAdjust() is a no-op and isn't ported: its `#if (COST_ADJUSTMENT != 100)` tests a constexpr the
    preprocessor reads as 0, so the loop is compiled, but (cost * 100 + 50) / 100 leaves every catalog cost unchanged
    (none is negative; checked over all 420).
  - Not modelled: text typed before <ESCAPE> at an offer prompt (the original leaves it in the buffer, which the parser
    then reads; the ReadString hook returns a whole line or null). stringToNumber()'s range is a 32-bit long's, as on
    the MSVC build the baselines come from.
- Save file (M12's deferral): SaveFile.Character.Stores holds all six stores as svWrite() writes them (counters, then
  the stocked slots only), between the hit-point table and the save time; LoadGame bounds-checks them (six stores, at
  most 24 slots each, loadGame()'s "goto error") and now really runs StoreInventory.Maintenance once per day of file
  age (GameSave.StoreMaintenanceDays, renamed from PendingStoreMaintenanceDays, reports how many).
- Verify -- tools/store_dump (M11's stub pattern, zero changes to src/; the stub gained a list of scripted
  getStringInput() lines, <ESCAPE> included, and an opt-in putString() log), three sections, each its own C# test
  (StoreTraceTests):
  - maint: storeInitializeOwners() and twelve days of storeMaintenance() from four seeds -- owners, every store's full
    stock (cost, item fields, description) and the RNG/treasure state after each day (4,335 lines);
  - price: storeItemSellPrice() for all 420 catalog objects at each of the 18 owners (7,578 lines);
  - scenario: 37 scripted visits to the real storeEnter() from tools/store_dump/store_scenarios.inc (shared with the C#
    test): buying with and without haggling, exact/over/under offers, the first-offer rules, the RETURN quirk, liar and
    kicked-out, the good-bargainer shortcut and its "Final Offer" rejection, a full pack, browsing and both halves of
    the stock, out of stock; selling refused kinds, masked and out-of-range letters, cursed/damaged items (and being
    thrown out for it), no room, the owner out of money, an unknown potion identified by the sale, stacking arrows
    onto the store's (group re-pricing), every display branch after a sale; charisma's effect on prices; the inventory
    keys; locked doors; stepping onto a store door through playerMove(). After each token: a state line, every store
    whose state changed, every message and every screen line (16,686 lines).
  28.6k lines in all, byte-for-byte on the first run. Mutation checks: a one-character baseline edit fails exactly that
  line in both message and price sections; changing the counter-offer's `- 3` to `- 2` fails the visits trace. The
  screen lines of a visit stop where drawCavePanel() starts (its first write is at row 2, column 0, where the store
  never writes -- the redraw is display only), and `V0`/`V1` turn screen comparison off around the inventory keys
  (M14 screens the port doesn't draw).
  - tools/persistence_dump's round trip now stocks the stores before saving and dumps them after restoring (C++
    svWrite()/loadGame() against the JSON section); its baseline changed only by the stores' lines and the RNG /
    missiles_counter they move. GameSaveTests adds the stores round trip, the out-of-bounds store data, and restore-
    time maintenance equal to a same-day restore plus N StoreInventory.Maintenance calls. Every older stub baseline
    regenerates byte-identical. 76 tests pass.
- Not covered: storeSellHaggle()'s `current_asking_price < 1` clamp (max_buy and max_gold are both at least 1 there);
  GetItemId's "You are not carrying anything." from the sell command ("You have nothing to sell" returns first).
- Found, not in scope -- town generation: generateCave()'s town branch (townGeneration(): dungeonPlaceTownStores() /
  dungeonBuildStore(), which place the store buildings and door objects, lightTown() with its townsfolk, the town_seed
  reseed, and a storeMaintenance() call) was never ported -- the POC scoped DungeonGenerator to levels >= 1 and no
  milestone owned the town. The harness places a store door object by hand to test the entrance. Handed to M15
  (below), where generateCave()'s level dispatch lives.
- Defer:
  - To M14: drawing the store screen from the ScreenWrite lines (owner, item list, prices, "- cont. -", gold, the
    command help, the haggle prompt and "Asking/Offer : N" lines); the bell; ^R redraw.
  - To M15: storeInitializeOwners() in startMoria() for a new game (before character creation's RNG use, as
    game_run.cpp:43 orders it); playDungeon()'s storeMaintenance() every 1000 turns below the town; town generation
    (see above).

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
- From M8: the hallucination decision now also covers movement -- sub1/sub3MoveLight redraw through
caveGetTileSymbol() on every step (running's own glyph lookups are already exact, Dungeon.TileSymbol).
																											
- From M11: the inventory/equipment/help screens are game-state-complete (Items.InventoryCommands tracks
game.screen's screen id and wear range); only their drawing and screen_has_changed are left.
- From M12: the message history the save file should carry (svWrite()'s last_message_id + messages[]); the
score screen (paging HighScores.FormatScoreLine lines, header, <ESCAPE>); splash/help/death text pagers;
CharacterSheet.StatsAsString / StatRating are the character screen's formatters too.
- From M13: the store screen -- StoreCommands writes every line it shows through PlayerWorld.ScreenWrite (row, column,
text, clear-to-end-of-line), so the screen is a renderer over those lines plus the bell and ^R redraw.

- Done (M14): Umoria.Core/Ui -- Terminal (a virtual 80x24 curses: ui_io.cpp is the only file of the original that
calls curses, so everything above it is ported code) and GameUi (ui_io.cpp, ui.cpp, ui_inventory.cpp's display half
and inventoryGetInputForItemId(), the ^P/M/L screens, the score table and text pagers). PlayerWorld.Ui is null
headless; display call sites call `w.Ui?.X()`, and dungeon-level code (lighting, panel) draws through
ScreenRendering, keyed by Dungeon. Every printCharacter*() site in ported code (~63) and every map redraw site
(LiteSpot, light/room/panel redraws, detection sprites, running) is wired. Umoria.Console is a Terminal.Gui view
over the Terminal with the game on its own thread, plus a temporary bench (Bench/UiBench.cs) until M15.
- Decided: hallucination glyphs on screen use a cosmetic RNG (TileSymbol.Get(w, coord, rng)); running's glyph
lookups keep the game RNG, so the game's random stream never depends on rendering.
- Verified: tools/ui_screen_dump keeps the REAL ui_io.cpp and swaps curses for a headless shim
(tools/ui_screen_dump/shim/curses.h, same semantics as Ui.Terminal), #including game_run.cpp for its static
screens; zero src/ changes. 42 scenarios (ui_screen_scenarios.inc, shared with UiScreenTraceTests), every screen at
every key read, ~13.4k lines byte-for-byte; mutate-and-restore done. Hallucinating screens are excluded by design.
- Findings: monster.cpp sets screen_has_changed on visibility changes (ported into MonsterAi); resetDungeonFlags()
is what marks the player's tile; a message left waiting makes the next clearScreen() read a -more- key.
- Deferred: the bolt/ball/breath and throw animations (panelPutTile('*') + putQIO() per square with no delay, only
visible on a slow terminal); roguelike key translation; the character creation screens (character.cpp's prompts --
CharacterCreation still takes a CharacterChoices record). Manual check in a real terminal still pending.

### M15 — Main loop integration                                                                                     
																											  
- Scope: game_run.cpp's playDungeon/startMoria (the actual per-turn loop and command dispatch, once M7's        
status-effect carve-out is removed this is mostly thin glue calling into already-ported subsystems),          
game_death.cpp, final character-creation wiring.                                                              
- Size: S-M — confirms it's integration glue, not hidden logic, once M7 is carved out separately.               
- Depends on: effectively everything above.                                                                     
- Verify: full end-to-end golden trace (multi-turn scripted playthrough, fixed seed, dump full state each turn) 
plus manual interactive playtesting as the final acceptance gate.                                             
- From M8: the player commands take their direction as a parameter; the command layer around them -- reading the
key (getDirectionWithMemory()'s prompt half), repeat counts and use_last_direction, playerRestOn()'s prompt, and
running's find_count bookkeeping in playDungeon() -- is this milestone's glue.
- From M10: the study and cast/pray commands are ported whole (PlayerSpells.GainSpells, SpellCasting.GetAndCastMagicSpell
/ Pray), reading their keys through PlayerWorld.ReadCommand/SelectItem/Confirm; the loop only dispatches to them and
sets game.use_last_direction for counted commands.
- From M9: the per-turn monster glue -- the compactMonsters() check when the list is nearly full, MON_CHANCE_OF_NEW
new monsters, and skipping the command while paralysed or resting -- before updateMonsters(true).
																											
- From M11: dispatch the item commands (eat, quaff, read, use, aim, throw, inventory, inscribe); re-enter
game.doing_inventory_command at the start of a turn; call MagicInitializeItemNames(game.magic_seed) after creation.
- From M12: startMoria()'s restore -- hold dg.game_turn at -1 until then (the C++ initializer's value; the port's
Dungeon defaults to 0, and LoadGame refuses to restore "while still alive"), call GameSave.LoadGame, on
LoadOutcome.Failed print "Please try again without that save file." and exit, on Restored skip creation
(changeCharacterName(), current_hp < 0 -> dead) and generate a level only if Generate is set; then
MagicInitializeItemNames(game.magic_seed) as for a new game. Re-derive the screen panel after a restore (only
max_rows/max_cols are saved). The save command ("(saved)" as the cause, then endGame()), endGame()'s save on
death, recordNewHighScore() + the score screen, and the eof/hangup save are this milestone's glue.
- From M13: StoreCommands.InitializeOwners(w) in startMoria() (game_run.cpp:43, before the restore/creation branch);
StoreInventory.Maintenance(w) every 1000 turns when dg.current_level != 0 (playDungeon(), before the new-monster
roll); and town generation, never ported -- generateCave()'s current_level == 0 branch (townGeneration():
seedSet(town_seed), dungeonPlaceTownStores()/dungeonBuildStore() with the store door objects, fill, boundary walls,
stairs, seedResetToOldSeed(), dungeonNewSpot(), lightTown()'s day/night lighting and townsfolk, storeMaintenance()).
It's ~130 lines over already-ported helpers; verify it with the golden-trace harness at level 0.

- From M14: replace Umoria.Console's Bench/UiBench.cs with playDungeon(); dispatch the M14 screens
(GameUi.CommandPreviousMessage/DungeonDisplayMap/CommandLocateOnMap/ChangeCharacterName/ShowScoresScreen/
DisplayTextHelpFile/DisplaySplashScreen/DisplayDeathFile) and set game.LastCommand for ^P; executeInputCommands()'s
panelMoveCursor(py.pos) + message_ready_to_print = false (GameUi.ClearMessageReady) before each key; the
printCharacter*() calls in the loop itself; GameUi.EndOfInput for the EOF save; the character creation screens;
carry PlayerWorld.MessageHistory in the save file (svWrite()'s last_message_id + messages[]); a non-blocking read on
IKeySource for checkForNonBlockingKeyPress() (interrupting rest/run).

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