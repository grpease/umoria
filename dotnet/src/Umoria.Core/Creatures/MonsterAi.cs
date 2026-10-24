using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Creatures;

// M9: monster movement -- src/monster.cpp's updateMonsters() and everything
// a monster's turn sets off short of attacking the player (MonsterAttacks)
// and casting (MonsterSpells): waking up, recovering from stuns, moving
// (normally, randomly, confused, fleeing, out of a wall), opening and
// bashing doors, glyphs of warding, eating other monsters, picking up
// objects, multiplying, and the visibility updates that light monsters up.
//
// The original holds `Monster_t &` references into monsters[] across these
// calls; here they are `ref` locals into MonsterManager.Monsters, which keep
// the same slot semantics (dungeonDeleteMonsterRecord() swaps the last slot
// in, exactly as before -- that is what hack_monptr guards).
//
// dungeonLiteSpot() redraws through ScreenRendering.LiteSpot (M14), and the
// visibility changes set screen_has_changed (game.ScreenHasChanged, the
// "Continuing with inventory command?" hint -- M14). game.wizard_mode is
// always false here (wizard.cpp is out of the roadmap's scope), so
// monsterUpdateVisibility()'s "wizard sight" branch is not ported.
public static class MonsterAi
{
    // src/monster.cpp:16 monsterIsVisible()
    private static bool IsVisible(PlayerWorld w, ref Monster monster)
    {
        var visible = false;

        var tile = w.Dg.Floor[monster.Pos.Y, monster.Pos.X];
        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];
        var py = w.Py;

        if (tile.PermanentLight || tile.TemporaryLight || (py.RunningTracker != 0 && monster.DistanceFromPlayer < 2 && py.CarryingLight))
        {
            // Normal sight.
            if ((creature.Movement & MonsterConfig.Move.CmInvisible) == 0)
            {
                visible = true;
            }
            else if (py.Flags.SeeInvisible)
            {
                visible = true;
                w.CreatureRecall[monster.CreatureId].Movement |= MonsterConfig.Move.CmInvisible;
            }
        }
        else if (py.Flags.SeeInfra > 0 && monster.DistanceFromPlayer <= py.Flags.SeeInfra && (creature.Defenses & MonsterConfig.Defense.CdInfra) != 0)
        {
            // Infra vision.
            visible = true;
            w.CreatureRecall[monster.CreatureId].Defenses |= MonsterConfig.Defense.CdInfra;
        }

