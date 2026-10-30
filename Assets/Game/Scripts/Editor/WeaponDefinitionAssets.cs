using System.Linq;
using Deadlock.Combat;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Writes every <see cref="WeaponDefinition"/> asset with all of its current fields. New optional fields (recoil
    /// profile, damage falloff) then sit in the files with their off / empty defaults, so a later save (iOS export,
    /// scene builders: AssetDatabase.SaveAssets) no longer adds them as an unexpected change. Values and GUIDs are not
    /// changed. Menu: DEADLOCK/Dev/Write Weapon Definition Fields.
    /// Batch: -batchmode -quit -executeMethod Deadlock.Editor.WeaponDefinitionAssets.WriteAllFields
    /// </summary>
    public static class WeaponDefinitionAssets
    {
        [MenuItem("DEADLOCK/Dev/Write Weapon Definition Fields")]
        public static void WriteAllFields()
        {
            var paths = AssetDatabase.FindAssets("t:" + nameof(WeaponDefinition))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith("Assets/"))
                .OrderBy(p => p)
                .ToArray();
            AssetDatabase.ForceReserializeAssets(paths, ForceReserializeAssetsOptions.ReserializeAssets);
            Debug.Log("[DEADLOCK] weapon definitions written with all fields: " + string.Join(", ", paths));
        }
    }
}
