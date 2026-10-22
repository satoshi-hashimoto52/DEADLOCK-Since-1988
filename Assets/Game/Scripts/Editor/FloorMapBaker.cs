using Deadlock.Map;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Bakes a <see cref="FloorMapData"/> from the colliders of the scene being built (physics queries, no image to draw
    /// by hand): a builder calls it right after creating the level geometry, so the map always matches the level.
    /// Per cell (column at its centre):
    /// - a sloped surface anywhere in the column (a stair ramp, normal 0.5..0.97 up) = stairs on every floor, drawn per
    ///   floor: no line where the ramp meets this floor (the way on / off it), step lines across the rest of it, and a
    ///   boundary line across the end that leads to the other floor;
    /// - otherwise, per floor: something solid at body height (0.5..1.9 m above the floor) = wall (walls, pillars,
    ///   parapet, cover); a level surface within 0.6 m below the floor height = floor; nothing = void on upper floors
    ///   (a drop, e.g. the atrium) and outside on the ground.
    /// </summary>
    public static class FloorMapBaker
    {
        public static FloorMapData Bake(string assetPath, Rect area, float cellSize, float[] floorHeights, string[] labels, float topY, int solidMask)
        {
            Physics.SyncTransforms();
            int width = Mathf.CeilToInt(area.width / cellSize - 0.001f);
            int height = Mathf.CeilToInt(area.height / cellSize - 0.001f);
            var data = ScriptableObject.CreateInstance<FloorMapData>();
            data.origin = area.min;
            data.cellSize = cellSize;
            data.width = width;
            data.height = height;
            data.floors = new FloorMapData.Floor[floorHeights.Length];
            for (int f = 0; f < floorHeights.Length; f++)
                data.floors[f] = new FloorMapData.Floor { label = labels[f], baseY = floorHeights[f], cells = new byte[width * height] };

            var hits = new RaycastHit[32];
            var half = new Vector3(cellSize * 0.45f, 0.7f, cellSize * 0.45f);
            float bottom = floorHeights[0] - 0.5f;
            // Ramp surface height per cell (NaN = no ramp): the lowest sloped surface in the column with room to stand
            // on it (the stair tunnel's sloped ceiling, which overhangs the ramp's ends, is not a ramp).
            var ramp = new float[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float wx = area.xMin + (x + 0.5f) * cellSize, wz = area.yMin + (y + 0.5f) * cellSize;
                    float h = float.NaN;
                    int n = Physics.RaycastNonAlloc(new Vector3(wx, topY, wz), Vector3.down, hits, topY - bottom, solidMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                        if (hits[i].normal.y > 0.5f && hits[i].normal.y < 0.97f && !(hits[i].point.y >= h)
                            && !Physics.CheckCapsule(hits[i].point + Vector3.up * 0.4f, hits[i].point + Vector3.up * 1.5f, 0.1f, solidMask, QueryTriggerInteraction.Ignore))
                            h = hits[i].point.y;
                    ramp[y * width + x] = h;
                }
            float H(int x, int y) => x < 0 || y < 0 || x >= width || y >= height ? float.NaN : ramp[y * width + x];

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float wx = area.xMin + (x + 0.5f) * cellSize, wz = area.yMin + (y + 0.5f) * cellSize;
                    float h = H(x, y);
                    for (int f = 0; f < floorHeights.Length; f++)
                    {
                        float baseY = floorHeights[f];
                        MapCell cell;
                        if (!float.IsNaN(h))
                            cell = StairCell(H, x, y, h, baseY);
                        else if (Physics.CheckBox(new Vector3(wx, baseY + 1.2f, wz), half, Quaternion.identity, solidMask, QueryTriggerInteraction.Ignore))
                            cell = MapCell.Wall;
                        else if (Physics.Raycast(new Vector3(wx, baseY + 0.45f, wz), Vector3.down, out RaycastHit floor, 1.05f, solidMask, QueryTriggerInteraction.Ignore)
                                 && floor.normal.y >= 0.97f)
                            cell = MapCell.Floor;
                        else
                            cell = f == 0 ? MapCell.Outside : MapCell.Void;
                        data.floors[f].cells[y * width + x] = (byte)cell;
                    }
                }

            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(data, assetPath);
            return data;
        }

        const float StepRise = 0.45f;  // a step line every 0.45 m of rise (about every third cell on a 30 degree ramp)

        /// <summary>
        /// A ramp cell as drawn on the floor at baseY. The ramp's direction comes from the neighbouring ramp heights;
        /// the end that leads away from this floor (within two cells of the ramp's end) is the boundary; step lines are
        /// left out near this floor's own end, so the way on / off the stair has no line across it.
        /// </summary>
        static MapCell StairCell(System.Func<int, int, float> h, int x, int y, float here, float baseY)
        {
            float Slope(int dx, int dy)
            {
                float a = h(x + dx, y + dy), b = h(x - dx, y - dy);
                if (!float.IsNaN(a) && !float.IsNaN(b)) return (a - b) * 0.5f;
                if (!float.IsNaN(a)) return a - here;
                if (!float.IsNaN(b)) return here - b;
                return 0f;
            }
            float sx = Slope(1, 0), sy = Slope(0, 1);
            bool alongX = Mathf.Abs(sx) > Mathf.Abs(sy);
            float slope = alongX ? sx : sy;
            if (Mathf.Approximately(slope, 0f))
                return MapCell.Stairs;
            // Along the ramp: within two cells of either end. The end far from this floor's height leads to the other
            // floor (boundary); the end at this floor's height is the way on / off (no line).
            int ux = alongX ? (slope > 0f ? 1 : -1) : 0, uy = alongX ? 0 : (slope > 0f ? 1 : -1);
            bool nearEnd = false;
            foreach (int k in new[] { 1, 2, -1, -2 })
                nearEnd |= float.IsNaN(h(x + k * ux, y + k * uy));
            if (nearEnd && Mathf.Abs(here - baseY) > 1f)
                return MapCell.StairsEnd;
            float up = h(x + ux, y + uy);
            if (Mathf.Abs(here - baseY) > StepRise && !float.IsNaN(up) && Mathf.FloorToInt(up / StepRise) != Mathf.FloorToInt(here / StepRise))
                return MapCell.StairsStep;
            return MapCell.Stairs;
        }
    }
}
