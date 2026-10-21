// Hand-translated from src/recall.h's Recall_t (not tool-generated).
namespace Umoria.Core.Model.Generated;

// The player's knowledge of one creature type, aka monster memory (the
// global creature_recall[MON_MAX_CREATURES], indexed by creature id). Added
// in M9: monster AI and combat update it as a side effect of nearly every
// encounter, and monsterTakeHit() reads it back (the treasure-drop bits) to
// decide what to remember. Displaying it (recall.cpp) is out of the
// roadmap's scope; the state itself is not. A class, like Player, because
// every user mutates it in place.
public sealed class Recall
{
    public uint Movement;
    public uint Spells;
    public ushort Kills;
    public ushort Deaths;
    public ushort Defenses;
    public byte Wake;
    public byte Ignore;
    public byte[] Attacks = new byte[MonsterConstants.MonMaxAttacks];

    public static Recall[] CreateTable()
    {
        var table = new Recall[MonsterConstants.MonMaxCreatures];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = new Recall();
        }

        return table;
    }
}
