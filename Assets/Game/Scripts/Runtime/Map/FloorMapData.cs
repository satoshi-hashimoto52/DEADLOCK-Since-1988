using System;
using UnityEngine;

namespace Deadlock.Map
{
    public enum MapCell : byte
    {
        Outside,
        Floor,
        Wall,
        /// <summary>No floor under the walking height (atrium seen from the gallery): not walkable.</summary>
        Void,
        /// <summary>Stair ramp (shown on every floor it links).</summary>
        Stairs,
    }

    /// <summary>
    /// Floor map of a level: one grid of <see cref="MapCell"/> per floor, baked in the Editor from the level's colliders
    /// (the ruins builder bakes it right after building the geometry), so a changed corridor never needs a redrawn image.
    /// Grid x runs east (+x), grid y north (+z), from <see cref="origin"/> (world x, z of cell 0, 0's corner).
    /// </summary>
    public sealed class FloorMapData : ScriptableObject
    {
        [Serializable]
        public sealed class Floor
        {
            public string label;
            /// <summary>Walking surface height of the floor.</summary>
            public float baseY;
            /// <summary>width * height cells, row by row from the south-west corner.</summary>
            public byte[] cells;
        }

        public Vector2 origin;
        public float cellSize = 0.25f;
        public int width;
        public int height;
        public Floor[] floors = Array.Empty<Floor>();
        [Tooltip("The shown floor changes only this far (m) past the height between two floors (no flicker on the stairs).")]
        public float switchHysteresis = 0.75f;

        public MapCell Cell(int floor, int x, int y)
        {
            if (floor < 0 || floor >= floors.Length || x < 0 || y < 0 || x >= width || y >= height)
                return MapCell.Outside;
            return (MapCell)floors[floor].cells[y * width + x];
        }

        public MapCell CellAt(int floor, Vector3 world)
        {
            Vector2 c = ToCells(world);
            return Cell(floor, Mathf.FloorToInt(c.x), Mathf.FloorToInt(c.y));
        }

        /// <summary>World position to grid coordinates (cells, fractional).</summary>
        public Vector2 ToCells(Vector3 world) => new Vector2((world.x - origin.x) / cellSize, (world.z - origin.y) / cellSize);

        /// <summary>Height between floor i and i + 1.</summary>
        float Boundary(int i) => (floors[i].baseY + floors[i + 1].baseY) * 0.5f;

        /// <summary>The floor a height belongs to (no hysteresis): for enemies at the moment they are detected.</summary>
        public int NearestFloor(float y)
        {
            int f = 0;
            while (f + 1 < floors.Length && y > Boundary(f))
                f++;
            return f;
        }

        /// <summary>
        /// The floor to show for a height, starting from the floor shown now: it changes only once the height is
        /// <see cref="switchHysteresis"/> past the middle between the floors, so walking on the stairs (or jumping
        /// there) does not switch back and forth. current &lt; 0: no floor shown yet.
        /// </summary>
        public int FloorFor(float y, int current)
        {
            if (floors.Length == 0)
                return -1;
            if (current < 0 || current >= floors.Length)
                return NearestFloor(y);
            while (current + 1 < floors.Length && y > Boundary(current) + switchHysteresis)
                current++;
            while (current > 0 && y < Boundary(current - 1) - switchHysteresis)
                current--;
            return current;
        }
    }
}
