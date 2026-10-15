using Umoria.Core.Config;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Dungeon;

// Port of src/dungeon_generate.cpp's dungeonGenerate() and the helpers it
// needs, scoped down from the original for this proof-of-concept:
//
//   INCLUDED (faithfully ported, same RNG call sequence as the C++):
//     - plain rectangular rooms (dungeonBuildRoom) and the "overlapping
//       rectangles" unusual room type (dungeonBuildRoomOverlappingRectangles)
//     - tunnel building between rooms, including the doors it places inline
//       at tunnel/room junctions (dungeonBuildTunnel)
//     - intersection doors after tunneling (dungeonPlaceDoorIfNextToTwoWalls)
//     - granite wall fill + magma/quartz streamers + boundary walls
//     - up/down staircase placement
//     - picking the player's starting spot
//
//   DEFERRED (not ported -- see the migration plan's full-migration phase):
//     - the two vault-containing unusual room types (inner rooms, cross
//       shaped rooms) -- both can spawn monsters/traps inside vaults via
//       monsterSummon()/dungeonSetTrap(), which pulls in the monster and
//       trap subsystems. When the real C++ would pick one of these two
//       room types, this port substitutes the overlapping-rectangles
//       builder instead, so the level stays fully connected and walkable,
//       but output will NOT match the original byte-for-byte for seeds
//       that hit this case.
//     - monsterPlaceWinning() (only reachable past the still-excluded vault
//       branch's level range; see MonsterManager for the rest of monster
//       placement, which IS ported)
//     - general object/treasure allocation (dungeonAllocateAndPlaceObject)
//     - rubble and traps (no vaults => dungeonPlaceVaultTrap is unreachable)
//
// Because the original interleaves ALL of the above into one shared RNG
// stream, this port is NOT a byte-identical golden-trace match against the
// original C++ for arbitrary seeds -- only a structurally faithful port of
// the subset above. True byte-for-byte fidelity would require porting the
// deferred subsystems too (see MEMORY/plan retrospective).
public sealed class DungeonGenerator
{
    private readonly Model.Generated.Dungeon _dg;
    private readonly TreasureList _treasure;
    private readonly MonsterManager _monsters;

    // src/dungeon_generate.cpp: static Coord_t doors_tk[100]; static int door_index;
    private readonly Coord[] _doorsFound = new Coord[100];
    private int _doorIndex;

    public DungeonGenerator(Model.Generated.Dungeon dg, TreasureList treasure, MonsterManager monsters)
    {
        _dg = dg;
        _treasure = treasure;
        _monsters = monsters;
    }

    // Port of generateCave()'s dungeon-level branch (current_level >= 1).
    // Town generation (current_level == 0) is out of scope for this POC.
    public Coord Generate(int level)
    {
        _dg.CurrentLevel = level;
        _dg.Height = DungeonConstants.MaxHeight;
        _dg.Width = DungeonConstants.MaxWidth;
        _doorIndex = 0;

        BlankEntireCave();

        return BuildDungeonLevel();
    }

    // src/dungeon_generate.cpp:62 dungeonBlankEntireCave()
    private void BlankEntireCave()
    {
        Array.Clear(_dg.Floor, 0, _dg.Floor.Length);
    }

    // src/dungeon_generate.cpp:13 dungeonFloorTileForLevel()
    private byte FloorTileForLevel()
    {
        return _dg.CurrentLevel <= RandomNumber.Next(25) ? TileConstants.TileLightFloor : TileConstants.TileDarkFloor;
    }

