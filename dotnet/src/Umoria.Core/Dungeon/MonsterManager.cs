using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dice;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Dungeon;

// Port of the monster-placement subset of src/monster_manager.cpp: popm(),
// monsterPlaceNew(), monsterGetOneSuitableForLevel(), and
// monsterPlaceNewWithinDistance() -- confirmed self-contained from
// monster.cpp's AI/combat code. Scoped down per the migration plan's M2:
// monsterPlaceWinning() and dungeonPlaceVaultMonster() are both only
// reachable via the still-excluded vault/unusual-room branch, so they're
// deferred along with it.
//
// The original reads the player's position/speed via the global `py`
// (Player_t); since no Player singleton is wired up yet (M1 only defined
// the data shape), callers pass both in explicitly instead.
public sealed class MonsterManager
{
    // monsters[] index 0 = "no monster", 1 reserved for the player -- real
    // monster slots start at MonsterConfig.MonMinIndexId (2). See
    // src/monster.h's MON_MIN_INDEX_ID comment.
    public Monster[] Monsters { get; } = new Monster[MonsterConstants.MonTotalAllocations];
    public int NextFreeMonsterId { get; private set; } = MonsterConfig.MonMinIndexId;

    private readonly Model.Generated.Dungeon _dg;

    // src/game_run.cpp:initializeMonsterLevels() -- cumulative histogram of
    // creature count by level, used to pick a random monster of (about) a
    // given level. Deterministic (built from the static CreaturesList
    // table, no RNG draws); the real game computes this once in
    // startMoria() and shares it across levels, but it's cheap enough to
    // rebuild per MonsterManager instance (one per level, same as
    // TreasureList) without needing a shared/static cache.
    private readonly int[] _monsterLevels;

    public MonsterManager(Model.Generated.Dungeon dg)
    {
        _dg = dg;
        _monsterLevels = BuildMonsterLevels();
    }

    private static int[] BuildMonsterLevels()
    {
        var levels = new int[MonsterConstants.MonMaxLevels + 1];

        var creatures = DataCreaturesData.CreaturesList;
        for (var i = 0; i < MonsterConstants.MonMaxCreatures - MonsterConfig.MonEndgameMonsters; i++)
        {
            levels[creatures[i].Level]++;
        }

        for (var i = 1; i <= MonsterConstants.MonMaxLevels; i++)
        {
            levels[i] += levels[i - 1];
        }

        return levels;
    }

    // src/monster_manager.cpp:21 popm()
    private int Popm()
    {
        if (NextFreeMonsterId == MonsterConstants.MonTotalAllocations)
        {
            // compactMonsters() (src/monster_manager.cpp:250) is unreachable
            // here: even the busiest level-1 seed places ~24 of 125 slots.
            // It also depends on dungeonDeleteMonster()/
            // dungeonRemoveMonsterFromLevel() (src/dungeon.cpp), deferred to
            // M5 -- see migration-plan.md. Throwing (rather than silently
            // returning failure, which the original treats as a soft error)
            // makes it loud if a future milestone's monster count ever gets
            // this high before M5 lands.
            throw new NotImplementedException(
                "compactMonsters() is not ported yet (deferred to M5); monster allocation should never reach capacity before then.");
        }

        return NextFreeMonsterId++;
    }

    // src/monster_manager.cpp:31 monsterPlaceNew()
    public bool PlaceNew(Coord coord, int creatureId, bool sleeping, Coord playerPos, int playerSpeedFlag)
    {
        var monsterId = Popm();

        var creature = DataCreaturesData.CreaturesList[creatureId];

        var hp = (short)((creature.Defenses & MonsterConfig.Defense.CdMaxHp) != 0
            ? DiceRoller.MaxRoll(creature.HitDie)
            : DiceRoller.Roll(creature.HitDie));

        // the CreaturesList speed value is 10 greater, so that it can be a byte
        var speed = (short)(creature.Speed - 10 + playerSpeedFlag);
        var distanceFromPlayer = (byte)CoordHelpers.DistanceBetween(playerPos, coord);

        short sleepCount;
        if (sleeping)
        {
            sleepCount = creature.SleepCounter == 0
                ? (short)0
                : (short)((creature.SleepCounter * 2) + RandomNumber.Next(creature.SleepCounter * 10));
        }
        else
        {
            sleepCount = 0;
        }

        Monsters[monsterId] = new Monster(hp, sleepCount, speed, (ushort)creatureId, coord, distanceFromPlayer, false, 0, 0);

        _dg.Floor[coord.Y, coord.X].CreatureId = (byte)monsterId;

        return true;
    }

    // src/monster_manager.cpp:129 monsterGetOneSuitableForLevel()
    private int GetOneSuitableForLevel(int level)
    {
        if (level == 0)
        {
            return RandomNumber.Next(_monsterLevels[0]) - 1;
        }

        if (level > MonsterConstants.MonMaxLevels)
        {
            level = MonsterConstants.MonMaxLevels;
        }

        if (RandomNumber.Next(MonsterConfig.MonChanceOfNasty) == 1)
        {
            var absDistribution = Math.Abs(RandomNumber.NextNormal(0, 4));
            level += absDistribution + 1;
            if (level > MonsterConstants.MonMaxLevels)
            {
                level = MonsterConstants.MonMaxLevels;
            }
        }
        else
        {
            // This distribution makes a level n monster occur approx 2/n%
            // of the time on level n, and 1/n*n% are 1st level.
            var num = _monsterLevels[level] - _monsterLevels[0];
            var i = RandomNumber.Next(num) - 1;
            var j = RandomNumber.Next(num) - 1;
            if (j > i)
            {
                i = j;
            }

            level = DataCreaturesData.CreaturesList[i + _monsterLevels[0]].Level;
        }

        return RandomNumber.Next(_monsterLevels[level] - _monsterLevels[level - 1]) - 1 + _monsterLevels[level - 1];
    }

    // src/monster_manager.cpp:163 monsterPlaceNewWithinDistance()
    public void PlaceNewWithinDistance(int number, int distanceFromSource, bool sleeping, Coord playerPos, int playerSpeedFlag)
    {
        for (var i = 0; i < number; i++)
        {
            Coord position;
            do
            {
                position = new Coord(RandomNumber.Next(_dg.Height - 2), RandomNumber.Next(_dg.Width - 2));
            } while (_dg.Floor[position.Y, position.X].FeatureId >= TileConstants.MinClosedSpace ||
                     _dg.Floor[position.Y, position.X].CreatureId != 0 ||
                     CoordHelpers.DistanceBetween(position, playerPos) <= distanceFromSource);

            var l = GetOneSuitableForLevel(_dg.CurrentLevel);

            // Dragons are always created sleeping here, so as to give the
            // player a sporting chance.
            var creatureSleeping = sleeping;
            var sprite = DataCreaturesData.CreaturesList[l].Sprite;
            if (sprite == (byte)'d' || sprite == (byte)'D')
            {
                creatureSleeping = true;
            }

            // PlaceNew() should always return true here. It does not matter
            // if it fails though.
            _ = PlaceNew(position, l, creatureSleeping, playerPos, playerSpeedFlag);
        }
    }
}
