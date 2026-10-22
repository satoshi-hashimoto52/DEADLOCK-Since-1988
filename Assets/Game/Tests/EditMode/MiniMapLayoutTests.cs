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
            }
        }
    }
}