    // src/dungeon_generate.cpp:995 dungeonGenerate(), scoped per the class comment above.
    private Coord BuildDungeonLevel()
    {
        var rowRooms = 2 * (_dg.Height / DungeonConstants.ScreenHeight);
        var colRooms = 2 * (_dg.Width / DungeonConstants.ScreenWidth);

        var roomMap = new bool[20, 20];

        var randomRoomCount = RandomNumber.NextNormal(DungeonGenerationConfig.DunRoomsMean, 2);
        for (var i = 0; i < randomRoomCount; i++)
        {
            roomMap[RandomNumber.Next(rowRooms) - 1, RandomNumber.Next(colRooms) - 1] = true;
        }

        var locationId = 0;
        var locations = new Coord[401]; // +1: dungeonGenerate() appends a wraparound entry

        for (var row = 0; row < rowRooms; row++)
        {
            for (var col = 0; col < colRooms; col++)
            {
                if (!roomMap[row, col])
                {
                    continue;
                }

                var coord = new Coord(
                    (row * (DungeonConstants.ScreenHeight >> 1)) + DungeonConstants.QuartHeight,
                    (col * (DungeonConstants.ScreenWidth >> 1)) + DungeonConstants.QuartWidth);
                locations[locationId] = coord;

                if (_dg.CurrentLevel > RandomNumber.Next(DungeonGenerationConfig.DunUnusualRooms))
                {
                    // Consume the same roomType draw the original makes, to
                    // keep subsequent RNG state aligned with its control
                    // flow as closely as this scoped port can. room_type 2
                    // and 3 are the vault-containing unusual rooms (deferred
                    // -- see class comment); all non-plain types are
                    // approximated here with the overlapping-rectangles
                    // builder, since it's the only unusual-room builder
                    // this port implements.
                    _ = RandomNumber.Next(3);
                    BuildRoomOverlappingRectangles(coord);
                }
                else
                {
                    BuildRoom(coord);
                }

                locationId++;
            }
        }

        for (var i = 0; i < locationId; i++)
        {
            var pick1 = RandomNumber.Next(locationId) - 1;
            var pick2 = RandomNumber.Next(locationId) - 1;

            (locations[pick1], locations[pick2]) = (locations[pick2], locations[pick1]);
        }

        _doorIndex = 0;

        // move zero entry to locationId, so that we can call BuildTunnel locationId times
        locations[locationId] = locations[0];

        for (var i = 0; i < locationId; i++)
        {
            BuildTunnel(locations[i + 1], locations[i]);
        }

        FillEmptyTilesWith(TileConstants.TileGraniteWall);
        for (var i = 0; i < DungeonGenerationConfig.DunMagmaStreamer; i++)
        {
            PlaceStreamerRock(TileConstants.TileMagmaWall, DungeonGenerationConfig.DunMagmaTreasure);
        }

        for (var i = 0; i < DungeonGenerationConfig.DunQuartzStreamer; i++)
        {
            PlaceStreamerRock(TileConstants.TileQuartzWall, DungeonGenerationConfig.DunQuartzTreasure);
        }

        PlaceBoundaryWalls();

        for (var i = 0; i < _doorIndex; i++)
        {
            PlaceDoorIfNextToTwoWalls(new Coord(_doorsFound[i].Y, _doorsFound[i].X - 1));
            PlaceDoorIfNextToTwoWalls(new Coord(_doorsFound[i].Y, _doorsFound[i].X + 1));
            PlaceDoorIfNextToTwoWalls(new Coord(_doorsFound[i].Y - 1, _doorsFound[i].X));
            PlaceDoorIfNextToTwoWalls(new Coord(_doorsFound[i].Y + 1, _doorsFound[i].X));
        }

        PlaceStairs(2, RandomNumber.Next(2) + 2, 3);
        PlaceStairs(1, RandomNumber.Next(2), 3);

        // Set up the character's starting coords (dungeonNewSpot).
        var playerStart = NewSpot();

        var allocLevel = _dg.CurrentLevel / 3;
        if (allocLevel < 2)
        {
            allocLevel = 2;
        }
        else if (allocLevel > 10)
        {
            allocLevel = 10;
        }

        // playerSpeedFlag: 0, matching py.flags.speed's zero-initialized
        // value -- no Player singleton exists yet (M1 only modeled the
        // shape; see MonsterManager's class comment) and nothing in this
        // scoped generator can have changed it from its startup default.
        _monsters.PlaceNewWithinDistance(RandomNumber.Next(8) + MonsterConfig.MonMinPerLevel + allocLevel, 0, true, playerStart, 0);

        // The original also seeds dungeonAllocateAndPlaceObject/
        // monsterPlaceWinning from here; those are deferred (see class
        // comment).
        return playerStart;
    }

