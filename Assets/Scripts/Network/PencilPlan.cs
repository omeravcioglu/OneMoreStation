using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>
    /// A line sketched in pencil (the Plan tool): costs nothing and runs nothing, saved with the city.
    /// Owned by the UI session's Plan tool; GameState keeps the list and SaveSystem persists it.
    /// </summary>
    [Serializable]
    public sealed class PencilPlan
    {
        public string name = "";
        public List<Vector2> points = new List<Vector2>();
        /// <summary>Per point: which way the 45° bend goes on the way to it (like PlannedStop.diagonalFirst).</summary>
        public List<bool> diagonalFirst = new List<bool>();

        public PencilPlan Copy() => new PencilPlan
        {
            name = name ?? "",
            points = new List<Vector2>(points ?? new List<Vector2>()),
            diagonalFirst = new List<bool>(diagonalFirst ?? new List<bool>())
        };
    }
}
