using Deadlock.Input;
using Deadlock.UI;
using NUnit.Framework;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>
    /// The floor map (top-left of the Safe Area) stays clear of the menu button, the top-centre status lines, the ammo /
    /// posture text, the HP bar and every default touch control (and the move zone) on iPhone 16 Pro, 16:9 and 4:3.
    /// </summary>
    public class MiniMapLayoutTests
    {
        // Safe Areas in canvas units (reference height 1080): iPhone 16 Pro as in TouchLayoutTests, 16:9, iPad 4:3.
        static readonly (string name, Vector2 size)[] Screens =
        {
            ("iPhone 16 Pro", new Vector2(2140f, 1020f)),
            ("iPhone 16 Pro (186 / 63 px insets)", new Vector2(2348f - 2f * 186f * 1080f / 1206f, 1080f - 63f * 1080f / 1206f)),
            ("16:9", new Vector2(1920f, 1080f)), ("4:3", new Vector2(1440f, 1080f)),
        };

        static Rect TopRight(Vector2 safe, Vector2 offset, Vector2 size) => new Rect(safe.x + offset.x - size.x, safe.y + offset.y - size.y, size.x, size.y);
        static Rect TopCentre(Vector2 safe, float fromTop, Vector2 size) => new Rect(safe.x * 0.5f - size.x * 0.5f, safe.y - fromTop - size.y, size.x, size.y);

        [Test]
        public void MiniMap_ClearOfHudAndDefaultControls_OnPhone16x9And4x3()
        {
            foreach (var (name, safe) in Screens)
            {
                Rect map = MiniMapView.LayoutRect(safe);
                Assert.GreaterOrEqual(map.xMin, 0f, name);
                Assert.LessOrEqual(map.yMax, safe.y, name);
                var hud = new (string, Rect)[]
                {
                    ("menu button and its margin", TouchLayout.MenuGuard(safe)),
                    ("training status", TopCentre(safe, 84f, new Vector2(600f, 40f))),
                    ("frame-rate line (dev)", TopCentre(safe, 8f, new Vector2(700f, 40f))),
                    ("ammo", TopRight(safe, new Vector2(-48f, -36f), new Vector2(420f, 90f))),
                    ("posture", TopRight(safe, new Vector2(-48f, -126f), new Vector2(420f, 80f))),
                    ("HP bar", new Rect(safe.x * 0.5f - 180f, 24f, 360f, 54f)),
                };
                foreach (var (what, rect) in hud)
                    Assert.IsFalse(map.Overlaps(rect), $"{name}: map {map} overlaps the {what} {rect}");

                var layout = TouchLayout.Defaults();
                foreach (var item in layout.items)
                {
                    if (item.id == TouchLayout.MoveZone)
                    {
                        var zone = new Rect((item.center - item.size * 0.5f) * safe, item.size * safe);
                        Assert.IsFalse(map.Overlaps(zone), $"{name}: map over the move zone {zone}");
                        continue;
                    }
                    Vector2 c = item.center * safe;
                    float r = TouchLayout.Diameter(item) * 0.5f;
                    Vector2 nearest = new Vector2(Mathf.Clamp(c.x, map.xMin, map.xMax), Mathf.Clamp(c.y, map.yMin, map.yMax));
                    Assert.Greater(Vector2.Distance(nearest, c), r, $"{name}: map over {item.id}");
                }
                Assert.AreEqual(MiniMapView.Margin.x, MiniMapView.ChoosePosition(safe, layout), $"{name}: default layout keeps the corner");
                Assert.AreEqual(4f / 3f, map.width / map.height, 0.001f, $"{name}: 4:3, a little wider than tall");
            }
        }

        // A layout saved on the test iPhone (2026-10-22): the weapon button moved to the top-left corner, ADS to the top right.
        const string DeviceLayout = "{\"version\":1,\"items\":[{\"id\":\"move_zone\",\"center\":{\"x\":0.2,\"y\":0.4},\"size\":{\"x\":0.4,\"y\":0.8},\"scale\":1.0},"
            + "{\"id\":\"move_stick\",\"center\":{\"x\":0.1086,\"y\":0.2765},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.0},"
            + "{\"id\":\"fire\",\"center\":{\"x\":0.9226,\"y\":0.2209},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.3},"
            + "{\"id\":\"ads\",\"center\":{\"x\":0.8883,\"y\":0.8730},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.4},"
            + "{\"id\":\"reload\",\"center\":{\"x\":0.7430,\"y\":0.2970},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.0},"
            + "{\"id\":\"jump\",\"center\":{\"x\":0.7594,\"y\":0.0853},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":0.9},"
            + "{\"id\":\"posture\",\"center\":{\"x\":0.6762,\"y\":0.0900},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.0},"
            + "{\"id\":\"weapon\",\"center\":{\"x\":0.0359,\"y\":0.8548},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.1}]}";

        [Test]
        public void Placement_KeptInsideTheSafeArea_ScaleLimited_AspectKept()
        {
            foreach (var (name, safe) in Screens)
            {
                foreach (var (centre, scale) in new[] { (new Vector2(-1f, 2f), 1f), (new Vector2(2f, -1f), 3f), (new Vector2(0.5f, 0.5f), 0.1f) })
                {
                    var p = MiniMapView.Clamp(new Deadlock.Map.MiniMapPlacement { center = centre, scale = scale }, safe);
                    Rect r = MiniMapView.PlacementRect(p, safe);
                    Assert.That(p.scale, Is.InRange(MiniMapView.MinScale, MiniMapView.MaxScale), name);
                    Assert.GreaterOrEqual(r.xMin, -0.01f, name);
                    Assert.GreaterOrEqual(r.yMin, -0.01f, name);
                    Assert.LessOrEqual(r.xMax, safe.x + 0.01f, name);
                    Assert.LessOrEqual(r.yMax, safe.y + 0.01f, name);
                    Assert.AreEqual(4f / 3f, r.width / r.height, 0.001f, name);
                }
            }
        }

        [Test]
        public void MiniMap_MovesAlongTheTop_ClearOfAButtonPutInTheCorner()
        {
            var layout = JsonUtility.FromJson<TouchLayoutData>(DeviceLayout);
            foreach (var (name, safe) in Screens)
            {
                float left = MiniMapView.ChoosePosition(safe, layout);
                Assert.Greater(left, MiniMapView.Margin.x, $"{name}: moved out of the corner");
                var map = new Rect(left, safe.y - MiniMapView.Margin.y - MiniMapView.BaseSize.y, MiniMapView.BaseSize.x, MiniMapView.BaseSize.y);
                foreach (var item in layout.items)
                {
                    if (item.id == TouchLayout.MoveZone)
                        continue;
                    float d = TouchLayout.Diameter(item);
                    Vector2 c = TouchLayout.ClampCircle(item.center, d, safe) * safe;
                    Vector2 nearest = new Vector2(Mathf.Clamp(c.x, map.xMin, map.xMax), Mathf.Clamp(c.y, map.yMin, map.yMax));
                    Assert.Greater(Vector2.Distance(nearest, c), d * 0.5f, $"{name}: map over {item.id}");
                }
                Assert.IsFalse(map.Overlaps(TouchLayout.MenuGuard(safe)), $"{name}: menu button");
                Assert.LessOrEqual(map.xMax, safe.x * 0.5f - 300f, $"{name}: left of the status line");
            }
        }
    }
}