    // src/dungeon_generate.cpp:331 dungeonBuildRoom()
    private void BuildRoom(Coord coord)
    {
        var floor = FloorTileForLevel();

        var height = coord.Y - RandomNumber.Next(4);
        var depth = coord.Y + RandomNumber.Next(3);
        var left = coord.X - RandomNumber.Next(11);
        var right = coord.X + RandomNumber.Next(11);

        for (var y = height; y <= depth; y++)
        {
            for (var x = left; x <= right; x++)
            {
                _dg.Floor[y, x].FeatureId = floor;
                _dg.Floor[y, x].PermaLitRoom = true;
            }
        }

        for (var y = height - 1; y <= depth + 1; y++)
        {
            _dg.Floor[y, left - 1].FeatureId = TileConstants.TileGraniteWall;
            _dg.Floor[y, left - 1].PermaLitRoom = true;

            _dg.Floor[y, right + 1].FeatureId = TileConstants.TileGraniteWall;
            _dg.Floor[y, right + 1].PermaLitRoom = true;
        }

        for (var x = left; x <= right; x++)
        {
            _dg.Floor[height - 1, x].FeatureId = TileConstants.TileGraniteWall;
            _dg.Floor[height - 1, x].PermaLitRoom = true;

            _dg.Floor[depth + 1, x].FeatureId = TileConstants.TileGraniteWall;
            _dg.Floor[depth + 1, x].PermaLitRoom = true;
        }
    }

