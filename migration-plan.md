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
  - The town's store entrances: storeEnter(), M13 (throws).
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
																											  
- Scope: inventory.cpp, identification.cpp (956), scrolls.cpp, staves.cpp — wielding, using, item-use dispatch. 
- Size: L (~2000+ lines combined), split by item category, each S-M.                                            
- Depends on: M3, M10 (detection/self-effect spells), M9 (combat-adjacent item uses, e.g. staff of              
teleport-other).                                                                                              
- Verify: mixed — inventory bookkeeping (stacking, weight) is golden-traceable; anything printing flavor text   
or prompting the player needs manual verification once minimal UI exists, so sequence part of this after M15  
starts rather than strictly before it.                                                                        
- From M8: Items.ItemDescription only covers the catalog-name categories (traps, gold, stairs, store entrances) and
throws for the rest -- replace it with the full itemDescription() here; the pickup/search messages and pickup prompts
in PlayerMovement are waiting on it for their text. inventoryDamageItem()/inventoryDestroyItem() unblock the fire,
acid and corroding-gas traps (with M9). (M9 did that: inventoryDamageItem()/inventoryDestroyItem() were self-contained.)
- From M9: damageMinusAC()'s armour messages carry the bare catalog name until itemDescription() exists;
player_throw.cpp (thrown objects) kills through MonsterLife.TakeHit.
- From M10: every spell an item casts now exists (Spells.*) -- the item-use dispatch only has to call them.
itemIdentify() is already ported (ObjectIdentification.ItemIdentify, with the shop/dungeon stack merge); identify's,
stone-to-mud's and genocide's messages are waiting on itemDescription() for their text.
																											  
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
- From M8: stepping onto a town store entrance (PlayerTraps.StepsOnTrap, sub_category 101-106) throws until
storeEnter() exists.
																											  
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