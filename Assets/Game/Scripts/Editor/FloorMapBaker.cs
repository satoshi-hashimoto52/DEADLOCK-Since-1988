using Deadlock.Map;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Bakes a <see cref="FloorMapData"/> from the colliders of the scene being built (physics queries, no image to draw
    /// by hand): a builder calls it right after creating the level geometry, so the map always matches the level.
    /// Per cell (column at its centre):
    /// - a sloped surface anywhere in the column (a stair ramp, normal 0.5..0.97 up) = stairs on every floor;
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
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float wx = area.xMin + (x + 0.5f) * cellSize, wz = area.yMin + (y + 0.5f) * cellSize;
                    bool stairs = false;
                    int n = Physics.RaycastNonAlloc(new Vector3(wx, topY, wz), Vector3.down, hits, topY - bottom, solidMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                        if (hits[i].normal.y > 0.5f && hits[i].normal.y < 0.97f)
                            stairs = true;
                    for (int f = 0; f < floorHeights.Length; f++)
                    {
                        float baseY = floorHeights[f];
                        MapCell cell;
                        if (stairs)
                            cell = MapCell.Stairs;
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
    }
}