        return visible;
    }

    // src/monster.cpp:40 monsterUpdateVisibility() -- updates screen when
    // monsters move about -RAK-
    public static void UpdateVisibility(PlayerWorld w, int monsterId)
    {
        var visible = false;
        ref var monster = ref w.Monsters.Monsters[monsterId];

        if (monster.DistanceFromPlayer <= MonsterConfig.MonMaxSight && (w.Py.Flags.Status & PlayerConfig.Status.PyBlind) == 0 &&
            ScreenPanel.CoordInsidePanel(w.Dg, monster.Pos))
        {
            // (game.wizard_mode's "wizard sight" -- see the class comment)
            if (Los.Check(w.Dg, w.Py.Pos, monster.Pos))
            {
                visible = IsVisible(w, ref monster);
            }
        }

        if (visible)
        {
            // Light it up.
            if (!monster.Lit)
            {
                PlayerCore.Disturb(w, 1, 0);
                monster.Lit = true;
                ScreenRendering.LiteSpot(w.Dg, monster.Pos);

                // notify inventoryExecuteCommand()
                w.Game.ScreenHasChanged = true;
            }
        }
        else if (monster.Lit)
        {
            // Turn it off.
            monster.Lit = false;
            ScreenRendering.LiteSpot(w.Dg, monster.Pos);

            // notify inventoryExecuteCommand()
            w.Game.ScreenHasChanged = true;
        }
    }

    // src/monster.cpp:77 monsterMovementRate() -- given speed, returns
    // number of moves this turn. -RAK-
    // NOTE: Player must always move at least once per iteration, a slowed
    // player is handled by moving monsters faster
    private static int MovementRate(PlayerWorld w, short speed)
    {
        if (speed > 0)
        {
            if (w.Py.Flags.Rest != 0)
            {
                return 1;
            }
            return speed;
        }

        // speed must be negative here
        var rate = 0;
        if (w.Dg.GameTurn % (2 - speed) == 0)
        {
            rate = 1;
        }

        return rate;
    }

    // src/monster.cpp:95 monsterMakeVisible() -- makes sure a new creature
    // gets lit up. -CJS-
    private static bool MakeVisible(PlayerWorld w, Coord coord)
    {
        int monsterId = w.Dg.Floor[coord.Y, coord.X].CreatureId;
        if (monsterId <= 1)
        {
            return false;
        }

        UpdateVisibility(w, monsterId);
        return w.Monsters.Monsters[monsterId].Lit;
    }

    // src/monster.cpp:106 monsterGetMoveDirection() -- choose correct
    // directions for monster movement -RAK-
    private static void GetMoveDirection(PlayerWorld w, int monsterId, int[] directions)
    {
        int ay, ax, movement;

        var y = w.Monsters.Monsters[monsterId].Pos.Y - w.Py.Pos.Y;
        var x = w.Monsters.Monsters[monsterId].Pos.X - w.Py.Pos.X;

        if (y < 0)
        {
            movement = 8;
            ay = -y;
        }
        else
        {
            movement = 0;
            ay = y;
        }
        if (x > 0)
        {
            movement += 4;
            ax = x;
        }
        else
        {
            ax = -x;
        }

        // this has the advantage of preventing the diamond maneuver, also faster
        if (ay > (ax << 1))
        {
            movement += 2;
        }
        else if (ax > (ay << 1))
        {
            movement++;
        }

        switch (movement)
        {
            case 0:
                directions[0] = 9;
                if (ay > ax)
                {
                    Set(directions, 8, 6, 7, 3);
                }
                else
                {
                    Set(directions, 6, 8, 3, 7);
                }
                break;
            case 1:
            case 9:
                directions[0] = 6;
                if (y < 0)
                {
                    Set(directions, 3, 9, 2, 8);
                }
                else
                {
                    Set(directions, 9, 3, 8, 2);
                }
                break;
            case 2:
            case 6:
                directions[0] = 8;
                if (x < 0)
                {
                    Set(directions, 9, 7, 6, 4);
                }
                else
                {
                    Set(directions, 7, 9, 4, 6);
                }
                break;
            case 4:
                directions[0] = 7;
                if (ay > ax)
                {
                    Set(directions, 8, 4, 9, 1);
                }
                else
                {
                    Set(directions, 4, 8, 1, 9);
                }
                break;
            case 5:
            case 13:
                directions[0] = 4;
                if (y < 0)
                {
                    Set(directions, 1, 7, 2, 8);
                }
                else
                {
                    Set(directions, 7, 1, 8, 2);
                }
                break;
            case 8:
                directions[0] = 3;
                if (ay > ax)
                {
                    Set(directions, 2, 6, 1, 9);
                }
                else
                {
                    Set(directions, 6, 2, 9, 1);
                }
                break;
            case 10:
            case 14:
                directions[0] = 2;
                if (x < 0)
                {
                    Set(directions, 3, 1, 6, 4);
                }
                else
                {
                    Set(directions, 1, 3, 4, 6);
                }
                break;
            case 12:
                directions[0] = 1;
                if (ay > ax)
                {
                    Set(directions, 2, 4, 3, 7);
                }
                else
                {
                    Set(directions, 4, 2, 7, 3);
                }
                break;
        }
    }

    private static void Set(int[] directions, int d1, int d2, int d3, int d4)
    {
        directions[1] = d1;
        directions[2] = d2;
        directions[3] = d3;
        directions[4] = d4;
    }

    // src/monster.cpp:477 monsterOpenDoor()
    private static void OpenDoor(PlayerWorld w, ref Tile tile, short monsterHp, uint moveBits, ref bool doTurn, ref bool doMove, ref uint rcmove, Coord coord)
    {
        ref var item = ref w.Treasure[tile.TreasureId];

        // Creature can open doors.
        if ((moveBits & MonsterConfig.Move.CmOpenDoor) != 0)
        {
            var doorIsStuck = false;

            if (item.CategoryId == TreasureCategory.TvClosedDoor)
            {
                doTurn = true;

                if (item.MiscUse == 0)
                {
                    // Closed doors

                    doMove = true;
                }
                else if (item.MiscUse > 0)
                {
                    // Locked doors

                    if (RandomNumber.Next((monsterHp + 1) * (50 + item.MiscUse)) < 40 * (monsterHp - 10 - item.MiscUse))
                    {
                        item.MiscUse = 0;
                    }
                }
                else if (item.MiscUse < 0)
                {
                    // Stuck doors

                    if (RandomNumber.Next((monsterHp + 1) * (50 - item.MiscUse)) < 40 * (monsterHp - 10 + item.MiscUse))
                    {
                        PlayerCore.PrintMessage(w, "You hear a door burst open!");
                        PlayerCore.Disturb(w, 1, 0);
                        doorIsStuck = true;
                        doMove = true;
                    }
                }
            }
            else if (item.CategoryId == TreasureCategory.TvSecretDoor)
            {
                doTurn = true;
                doMove = true;
            }

            if (doMove)
            {
                item = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjOpenDoor);

                // 50% chance of breaking door
                if (doorIsStuck)
                {
                    item.MiscUse = (short)(1 - RandomNumber.Next(2));
                }
                tile.FeatureId = TileConstants.TileCorrFloor;
                ScreenRendering.LiteSpot(w.Dg, coord);
                rcmove |= MonsterConfig.Move.CmOpenDoor;
                doMove = false;
            }
        }
        else if (item.CategoryId == TreasureCategory.TvClosedDoor)
        {
            // Creature can not open doors, must bash them
            doTurn = true;

            var absMiscUse = Math.Abs((int)item.MiscUse);
            if (RandomNumber.Next((monsterHp + 1) * (80 + absMiscUse)) < 40 * (monsterHp - 20 - absMiscUse))
            {
                item = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjOpenDoor);

                // 50% chance of breaking door
                item.MiscUse = (short)(1 - RandomNumber.Next(2));
                tile.FeatureId = TileConstants.TileCorrFloor;
                ScreenRendering.LiteSpot(w.Dg, coord);
                PlayerCore.PrintMessage(w, "You hear a door burst open!");
                PlayerCore.Disturb(w, 1, 0);
            }
        }
    }

    // src/monster.cpp:542 glyphOfWardingProtection()
    private static void GlyphOfWardingProtection(PlayerWorld w, ushort creatureId, uint moveBits, ref bool doMove, ref bool doTurn, Coord coord)
    {
        if (RandomNumber.Next(TreasureConfig.ObjectsRuneProtection) < DataCreaturesData.CreaturesList[creatureId].Level)
        {
            if (coord.Y == w.Py.Pos.Y && coord.X == w.Py.Pos.X)
            {
                PlayerCore.PrintMessage(w, "The rune of protection is broken!");
            }
            w.Objects.DeleteObject(coord);
            return;
        }

        doMove = false;

        // If the creature moves only to attack, don't let it
        // move if the glyph prevents it from attacking
        if ((moveBits & MonsterConfig.Move.CmAttackOnly) != 0)
        {
            doTurn = true;
        }
    }

    // src/monster.cpp:560 monsterMovesOnPlayer() -- `creatureId` is the
    // destination tile's creature_id (1 = the player).
    private static void MovesOnPlayer(PlayerWorld w, ref Monster monster, byte creatureId, int monsterId, uint moveBits, ref bool doMove, ref bool doTurn, ref uint rcmove, Coord coord)
    {
        if (creatureId == 1)
        {
            // if the monster is not lit, must call monsterUpdateVisibility, it
            // may be faster than character, and hence could have
            // just moved next to character this same turn.
            if (!monster.Lit)
            {
                UpdateVisibility(w, monsterId);
            }
            MonsterAttacks.AttackPlayer(w, monsterId);
            doMove = false;
            doTurn = true;
        }
        else if (creatureId > 1 && (coord.Y != monster.Pos.Y || coord.X != monster.Pos.X))
        {
            // Creature is attempting to move on other creature?

            // Creature eats other creatures?
            var monsters = w.Monsters.Monsters;
            if ((moveBits & MonsterConfig.Move.CmEatsOther) != 0 &&
                DataCreaturesData.CreaturesList[monster.CreatureId].KillExpValue >= DataCreaturesData.CreaturesList[monsters[creatureId].CreatureId].KillExpValue)
            {
                if (monsters[creatureId].Lit)
                {
                    rcmove |= MonsterConfig.Move.CmEatsOther;
                }

                // It ate an already processed monster. Handle normally.
                if (monsterId < creatureId)
                {
                    w.Monsters.DeleteMonster(creatureId);
                }
                else
                {
                    // If it eats this monster, an already processed
                    // monster will take its place, causing all kinds
                    // of havoc. Delay the kill a bit.
                    w.Monsters.RemoveMonsterFromLevel(creatureId);
                }
            }
            else
            {
                doMove = false;
            }
        }
    }

    // src/monster.cpp:596 monsterAllowedToMove()
    private static void AllowedToMove(PlayerWorld w, ref Monster monster, uint moveBits, ref bool doTurn, ref uint rcmove, Coord coord)
    {
        // Pick up or eat an object
        if ((moveBits & MonsterConfig.Move.CmPicksUp) != 0)
        {
            var treasureId = w.Dg.Floor[coord.Y, coord.X].TreasureId;

            if (treasureId != 0 && w.Treasure[treasureId].CategoryId <= TreasureCategory.TvMaxObject)
            {
                rcmove |= MonsterConfig.Move.CmPicksUp;
                w.Objects.DeleteObject(coord);
            }
        }

        // Move creature record
        w.Monsters.MoveCreatureRecord(monster.Pos, coord);

        if (monster.Lit)
        {
            monster.Lit = false;
            ScreenRendering.LiteSpot(w.Dg, monster.Pos);
        }

        monster.Pos = coord;
        monster.DistanceFromPlayer = (byte)CoordHelpers.DistanceBetween(w.Py.Pos, coord);

        doTurn = true;
    }

    // src/monster.cpp:623 makeMove() -- make the move if possible, five
    // choices -RAK-
    private static void MakeMove(PlayerWorld w, int monsterId, int[] directions, ref uint rcmove)
    {
        var doTurn = false;
        var doMove = false;

        ref var monster = ref w.Monsters.Monsters[monsterId];
        var moveBits = DataCreaturesData.CreaturesList[monster.CreatureId].Movement;

        // Up to 5 attempts at moving, give up.
        for (var i = 0; !doTurn && i < 5; i++)
        {
            // Get new position
            var coord = monster.Pos;

            PlayerMovement.MovePosition(w.Dg, directions[i], ref coord);

            ref var tile = ref w.Dg.Floor[coord.Y, coord.X];

            if (tile.FeatureId == TileConstants.TileBoundaryWall)
            {
                continue;
            }

            // Floor is open?
            if (tile.FeatureId <= TileConstants.MaxOpenSpace)
            {
                doMove = true;
            }
            else if ((moveBits & MonsterConfig.Move.CmPhase) != 0)
            {
                // Creature moves through walls?
                doMove = true;
                rcmove |= MonsterConfig.Move.CmPhase;
            }
            else if (tile.TreasureId != 0)
            {
                // Creature can open doors?
                OpenDoor(w, ref tile, monster.Hp, moveBits, ref doTurn, ref doMove, ref rcmove, coord);
            }

            // Glyph of warding present?
            if (doMove && tile.TreasureId != 0 && w.Treasure[tile.TreasureId].CategoryId == TreasureCategory.TvVisTrap &&
                w.Treasure[tile.TreasureId].SubCategoryId == 99)
            {
                GlyphOfWardingProtection(w, monster.CreatureId, moveBits, ref doMove, ref doTurn, coord);
            }

            // Creature has attempted to move on player?
            if (doMove)
            {
                MovesOnPlayer(w, ref monster, tile.CreatureId, monsterId, moveBits, ref doMove, ref doTurn, ref rcmove, coord);
            }

            // Creature has been allowed move.
            if (doMove)
            {
                AllowedToMove(w, ref monster, moveBits, ref doTurn, ref rcmove, coord);
            }
        }
    }

    // src/monster.cpp:919 monsterMultiply() -- places creature adjacent to
    // given location -RAK- Rats and Flys are fun!
    public static bool Multiply(PlayerWorld w, Coord coord, int creatureId, int monsterId)
    {
        var dg = w.Dg;
        var monsters = w.Monsters;

        for (var i = 0; i <= 18; i++)
        {
            var position = new Coord(coord.Y - 2 + RandomNumber.Next(3), coord.X - 2 + RandomNumber.Next(3));

            // don't create a new creature on top of the old one, that
            // causes invincible/invisible creatures to appear.
            if (CoordHelpers.InBounds(dg, position) && (position.Y != coord.Y || position.X != coord.X))
            {
                var tile = dg.Floor[position.Y, position.X];

                if (tile.FeatureId <= TileConstants.MaxOpenSpace && tile.TreasureId == 0 && tile.CreatureId != 1)
                {
                    // Creature there already?
                    if (tile.CreatureId > 1)
                    {
                        // Some critters are cannibalistic!
                        var cannibalistic = (DataCreaturesData.CreaturesList[creatureId].Movement & MonsterConfig.Move.CmEatsOther) != 0;

                        // Check the experience level -CJS-
                        var experienced = DataCreaturesData.CreaturesList[creatureId].KillExpValue >=
                                          DataCreaturesData.CreaturesList[monsters.Monsters[tile.CreatureId].CreatureId].KillExpValue;

                        if (cannibalistic && experienced)
                        {
                            // It ate an already processed monster. Handle * normally.
                            if (monsterId < tile.CreatureId)
                            {
                                monsters.DeleteMonster(tile.CreatureId);
                            }
                            else
                            {
                                // If it eats this monster, an already processed
                                // monster will take its place, causing all kinds
                                // of havoc. Delay the kill a bit.
                                monsters.RemoveMonsterFromLevel(tile.CreatureId);
                            }

                            return PlaceMultiplied(w, position, creatureId, monsterId);
                        }
                    }
                    else
                    {
                        // All clear, place a monster
                        return PlaceMultiplied(w, position, creatureId, monsterId);
                    }
                }
            }
        }

        return false;
    }

    // (the tail both of monsterMultiply()'s placements share)
    private static bool PlaceMultiplied(PlayerWorld w, Coord position, int creatureId, int monsterId)
    {
        // in case compact_monster() is called, it needs monster_id.
        w.Monsters.HackMonptr = monsterId;
        // Place_monster() may fail if monster list full.
        var result = w.Monsters.PlaceNew(position, creatureId, false, w.Py.Pos, w.Py.Flags.Speed);
        w.Monsters.HackMonptr = -1;
        if (!result)
        {
            return false;
        }

        w.Monsters.MonsterMultiplyTotal++;
        return MakeVisible(w, position);
    }

    // src/monster.cpp:985 monsterMultiplyCritter()
    private static void MultiplyCritter(PlayerWorld w, ref Monster monster, int monsterId, ref uint rcmove)
    {
        var counter = 0;

        for (var y = monster.Pos.Y - 1; y <= monster.Pos.Y + 1; y++)
        {
            for (var x = monster.Pos.X - 1; x <= monster.Pos.X + 1; x++)
            {
                if (CoordHelpers.InBounds(w.Dg, new Coord(y, x)) && w.Dg.Floor[y, x].CreatureId > 1)
                {
                    counter++;
                }
            }
        }

        // can't call randomNumber with a value of zero, increment
        // counter to allow creature multiplication.
        if (counter == 0)
        {
            counter++;
        }

        if (counter < 4 && RandomNumber.Next(counter * MonsterConfig.MonMultiplyAdjust) == 1)
        {
            if (Multiply(w, monster.Pos, monster.CreatureId, monsterId))
            {
                rcmove |= MonsterConfig.Move.CmMultiply;
            }
        }
    }

    // src/monster.cpp:1011 monsterMoveOutOfWall()
    private static void MoveOutOfWall(PlayerWorld w, ref Monster monster, int monsterId, ref uint rcmove)
    {
        // If the monster is already dead, don't kill it again!
        // This can happen for monsters moving faster than the player. They
        // will get multiple moves, but should not if they die on the first
        // move.  This is only a problem for monsters stuck in rock.
        if (monster.Hp < 0)
        {
            return;
        }

        var id = 0;
        var dir = 1;

        // The original leaves the unfilled entries of its directions[9]
        // uninitialized, and makeMove() always reads five: with fewer than
        // five open squares around the monster it reads stack garbage
        // (undefined behaviour). Zero here, which playerMovePosition() maps
        // to the boundary corner (0,0) -- a skipped attempt -- matching the
        // original whenever that garbage is not a valid 1-9 direction.
        var directions = new int[9];

        // Note direction of for loops matches direction of keypad from 1 to 9
        // Do not allow attack against the player.
        for (var y = monster.Pos.Y + 1; y >= monster.Pos.Y - 1; y--)
        {
            for (var x = monster.Pos.X - 1; x <= monster.Pos.X + 1; x++)
            {
                if (dir != 5 && w.Dg.Floor[y, x].FeatureId <= TileConstants.MaxOpenSpace && w.Dg.Floor[y, x].CreatureId != 1)
                {
                    directions[id] = dir;
                    id++;
                }
                dir++;
            }
        }

        if (id != 0)
        {
            // put a random direction first
            dir = RandomNumber.Next(id) - 1;

            (directions[0], directions[dir]) = (directions[dir], directions[0]);

            // this can only fail if directions[0] has a rune of protection
            MakeMove(w, monsterId, directions, ref rcmove);
        }

        // if still in a wall, let it dig itself out, but also apply some more damage
        if (w.Dg.Floor[monster.Pos.Y, monster.Pos.X].FeatureId >= TileConstants.MinCaveWall)
        {
            // in case the monster dies, may need to callfix1_delete_monster()
            // instead of delete_monsters()
            w.Monsters.HackMonptr = monsterId;
            var i = MonsterLife.TakeHit(w, monsterId, DiceRoller.Roll(new Model.Generated.Dice(8, 8)));
            w.Monsters.HackMonptr = -1;

            if (i >= 0)
            {
                PlayerCore.PrintMessage(w, "You hear a scream muffled by rock!");
                PlayerCore.DisplayCharacterExperience(w);
            }
            else
            {
                PlayerCore.PrintMessage(w, "A creature digs itself out from the rock!");
                PlayerTunnel.TunnelWall(w, monster.Pos, 1, 0);
            }
        }
    }

    // src/monster.cpp:1070 monsterMoveUndead() -- undead only get confused
    // from turn undead, so they should flee
    private static void MoveUndead(PlayerWorld w, in Creature creature, int monsterId, ref uint rcmove)
    {
        var directions = new int[9];
        GetMoveDirection(w, monsterId, directions);

        directions[0] = 10 - directions[0];
        directions[1] = 10 - directions[1];
        directions[2] = 10 - directions[2];
        directions[3] = RandomNumber.Next(9); // May attack only if cornered
        directions[4] = RandomNumber.Next(9);

        // don't move if it's is not supposed to move!
        if ((creature.Movement & MonsterConfig.Move.CmAttackOnly) == 0)
        {
            MakeMove(w, monsterId, directions, ref rcmove);
        }
    }

    private static int[] RandomDirections()
    {
        var directions = new int[9];
        for (var i = 0; i < 5; i++)
        {
            directions[i] = RandomNumber.Next(9);
        }

        return directions;
    }

    // src/monster.cpp:1086 monsterMoveConfused()
    private static void MoveConfused(PlayerWorld w, in Creature creature, int monsterId, ref uint rcmove)
    {
        var directions = RandomDirections();

        // don't move if it's is not supposed to move!
        if ((creature.Movement & MonsterConfig.Move.CmAttackOnly) == 0)
        {
            MakeMove(w, monsterId, directions, ref rcmove);
        }
    }

    // src/monster.cpp:1101 monsterDoMove()
    private static bool DoMove(PlayerWorld w, int monsterId, ref uint rcmove, ref Monster monster, in Creature creature)
    {
        // Creature is confused or undead turned?
        if (monster.ConfusedAmount != 0)
        {
            if ((creature.Defenses & MonsterConfig.Defense.CdUndead) != 0)
            {
                MoveUndead(w, creature, monsterId, ref rcmove);
            }
            else
            {
                MoveConfused(w, creature, monsterId, ref rcmove);
            }
            monster.ConfusedAmount--;
            return true;
        }

        // Creature may cast a spell
        if ((creature.Spells & MonsterConfig.Spells.CsFreq) != 0)
        {
            return MonsterSpells.CastSpell(w, monsterId);
        }

        return false;
    }

    // src/monster.cpp:1121 monsterMoveRandomly()
    private static void MoveRandomly(PlayerWorld w, int monsterId, ref uint rcmove, uint randomness)
    {
        var directions = RandomDirections();

        rcmove |= randomness;

        MakeMove(w, monsterId, directions, ref rcmove);
    }

    // src/monster.cpp:1135 monsterMoveNormally()
    private static void MoveNormally(PlayerWorld w, int monsterId, ref uint rcmove)
    {
        int[] directions;

        if (RandomNumber.Next(200) == 1)
        {
            directions = RandomDirections();
        }
        else
        {
            directions = new int[9];
            GetMoveDirection(w, monsterId, directions);
        }

        rcmove |= MonsterConfig.Move.CmMoveNormal;

        MakeMove(w, monsterId, directions, ref rcmove);
    }

    // src/monster.cpp:1153 monsterAttackWithoutMoving()
    private static void AttackWithoutMoving(PlayerWorld w, int monsterId, ref uint rcmove, byte distanceFromPlayer)
    {
        if (distanceFromPlayer < 2)
        {
            var directions = new int[9];
            GetMoveDirection(w, monsterId, directions);
            MakeMove(w, monsterId, directions, ref rcmove);
        }
        else
        {
            // Learn that the monster does does not move when
            // it should have moved, but didn't.
            rcmove |= MonsterConfig.Move.CmAttackOnly;
        }
    }

    // src/monster.cpp:1167 monsterMove() -- move the critters about the
    // dungeon -RAK-
    private static void Move(PlayerWorld w, int monsterId, ref uint rcmove)
    {
        ref var monster = ref w.Monsters.Monsters[monsterId];
        var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

        // Does the critter multiply?
        // rest could be negative, to be safe, only use mod with positive values.
        var absRestPeriod = Math.Abs((int)w.Py.Flags.Rest);
        if ((creature.Movement & MonsterConfig.Move.CmMultiply) != 0 && MonsterConfig.MonMaxMultiplyPerLevel >= w.Monsters.MonsterMultiplyTotal &&
            absRestPeriod % MonsterConfig.MonMultiplyAdjust == 0)
        {
            MultiplyCritter(w, ref monster, monsterId, ref rcmove);
        }

        // if in wall, must immediately escape to a clear area
        // then monster movement finished
        if ((creature.Movement & MonsterConfig.Move.CmPhase) == 0 && w.Dg.Floor[monster.Pos.Y, monster.Pos.X].FeatureId >= TileConstants.MinCaveWall)
        {
            MoveOutOfWall(w, ref monster, monsterId, ref rcmove);
            return;
        }

        if (DoMove(w, monsterId, ref rcmove, ref monster, creature))
        {
            return;
        }

        // 75% random movement
        if ((creature.Movement & MonsterConfig.Move.Cm75Random) != 0 && RandomNumber.Next(100) < 75)
        {
            MoveRandomly(w, monsterId, ref rcmove, MonsterConfig.Move.Cm75Random);
            return;
        }

        // 40% random movement
        if ((creature.Movement & MonsterConfig.Move.Cm40Random) != 0 && RandomNumber.Next(100) < 40)
        {
            MoveRandomly(w, monsterId, ref rcmove, MonsterConfig.Move.Cm40Random);
            return;
        }

        // 20% random movement
        if ((creature.Movement & MonsterConfig.Move.Cm20Random) != 0 && RandomNumber.Next(100) < 20)
        {
            MoveRandomly(w, monsterId, ref rcmove, MonsterConfig.Move.Cm20Random);
            return;
        }

        // Normal movement
        if ((creature.Movement & MonsterConfig.Move.CmMoveNormal) != 0)
        {
            MoveNormally(w, monsterId, ref rcmove);
            return;
        }

        // Attack, but don't move
        if ((creature.Movement & MonsterConfig.Move.CmAttackOnly) != 0)
        {
            AttackWithoutMoving(w, monsterId, ref rcmove, monster.DistanceFromPlayer);
            return;
        }

        if ((creature.Movement & MonsterConfig.Move.CmOnlyMagic) != 0 && monster.DistanceFromPlayer < 2)
        {
            var memory = w.CreatureRecall[monster.CreatureId];

            // A little hack for Quylthulgs, so that one will eventually
            // notice that they have no physical attacks.
            if (memory.Attacks[0] < byte.MaxValue)
            {
                memory.Attacks[0]++;
            }

            // Another little hack for Quylthulgs, so that one can
            // eventually learn their speed.
            if (memory.Attacks[0] > 20)
            {
                memory.Movement |= MonsterConfig.Move.CmOnlyMagic;
            }
        }
    }

    // src/monster.cpp:1235 memoryUpdateRecall()
    private static void UpdateRecall(PlayerWorld w, ref Monster monster, bool wake, bool ignore, uint rcmove)
    {
        if (!monster.Lit)
        {
            return;
        }

        var memory = w.CreatureRecall[monster.CreatureId];

        if (wake)
        {
            if (memory.Wake < byte.MaxValue)
            {
                memory.Wake++;
            }
        }
        else if (ignore)
        {
            if (memory.Ignore < byte.MaxValue)
            {
                memory.Ignore++;
            }
        }

        memory.Movement |= rcmove;
    }

    // src/monster.cpp:1255 monsterAttackingUpdate()
    private static void AttackingUpdate(PlayerWorld w, ref Monster monster, int monsterId, int moves)
    {
        var py = w.Py;

        for (var i = moves; i > 0; i--)
        {
            var wake = false;
            var ignore = false;

            uint rcmove = 0;

            var creature = DataCreaturesData.CreaturesList[monster.CreatureId];

            // Monsters trapped in rock must be given a turn also,
            // so that they will die/dig out immediately.
            if (monster.Lit || monster.DistanceFromPlayer <= creature.AreaAffectRadius ||
                ((creature.Movement & MonsterConfig.Move.CmPhase) == 0 && w.Dg.Floor[monster.Pos.Y, monster.Pos.X].FeatureId >= TileConstants.MinCaveWall))
            {
                if (monster.SleepCount > 0)
                {
                    if (py.Flags.Aggravate)
                    {
                        monster.SleepCount = 0;
                    }
                    else if ((py.Flags.Rest == 0 && py.Flags.Paralysis < 1) || RandomNumber.Next(50) == 1)
                    {
                        var notice = RandomNumber.Next(1024);

                        // The original's `1L << (29 - stealth_factor)` is a
                        // 32-bit long on the reference (MSVC) build: a
                        // stealth factor of -2 shifts into the sign bit, so
                        // nothing ever notices. C#'s int shift reproduces it.
                        if (notice * notice * notice <= (1 << (29 - py.Misc.StealthFactor)))
                        {
                            monster.SleepCount = (short)(monster.SleepCount - (100 / monster.DistanceFromPlayer));
                            if (monster.SleepCount > 0)
                            {
                                ignore = true;
                            }
                            else
                            {
                                wake = true;

                                // force it to be exactly zero
                                monster.SleepCount = 0;
                            }
                        }
                    }
                }

                if (monster.StunnedAmount != 0)
                {
                    // NOTE: Balrog = 100*100 = 10000, it always recovers instantly
                    if (RandomNumber.Next(5000) < creature.Level * creature.Level)
                    {
                        monster.StunnedAmount = 0;
                    }
                    else
                    {
                        monster.StunnedAmount--;
                    }

                    if (monster.StunnedAmount == 0)
                    {
                        if (monster.Lit)
                        {
                            PlayerCore.PrintMessage(w, $"The {creature.Name} recovers and glares at you.");
                        }
                    }
                }
                if (monster.SleepCount == 0 && monster.StunnedAmount == 0)
                {
                    Move(w, monsterId, ref rcmove);
                }
            }

            UpdateVisibility(w, monsterId);
            UpdateRecall(w, ref monster, wake, ignore, rcmove);
        }
    }

    // src/monster.cpp:1313 updateMonsters() -- creatures movement and
    // attacking are done from here -RAK-
    public static void UpdateMonsters(PlayerWorld w, bool attack)
    {
        var monsters = w.Monsters;

        // Process the monsters
        for (var id = monsters.NextFreeMonsterId - 1; id >= MonsterConfig.MonMinIndexId && !w.Game.CharacterIsDead; id--)
        {
            ref var monster = ref monsters.Monsters[id];

            // Get rid of an eaten/breathed on monster.  Note: Be sure not to
            // process this monster. This is necessary because we can't delete
            // monsters while scanning the monsters here.
            if (monster.Hp < 0)
            {
                monsters.DeleteMonsterRecord(id);
                continue;
            }

            monster.DistanceFromPlayer = (byte)CoordHelpers.DistanceBetween(w.Py.Pos, monster.Pos);

            // Attack is argument passed to CREATURE
            if (attack)
            {
                var moves = MovementRate(w, monster.Speed);

                if (moves <= 0)
                {
                    UpdateVisibility(w, id);
                }
                else
                {
                    AttackingUpdate(w, ref monster, id, moves);
                }
            }
            else
            {
                UpdateVisibility(w, id);
            }

            // Get rid of an eaten/breathed on monster. This is necessary because
            // we can't delete monsters while scanning the monsters here.
            // This monster may have been killed during monsterMove().
            if (monster.Hp < 0)
            {
                monsters.DeleteMonsterRecord(id);
            }
        }
    }
}
