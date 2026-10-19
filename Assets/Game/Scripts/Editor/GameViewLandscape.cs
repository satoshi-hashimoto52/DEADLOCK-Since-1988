using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Editor-only: adds landscape Game View presets (16:9 1920x1080, iPhone-like 2532x1170, iPad 4:3 2048x1536)
    /// and selects one. This is an Editor display setting (stored per user, not in git) and is unrelated to the
    /// device orientation, which is set in Player Settings (landscape left/right only).
    /// Unity has no public API for Game View sizes; this uses the internal one by reflection and logs if it is unavailable.
    /// </summary>
    public static class GameViewLandscape
    {
        static readonly (string name, int w, int h)[] Presets =
        {
            ("DEADLOCK 16:9 1920x1080", 1920, 1080),
            ("DEADLOCK iPhone 2532x1170", 2532, 1170),
            ("DEADLOCK iPad 4:3 2048x1536", 2048, 1536),
        };

        [MenuItem("DEADLOCK/Game View/16:9 1920x1080")]
        public static void Select16x9() => Select(0);

        [MenuItem("DEADLOCK/Game View/iPhone 2532x1170")]
        public static void SelectPhone() => Select(1);

        [MenuItem("DEADLOCK/Game View/iPad 4:3 2048x1536")]
        public static void SelectPad() => Select(2);

        /// <summary>For launching the Editor: -executeMethod Deadlock.Editor.GameViewLandscape.OpenSceneLandscape</summary>
        public static void OpenSceneLandscape()
        {
            // Wait until the Editor has finished starting before touching windows.
            EditorApplication.delayCall += () =>
            {
                PrototypeSceneBuilder.OpenScene();
                Select(0);
            };
        }

        static void Select(int preset)
        {
            try
            {
                Assembly editor = typeof(EditorWindow).Assembly;
                Type sizesType = editor.GetType("UnityEditor.GameViewSizes");
                Type sizeType = editor.GetType("UnityEditor.GameViewSize");
                Type sizeKindType = editor.GetType("UnityEditor.GameViewSizeType");
                Type gameViewType = editor.GetType("UnityEditor.GameView");

                object sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
                object group = sizesType.GetProperty("currentGroup").GetValue(sizes);
                Type groupType = group.GetType();
                MethodInfo getTexts = groupType.GetMethod("GetDisplayTexts");
                MethodInfo addCustom = groupType.GetMethod("AddCustomSize");

                foreach (var (name, w, h) in Presets)
                {
                    if (((string[])getTexts.Invoke(group, null)).Any(t => t.StartsWith(name)))
                        continue;
                    object fixedResolution = Enum.Parse(sizeKindType, "FixedResolution");
                    object size = Activator.CreateInstance(sizeType, fixedResolution, w, h, name);
                    addCustom.Invoke(group, new[] { size });
                }
                sizesType.GetMethod("SaveToHDD")?.Invoke(sizes, null);

                string[] texts = (string[])getTexts.Invoke(group, null);
                int index = Array.FindIndex(texts, t => t.StartsWith(Presets[preset].name));
                var window = EditorWindow.GetWindow(gameViewType);
                gameViewType.GetMethod("SizeSelectionCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(window, new object[] { index, null });
                Debug.Log($"[DEADLOCK] Game View set to '{Presets[preset].name}' (index {index}).");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DEADLOCK] Could not set the Game View size automatically ({e.GetType().Name}: {e.Message}). " +
                                 "Choose a landscape resolution from the Game View's aspect drop-down instead.");
            }
        }
    }
}