    // src/dungeon_generate.cpp:370 dungeonBuildRoomOverlappingRectangles()
    private void BuildRoomOverlappingRectangles(Coord coord)
    {
        var floor = FloorTileForLevel();

        var limit = 1 + RandomNumber.Next(2);

        for (var count = 0; count < limit; count++)
        {
            var height = coord.Y - RandomNumber.Next(4);
            var depth = coord.Y + RandomNumber.Next(3);
            var left = coord.X - RandomNumber.Next(11);
            var right = coord.X + RandomNumber.Next(11);

            for (var y = height; y <= depth; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    _dg.Floor[y, x].FeatureId = floor;
                    _dg.Floor[y, x].PermaLitRoom = true;
                }
            }

            for (var y = height - 1; y <= depth + 1; y++)
            {
                if (_dg.Floor[y, left - 1].FeatureId != floor)
                {
                    _dg.Floor[y, left - 1].FeatureId = TileConstants.TileGraniteWall;
                    _dg.Floor[y, left - 1].PermaLitRoom = true;
                }

                if (_dg.Floor[y, right + 1].FeatureId != floor)
                {
                    _dg.Floor[y, right + 1].FeatureId = TileConstants.TileGraniteWall;
                    _dg.Floor[y, right + 1].PermaLitRoom = true;
                }
            }

            for (var x = left; x <= right; x++)
            {
                if (_dg.Floor[height - 1, x].FeatureId != floor)
                {
                    _dg.Floor[height - 1, x].FeatureId = TileConstants.TileGraniteWall;
                    _dg.Floor[height - 1, x].PermaLitRoom = true;
                }

                if (_dg.Floor[depth + 1, x].FeatureId != floor)
                {
                    _dg.Floor[depth + 1, x].FeatureId = TileConstants.TileGraniteWall;
                    _dg.Floor[depth + 1, x].PermaLitRoom = true;
                }
            }
        }
    }

    // src/dungeon_generate.cpp:22 pickCorrectDirection()
    private static void PickCorrectDirection(out int vertical, out int horizontal, Coord start, Coord end)
    {
        vertical = start.Y < end.Y ? 1 : start.Y == end.Y ? 0 : -1;
        horizontal = start.X < end.X ? 1 : start.X == end.X ? 0 : -1;

        if (vertical != 0 && horizontal != 0)
        {
            if (RandomNumber.Next(2) == 1)
            {
                vertical = 0;
            }
            else
            {
                horizontal = 0;
            }
        }
    }

    // src/dungeon_generate.cpp:49 chanceOfRandomDirection()
    private static void ChanceOfRandomDirection(out int vertical, out int horizontal)
    {
        var direction = RandomNumber.Next(4);

        if (direction < 3)
        {
            horizontal = 0;
            vertical = -3 + (direction << 1);
        }
        else
        {
            vertical = 0;
            horizontal = -7 + (direction << 1);
        }
    }

    // src/dungeon_generate.cpp:811 dungeonBuildTunnel()
    private void BuildTunnel(Coord start, Coord end)
    {
        var tunnels = new Coord[1000];
        var walls = new Coord[1000];

        var doorFlag = false;
        var stopFlag = false;
        var mainLoopCount = 0;
        var startRow = start.Y;
        var startCol = start.X;
        var tunnelIndex = 0;
        var wallIndex = 0;

        PickCorrectDirection(out var yDir, out var xDir, start, end);

        do
        {
            mainLoopCount++;
            if (mainLoopCount > 2000)
            {
                stopFlag = true;
            }

            if (RandomNumber.Next(100) > DungeonGenerationConfig.DunDirChange)
            {
                if (RandomNumber.Next(DungeonGenerationConfig.DunRandomDir) == 1)
                {
                    ChanceOfRandomDirection(out yDir, out xDir);
                }
                else
                {
                    PickCorrectDirection(out yDir, out xDir, start, end);
                }
            }

            var tmpRow = start.Y + yDir;
            var tmpCol = start.X + xDir;

            while (!CoordHelpers.InBounds(_dg, new Coord(tmpRow, tmpCol)))
            {
                if (RandomNumber.Next(DungeonGenerationConfig.DunRandomDir) == 1)
                {
                    ChanceOfRandomDirection(out yDir, out xDir);
                }
                else
                {
                    PickCorrectDirection(out yDir, out xDir, start, end);
                }

                tmpRow = start.Y + yDir;
                tmpCol = start.X + xDir;
            }

            var featureId = _dg.Floor[tmpRow, tmpCol].FeatureId;

            if (featureId == TileConstants.TileNullWall)
            {
                start = new Coord(tmpRow, tmpCol);
                if (tunnelIndex < 1000)
                {
                    tunnels[tunnelIndex] = start;
                    tunnelIndex++;
                }

                doorFlag = false;
            }
            else if (featureId == TileConstants.Tmp2Wall)
            {
                // do nothing
            }
            else if (featureId == TileConstants.TileGraniteWall)
            {
                start = new Coord(tmpRow, tmpCol);

                if (wallIndex < 1000)
                {
                    walls[wallIndex] = start;
                    wallIndex++;
                }

                for (var y = start.Y - 1; y <= start.Y + 1; y++)
                {
                    for (var x = start.X - 1; x <= start.X + 1; x++)
                    {
                        var c = new Coord(y, x);
                        if (CoordHelpers.InBounds(_dg, c) && _dg.Floor[y, x].FeatureId == TileConstants.TileGraniteWall)
                        {
                            _dg.Floor[y, x].FeatureId = TileConstants.Tmp2Wall;
                        }
                    }
                }
            }
            else if (featureId == TileConstants.TileCorrFloor || featureId == TileConstants.TileBlockedFloor)
            {
                start = new Coord(tmpRow, tmpCol);

                if (!doorFlag)
                {
                    if (_doorIndex < 100)
                    {
                        _doorsFound[_doorIndex] = start;
                        _doorIndex++;
                    }

                    doorFlag = true;
                }

                if (RandomNumber.Next(100) > DungeonGenerationConfig.DunTunneling)
                {
                    var dRow = Math.Abs(start.Y - startRow);
                    var dCol = Math.Abs(start.X - startCol);

                    if (dRow > 10 || dCol > 10)
                    {
                        stopFlag = true;
                    }
                }
            }
            else
            {
                start = new Coord(tmpRow, tmpCol);
            }
        } while ((start.Y != end.Y || start.X != end.X) && !stopFlag);

        for (var i = 0; i < tunnelIndex; i++)
        {
            _dg.Floor[tunnels[i].Y, tunnels[i].X].FeatureId = TileConstants.TileCorrFloor;
        }

        for (var i = 0; i < wallIndex; i++)
        {
            ref var tile = ref _dg.Floor[walls[i].Y, walls[i].X];

            if (tile.FeatureId != TileConstants.Tmp2Wall)
            {
                continue;
            }

            if (RandomNumber.Next(100) < DungeonGenerationConfig.DunRoomDoors)
            {
                PlaceDoor(walls[i]);
            }
            else
            {
                tile.FeatureId = TileConstants.TileCorrFloor;
            }
        }
    }

    // src/dungeon_generate.cpp:948 dungeonIsNextTo()
    private bool IsNextTo(Coord coord)
    {
        if (CoordHelpers.CorridorWallsNextTo(_dg, _treasure, coord) <= 2)
        {
            return false;
        }

        var vertical = _dg.Floor[coord.Y - 1, coord.X].FeatureId >= TileConstants.MinCaveWall &&
                       _dg.Floor[coord.Y + 1, coord.X].FeatureId >= TileConstants.MinCaveWall;
        var horizontal = _dg.Floor[coord.Y, coord.X - 1].FeatureId >= TileConstants.MinCaveWall &&
                          _dg.Floor[coord.Y, coord.X + 1].FeatureId >= TileConstants.MinCaveWall;

        return vertical || horizontal;
    }

    // src/dungeon_generate.cpp:960 dungeonPlaceDoorIfNextToTwoWalls()
    private void PlaceDoorIfNextToTwoWalls(Coord coord)
    {
        if (_dg.Floor[coord.Y, coord.X].FeatureId == TileConstants.TileCorrFloor &&
            RandomNumber.Next(100) > DungeonGenerationConfig.DunTunnelDoors &&
            IsNextTo(coord))
        {
            PlaceDoor(coord);
        }
    }

    // src/dungeon_generate.cpp:967 dungeonNewSpot()
    private Coord NewSpot()
    {
        Coord position;

        do
        {
            position = new Coord(RandomNumber.Next(_dg.Height - 2), RandomNumber.Next(_dg.Width - 2));
        } while (_dg.Floor[position.Y, position.X].FeatureId >= TileConstants.MinClosedSpace ||
                 _dg.Floor[position.Y, position.X].CreatureId != 0 ||
                 _dg.Floor[position.Y, position.X].TreasureId != 0);

        return position;
    }

    // src/dungeon_generate.cpp:68 dungeonFillEmptyTilesWith()
    private void FillEmptyTilesWith(byte rockType)
    {
        for (var y = _dg.Height - 2; y > 0; y--)
        {
            for (var x = _dg.Width - 2; x > 0; x--)
            {
                var featureId = _dg.Floor[y, x].FeatureId;
                if (featureId == TileConstants.TileNullWall || featureId == TileConstants.Tmp1Wall || featureId == TileConstants.Tmp2Wall)
                {
                    _dg.Floor[y, x].FeatureId = rockType;
                }
            }
        }
    }

    // src/dungeon_generate.cpp:87 dungeonPlaceBoundaryWalls()
    private void PlaceBoundaryWalls()
    {
        for (var i = 0; i < _dg.Height; i++)
        {
            _dg.Floor[i, 0].FeatureId = TileConstants.TileBoundaryWall;
            _dg.Floor[i, _dg.Width - 1].FeatureId = TileConstants.TileBoundaryWall;
        }

        for (var i = 0; i < _dg.Width; i++)
        {
            _dg.Floor[0, i].FeatureId = TileConstants.TileBoundaryWall;
            _dg.Floor[_dg.Height - 1, i].FeatureId = TileConstants.TileBoundaryWall;
        }
    }

    // src/dungeon_generate.cpp:126 dungeonPlaceStreamerRock()
    private void PlaceStreamerRock(byte rockType, int chanceOfTreasure)
    {
        var coord = new Coord(
            (_dg.Height / 2) + 11 - RandomNumber.Next(23),
            (_dg.Width / 2) + 16 - RandomNumber.Next(33));

        var dir = RandomNumber.Next(8);
        if (dir > 4)
        {
            dir += 1;
        }

        var t1 = (2 * DungeonGenerationConfig.DunStreamerWidth) + 1;
        var t2 = DungeonGenerationConfig.DunStreamerWidth + 1;

        do
        {
            for (var i = 0; i < DungeonGenerationConfig.DunStreamerDensity; i++)
            {
                var spot = new Coord(
                    coord.Y + RandomNumber.Next(t1) - t2,
                    coord.X + RandomNumber.Next(t1) - t2);

                if (CoordHelpers.InBounds(_dg, spot) && _dg.Floor[spot.Y, spot.X].FeatureId == TileConstants.TileGraniteWall)
                {
                    _dg.Floor[spot.Y, spot.X].FeatureId = rockType;

                    if (RandomNumber.Next(chanceOfTreasure) == 1)
                    {
                        PlaceGold(spot);
                    }
                }
            }
        } while (PlayerMovePosition(dir, ref coord));
    }

    // Port of src/player.cpp:49 playerMovePosition() -- the one piece of the
    // (deferred) player-movement subsystem dungeon generation itself needs,
    // for streamer-rock placement's walk-in-a-direction loop.
    private bool PlayerMovePosition(int dir, ref Coord coord)
    {
        var (deltaY, deltaX) = Movement.DirectionOffsets.For(dir);
        var newCoord = new Coord(coord.Y + deltaY, coord.X + deltaX);

        if (newCoord.Y >= 0 && newCoord.Y < _dg.Height && newCoord.X >= 0 && newCoord.X < _dg.Width)
        {
            coord = newCoord;
            return true;
        }

        return false;
    }

    // src/dungeon.cpp:254 dungeonPlaceGold() -- cost/value math is replaced
    // with placeholder RNG draws (see TreasureStub) since it never affects
    // control flow and the item/cost system itself is deferred.
    private void PlaceGold(Coord coord)
    {
        var id = _treasure.Allocate();
        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;

        _ = RandomNumber.Next(_dg.CurrentLevel + 2); // gold_type_id roll
        if (RandomNumber.Next(12) == 1) // config::treasure::TREASURE_CHANCE_OF_GREAT_ITEM
        {
            _ = RandomNumber.Next(_dg.CurrentLevel + 1);
        }

        _ = RandomNumber.Next(1); // placeholder for randomNumber((int)cost)
        _ = RandomNumber.Next(8);
    }

    // --- Door placement: src/dungeon_generate.cpp:163-230 ---

    private void PlaceOpenDoor(Coord coord)
    {
        var id = _treasure.Allocate();
        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;
        _treasure[id].CategoryId = TreasureCategory.TvOpenDoor;
        _dg.Floor[coord.Y, coord.X].FeatureId = TileConstants.TileCorrFloor;
    }

    private void PlaceBrokenDoor(Coord coord)
    {
        var id = _treasure.Allocate();
        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;
        _treasure[id].CategoryId = TreasureCategory.TvOpenDoor;
        _dg.Floor[coord.Y, coord.X].FeatureId = TileConstants.TileCorrFloor;
    }

    private void PlaceClosedDoor(Coord coord)
    {
        var id = _treasure.Allocate();
        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;
        _treasure[id].CategoryId = TreasureCategory.TvClosedDoor;
        _dg.Floor[coord.Y, coord.X].FeatureId = TileConstants.TileBlockedFloor;
    }

    private void PlaceLockedDoor(Coord coord)
    {
        var id = _treasure.Allocate();
        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;
        _treasure[id].CategoryId = TreasureCategory.TvClosedDoor;
        _dg.Floor[coord.Y, coord.X].FeatureId = TileConstants.TileBlockedFloor;
        _ = RandomNumber.Next(10); // misc_use (lock strength); field omitted, see TreasureStub
    }

    private void PlaceStuckDoor(Coord coord)
    {
        var id = _treasure.Allocate();
        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;
        _treasure[id].CategoryId = TreasureCategory.TvClosedDoor;
        _dg.Floor[coord.Y, coord.X].FeatureId = TileConstants.TileBlockedFloor;
        _ = RandomNumber.Next(10); // misc_use (stuck strength); field omitted
    }

    private void PlaceSecretDoor(Coord coord)
    {
        var id = _treasure.Allocate();
        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;
        _treasure[id].CategoryId = TreasureCategory.TvSecretDoor;
        _dg.Floor[coord.Y, coord.X].FeatureId = TileConstants.TileBlockedFloor;
    }

    private void PlaceDoor(Coord coord)
    {
        var doorType = RandomNumber.Next(3);

        if (doorType == 1)
        {
            if (RandomNumber.Next(4) == 1)
            {
                PlaceBrokenDoor(coord);
            }
            else
            {
                PlaceOpenDoor(coord);
            }
        }
        else if (doorType == 2)
        {
            doorType = RandomNumber.Next(12);

            if (doorType > 3)
            {
                PlaceClosedDoor(coord);
            }
            else if (doorType == 3)
            {
                PlaceStuckDoor(coord);
            }
            else
            {
                PlaceLockedDoor(coord);
            }
        }
        else
        {
            PlaceSecretDoor(coord);
        }
    }

    // --- Stair placement: src/dungeon_generate.cpp:233-298 ---

    private void PlaceUpStairs(Coord coord)
    {
        // original: dungeonDeleteObject(coord) if treasure_id != 0 --
        // unreachable in this scoped generator (stairs are placed onto
        // tiles not yet carrying a treasure_id).
        var id = _treasure.Allocate();
        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;
        _treasure[id].CategoryId = TreasureCategory.TvUpStair;
    }

    private void PlaceDownStairs(Coord coord)
    {
        var id = _treasure.Allocate();
        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;
        _treasure[id].CategoryId = TreasureCategory.TvDownStair;
    }

    private void PlaceStairs(int stairType, int number, int walls)
    {
        for (var i = 0; i < number; i++)
        {
            var placed = false;

            while (!placed)
            {
                var j = 0;

                do
                {
                    var coord1 = new Coord(RandomNumber.Next(_dg.Height - 14), RandomNumber.Next(_dg.Width - 14));
                    var coord2 = new Coord(coord1.Y + 12, coord1.X + 12);

                    do
                    {
                        do
                        {
                            if (_dg.Floor[coord1.Y, coord1.X].FeatureId <= TileConstants.MaxOpenSpace &&
                                _dg.Floor[coord1.Y, coord1.X].TreasureId == 0 &&
                                CoordHelpers.WallsNextTo(_dg, coord1) >= walls)
                            {
                                placed = true;
                                if (stairType == 1)
                                {
                                    PlaceUpStairs(coord1);
                                }
                                else
                                {
                                    PlaceDownStairs(coord1);
                                }
                            }

                            coord1 = coord1 with { X = coord1.X + 1 };
                        } while (coord1.X != coord2.X && !placed);

                        coord1 = coord1 with { X = coord2.X - 12, Y = coord1.Y + 1 };
                    } while (coord1.Y != coord2.Y && !placed);

                    j++;
                } while (!placed && j <= 30);

                walls--;
            }
        }
    }
}
