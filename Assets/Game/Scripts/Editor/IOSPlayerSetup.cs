using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Development iOS player settings for running the prototype on a device: bundle id, display name, automatic
    /// signing with the development team, landscape-only orientation. Minimum OS / target devices are left as they
    /// are (not decided yet). Batch: -batchmode -quit -executeMethod Deadlock.Editor.IOSPlayerSetup.Apply
    /// </summary>
    public static class IOSPlayerSetup
    {
        public const string BundleIdentifier = "com.satoshihashimoto52.deadlock1988";
        public const string DisplayName = "DEADLOCK: Since 1988";
        /// <summary>Personal development team already used for this user's other Unity iOS project.</summary>
        public const string DevelopmentTeam = "HZ43AT7U4F";

        [MenuItem("DEADLOCK/Build/Apply iOS Player Settings")]
        public static void Apply()
        {
            // Edited through the serialized asset: the static PlayerSettings setters are not persisted in batch mode.
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0];
            var so = new SerializedObject(asset);
            so.FindProperty("productName").stringValue = DisplayName;
            SetMapValue(so.FindProperty("applicationIdentifier"), "iPhone", BundleIdentifier);
            so.FindProperty("appleDeveloperTeamID").stringValue = DevelopmentTeam;
            so.FindProperty("appleEnableAutomaticSigning").intValue = 1;
            // Landscape left/right only (D-025).
            so.FindProperty("defaultScreenOrientation").intValue = (int)UIOrientation.AutoRotation;
            so.FindProperty("allowedAutorotateToPortrait").boolValue = false;
            so.FindProperty("allowedAutorotateToPortraitUpsideDown").boolValue = false;
            so.FindProperty("allowedAutorotateToLandscapeLeft").boolValue = true;
            so.FindProperty("allowedAutorotateToLandscapeRight").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(asset);
            Debug.Log($"[DEADLOCK] iOS player settings: {BundleIdentifier}, '{DisplayName}', team {DevelopmentTeam}, automatic signing.");
        }

        static void SetMapValue(SerializedProperty map, string key, string value)
        {
            for (int i = 0; i < map.arraySize; i++)
            {
                var entry = map.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == key)
                {
                    entry.FindPropertyRelative("second").stringValue = value;
                    return;
                }
            }
            map.InsertArrayElementAtIndex(map.arraySize);
            var added = map.GetArrayElementAtIndex(map.arraySize - 1);
            added.FindPropertyRelative("first").stringValue = key;
            added.FindPropertyRelative("second").stringValue = value;
        }
    }
}
