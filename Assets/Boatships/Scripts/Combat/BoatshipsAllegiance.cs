using System;
using UnityEngine;

namespace Boatships
{
    /// <summary>Which side a ship fights on. The encounter director registers the lookup: the flagship and her company
    /// are side 0, hostile encounters side 1, anything else (practice targets, an AI employer) has no side (-1).
    /// Round shot passes harmlessly over ships of its own side, and gun crews treat anyone attacking an ally as an enemy.</summary>
    public static class BoatshipsAllegiance
    {
        public const int Player = 0, Hostile = 1, None = -1;

        public static Func<ShipCombatTarget, int> SideOf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => SideOf = null;

        public static int Side(ShipCombatTarget ship) => ship == null || SideOf == null ? None : SideOf(ship);

        public static bool Allied(ShipCombatTarget a, ShipCombatTarget b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            int side = Side(a);
            return side != None && side == Side(b);
        }
    }
}
