using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Dev;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.UI;
using Deadlock.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 33 in a running scene: the development equipment (menu → 開発用装備) puts the EPIC SMG in Slot 1 and the
    /// handgun in Slot 2 with the knife kept, refuses invalid combinations, keeps each gun's rounds over the switches,
    /// fires and reloads the EPIC SMG (950 RPM, 28 rounds at once, 28 from an ammo box), shows it without art and without
    /// an exception, writes no saved data, leaves the menu's rifle ⇄ SMG choice and the weapon lab's guns as they were,
    /// is gone when switched off, and fits an iPhone and an iPad screen (measured, not looked at).
    /// </summary>
    public class DevLoadoutTests : FiringRangeTestBase
    {
        public override void Setup()
        {
            base.Setup();
            DevGunSelection.ClearSelection();
            DevGunSelection.SwitchedOff = false;
        }

        public override void TearDown()
        {
            DevGunSelection.ClearSelection();
            DevGunSelection.SwitchedOff = false;
            base.TearDown();
        }

        GameMenu menu;
        DevGunSelection selection;
        DevLoadoutPanel panel;

        IEnumerator LoadTraining(InputMode mode = InputMode.Desktop, string scene = GameScenes.Training)
        {
            yield return LoadScene(mode, scene);
            foreach (var z in Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None))
                z.Active = false;
            yield return null;
            menu = Object.FindAnyObjectByType<GameMenu>();
            selection = player.GetComponent<DevGunSelection>();
            panel = menu != null ? menu.GetComponent<DevLoadoutPanel>() : null;
        }

        WeaponDefinition Choice(IReadOnlyList<WeaponDefinition> list, string id) => list.Single(d => d.weaponId == id);

        Button PanelButton(string name) =>
            menu.transform.Find($"MenuLayer/{DevLoadoutPanel.PanelName}/SafeArea/Window/{name}").GetComponent<Button>();

        IEnumerator OpenPanel()
        {
            menu.Open();
            yield return null;
            panel.EntryButton.onClick.Invoke();
            yield return null;
            Assert.IsTrue(panel.IsOpen);
        }

        IEnumerator CloseAll()
        {
            PanelButton("DevLoadoutClose").onClick.Invoke();
            yield return null;
            Assert.IsFalse(panel.IsOpen);
            Assert.IsTrue(menu.IsOpen, "back to the menu");
            menu.Close();
            yield return null;
        }

        IEnumerator WaitSwitch() => Wait(weapons.SwitchTime + 0.15f);

        [UnityTest]
        public IEnumerator DevEquipment_EpicSmgAndHandgun_SwitchKeepsRounds_FireReloadAmmoBox()
        {
            yield return LoadTraining();
            Assert.IsNotNull(selection);
            Assert.IsNotNull(panel, "offered in the menu of an offline scene");
            var rifle = gun.Definition;
            CollectionAssert.AreEqual(new[] { "proto_rifle", "proto_smg" }, selection.Options.Select(o => o.weaponId).ToArray(),
                "the menu's rifle ⇄ SMG choice and the weapon lab's guns unchanged");

            yield return OpenPanel();
            foreach (var id in new[] { "proto_rifle", "proto_smg", "proto_shotgun", "proto_lmg", EpicSmgSpec.WeaponId })
                Assert.IsNotNull(PanelButton("Main_" + id), id);
            Assert.IsNotNull(PanelButton("Sub_none"));
            Assert.IsNotNull(PanelButton("Sub_proto_handgun"));
            Assert.IsNull(menu.transform.Find($"MenuLayer/{DevLoadoutPanel.PanelName}/SafeArea/Window/Main_proto_handgun"), "no handgun for Slot 1");
            Assert.IsNull(menu.transform.Find($"MenuLayer/{DevLoadoutPanel.PanelName}/SafeArea/Window/Sub_{EpicSmgSpec.WeaponId}"), "no EPIC SMG for Slot 2");

            PanelButton("Main_" + EpicSmgSpec.WeaponId).onClick.Invoke();
            yield return null;
            var epic = gun.Definition;
            Assert.AreEqual(EpicSmgSpec.WeaponId, epic.weaponId);
            Assert.AreEqual(LoadoutSlot.Primary, gun.ActiveSlot);
            Assert.AreEqual(28, gun.Ammo.Loaded);
            Assert.AreEqual(56, gun.Ammo.Reserve);
            StringAssert.Contains("EPIC SMG（仮称）", panel.Message);
            StringAssert.Contains("EPIC・SMG", panel.Info);
            StringAssert.Contains("確定：EPIC", panel.Info);
            StringAssert.Contains("仮値：ダメージ18", panel.Info);
            PanelButton("Sub_proto_handgun").onClick.Invoke();
            yield return null;
            var handgun = gun.Inventory.Gun(LoadoutSlot.Secondary);
            Assert.AreEqual("proto_handgun", handgun.weaponId);
            Assert.AreSame(epic, gun.Definition, "Slot 1 stays in hand");
            Assert.AreEqual(12, gun.Inventory.Ammo(LoadoutSlot.Secondary).Loaded);
            Assert.AreEqual(24, gun.Inventory.Ammo(LoadoutSlot.Secondary).Reserve);
            Assert.AreEqual("proto_knife", melee.Definition.weaponId, "Slot 3 keeps the knife");
            StringAssert.Contains("Slot 3：ナイフ（固定）", panel.Info);
            yield return CloseAll();

            // The EPIC SMG fires automatically at 950 RPM. Stage 38: its hip image is drawn exactly when its approved
            // image is registered (never another gun's image; no art: no weapon image, no exception).
            var view = Object.FindAnyObjectByType<WeaponViewModel>();
            bool hasHip = epic.viewProfile != null && epic.viewProfile.hipSprite != null;
            Assert.AreEqual(hasHip, view.TryGetMuzzleScreenPoint(out _), "weapon image only from its registered art");
            if (hasHip)
                StringAssert.Contains("/EpicSMG/", UnityEditor.AssetDatabase.GetAssetPath(epic.viewProfile.hipSprite));
            int shots = 0;
            gun.Fired += () => shots++;
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.5f);
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            Assert.That(shots, Is.InRange(6, 10), "about 950 RPM for half a second (7.9)");
            int loaded = gun.Ammo.Loaded;
            Assert.AreEqual(28 - shots, loaded);

            // 3: the handgun; its own rounds; 1: the EPIC SMG with its rounds kept.
            yield return Tap(keyboard.digit3Key);
            yield return WaitSwitch();
            Assert.AreSame(handgun, gun.Definition);
            yield return Tap(mouse.leftButton);
            yield return null;
            Assert.AreEqual(11, gun.Ammo.Loaded);
            Assert.AreEqual(loaded, gun.Inventory.Ammo(LoadoutSlot.Primary).Loaded, "the EPIC SMG's rounds stay");
            yield return Tap(keyboard.digit2Key);
            yield return WaitSwitch();
            Assert.AreEqual(WeaponSlot.Melee, weapons.Current, "the knife as before");
            yield return Tap(keyboard.digit1Key);
            yield return WaitSwitch();
            Assert.AreSame(epic, gun.Definition);
            Assert.AreEqual(loaded, gun.Ammo.Loaded);
            Assert.AreEqual(11, gun.Inventory.Ammo(LoadoutSlot.Secondary).Loaded, "the handgun's rounds stay");

            // Reload: the whole magazine at once after the reload time.
            Assert.IsTrue(gun.TryStartReload());
            yield return Wait(epic.reloadTime * 0.5f);
            Assert.AreEqual(loaded, gun.Ammo.Loaded, "nothing loaded half way");
            yield return Wait(epic.reloadTime * 0.5f + 0.15f);
            Assert.AreEqual(28, gun.Ammo.Loaded);
            Assert.AreEqual(56 - (28 - loaded), gun.Ammo.Reserve);

            // Ammo box: 28 to the EPIC SMG (capped at 84), 12 to the handgun.
            int epicReserve = gun.Ammo.Reserve, handgunReserve = gun.Inventory.Ammo(LoadoutSlot.Secondary).Reserve;
            Assert.Greater(gun.ApplyAmmoBox(), 0);
            Assert.AreEqual(Mathf.Min(84, epicReserve + 28), gun.Ammo.Reserve);
            Assert.AreEqual(Mathf.Min(36, handgunReserve + 12), gun.Inventory.Ammo(LoadoutSlot.Secondary).Reserve);
            gun.ApplyAmmoBox();
            gun.ApplyAmmoBox();
            Assert.AreEqual(84, gun.Ammo.Reserve, "at most 84");

            // HUD: the approved side view when registered (Stage 38), else the stand-in name; the rounds.
            var hud = Object.FindAnyObjectByType<HudController>();
            yield return null;
            foreach (var (slot, d, name) in new[] { (LoadoutSlot.Primary, epic, "EPIC SMG"), (LoadoutSlot.Secondary, handgun, "HANDGUN") })
            {
                bool side = WeaponArtStatus.Of(d, UnityEditor.AssetDatabase.LoadAssetAtPath<SkinCatalog>("Assets/Game/Data/Skins/SkinCatalog.asset"), WeaponArtView.Side) == WeaponArtState.Approved;
                if (side)
                {
                    Assert.IsNotNull(hud.SlotArt(slot), $"{name}: its side view");
                    Assert.AreEqual("", hud.SlotCaption(slot).text);
                }
                else
                    Assert.AreEqual(name + WeaponArtStatus.PlaceholderMark, hud.SlotCaption(slot).text);
            }
            Assert.AreEqual($"28/84", hud.SlotAmmo(LoadoutSlot.Primary).text);
            Assert.IsTrue(hud.SlotSelected(LoadoutSlot.Primary));
            // An exception or error log would have failed the test (the test framework's default).

            // The menu's own button still cycles rifle ⇄ SMG (from the EPIC SMG: the rifle).
            menu.Open();
            yield return null;
            var devGunButton = menu.transform.Find("MenuLayer/MenuPanel/Window/DevGunButton");
            StringAssert.Contains("→ ライフル", devGunButton.GetComponentInChildren<Text>().text);
            devGunButton.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.AreSame(rifle, gun.Definition);
            Assert.AreSame(handgun, gun.Inventory.Gun(LoadoutSlot.Secondary), "Slot 2 kept");
            menu.Close();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DevEquipment_InvalidCombinations_Refused_NothingChanges()
        {
            yield return LoadTraining();
            var catalog = selection.Catalog;
            var epic = catalog.Find(EpicSmgSpec.WeaponId).gun;
            var handgun = catalog.Find("proto_handgun").gun;
            var rifle = gun.Definition;

            Assert.IsFalse(selection.TryEquipMain(handgun, out string error), "handgun in Slot 1");
            StringAssert.Contains("Slot 1", error);
            Assert.IsFalse(selection.TryEquipSub(epic, out error), "EPIC SMG in Slot 2");
            Assert.IsFalse(selection.TryEquipSub(rifle, out _), "rifle in Slot 2");
            Assert.IsFalse(selection.TryEquipMain(null, out _), "Slot 1 never empty");
            var stray = Object.Instantiate(epic);
            try
            {
                stray.weaponId = "test_not_in_catalog";
                Assert.IsFalse(selection.TryEquipMain(stray, out error), "only catalog weapons");
            }
            finally
            {
                Object.Destroy(stray);
            }
            Assert.AreSame(rifle, gun.Definition);
            Assert.IsFalse(gun.Inventory.Has(LoadoutSlot.Secondary));
            Assert.IsNull(DevGunSelection.SelectedId);
            Assert.IsNull(DevGunSelection.SelectedSecondaryId);

            // The panel says why, and changes nothing.
            yield return OpenPanel();
            Assert.IsFalse(panel.ChooseMain(handgun));
            StringAssert.StartsWith("変更できません", panel.Message);
            Assert.AreSame(rifle, gun.Definition);
            yield return CloseAll();

            // During a weapon switch: refused.
            Assert.IsTrue(selection.TryEquipSub(handgun, out _));
            yield return Tap(keyboard.digit3Key);
            Assert.IsTrue(weapons.IsSwitching);
            Assert.IsFalse(selection.TryEquipMain(epic, out error));
            StringAssert.Contains("switch", error);
            yield return WaitSwitch();
            Assert.AreSame(handgun, gun.Definition);

            // Emptying Slot 2 with the handgun out: the main gun comes back to the hand.
            Assert.IsTrue(selection.TryEquipSub(null, out _));
            Assert.IsFalse(gun.Inventory.Has(LoadoutSlot.Secondary));
            Assert.AreSame(rifle, gun.Definition);
            yield return Tap(keyboard.digit3Key);
            Assert.IsFalse(weapons.IsSwitching, "no sub gun: key 3 refused");
        }

        [UnityTest]
        public IEnumerator DevEquipment_SessionOnly_NoSavedData_SwitchedOffInARelease()
        {
            string dataDir = Application.persistentDataPath;
            string Files() => Directory.Exists(dataDir)
                ? string.Join("\n", Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories).OrderBy(f => f)
                    .Select(f => $"{f} {new FileInfo(f).Length} {File.GetLastWriteTimeUtc(f).Ticks}"))
                : "";
            string filesBefore = Files();
            string layoutBefore = PlayerPrefs.GetString(TouchLayout.PrefsKey, "<none>");

            yield return LoadTraining();
            var catalog = selection.Catalog;
            Assert.IsTrue(selection.TryEquipMain(catalog.Find(EpicSmgSpec.WeaponId).gun, out _));
            Assert.IsTrue(selection.TryEquipSub(catalog.Find("proto_handgun").gun, out _));

            // Kept for the app session: a scene load (retry, another scene) comes back with the same equipment.
            yield return LoadTraining(InputMode.Desktop, GameScenes.Ruins);
            Assert.AreEqual(EpicSmgSpec.WeaponId, gun.Definition.weaponId);
            Assert.AreEqual("proto_handgun", gun.Inventory.Gun(LoadoutSlot.Secondary).weaponId);
            Assert.AreEqual(28, gun.Ammo.Loaded, "fresh rounds");

            // Nothing saved: no file, no layout change, no owned-item key; the catalog has no owner data.
            Assert.AreEqual(filesBefore, Files(), "no file written");
            Assert.AreEqual(layoutBefore, PlayerPrefs.GetString(TouchLayout.PrefsKey, "<none>"));
            foreach (var key in new[] { "deadlock.inventory", "deadlock.owned", "deadlock.gacha", "deadlock.devLoadout", "deadlock.loadout" })
                Assert.IsFalse(PlayerPrefs.HasKey(key), key);
            Assert.IsNull(typeof(WeaponCatalogEntry).GetField("owned"));

            // A restart (cleared session) goes back to the scene's own guns.
            DevGunSelection.ClearSelection();
            yield return LoadTraining();
            Assert.AreEqual("proto_rifle", gun.Definition.weaponId);
            Assert.IsFalse(gun.Inventory.Has(LoadoutSlot.Secondary));

            // Switched off (as in a release build): no menu entry, every change refused, a kept choice not applied.
            Assert.IsTrue(selection.TryEquipMain(catalog.Find(EpicSmgSpec.WeaponId).gun, out _));
            DevGunSelection.SwitchedOff = true;
            Assert.IsFalse(DevGunSelection.Enabled);
            yield return LoadTraining();
            Assert.IsNull(panel, "no development equipment in the menu");
            Assert.IsNull(menu.transform.Find("MenuLayer/MenuPanel/Window/" + DevLoadoutPanel.EntryButtonName));
            Assert.AreEqual("proto_rifle", gun.Definition.weaponId, "a kept development choice is not applied");
            Assert.IsFalse(selection.TryEquipSub(catalog.Find("proto_handgun").gun, out string error));
            StringAssert.Contains("off", error);
            Assert.IsFalse(gun.Inventory.Has(LoadoutSlot.Secondary));
            Assert.IsNotNull(menu.transform.Find("MenuLayer/MenuPanel/Window/DevGunButton"), "the menu's rifle ⇄ SMG choice stays");
        }

        /// <summary>
        /// The menu entry and the panel on an iPhone 16 Pro (2622 × 1206, notch sides and home indicator off the Safe
        /// Area) and an iPad (2048 × 1536): the entry inside the menu window clear of every menu button; the panel
        /// window and its buttons inside the Safe Area.
        /// </summary>
        [UnityTest]
        public IEnumerator DevPanel_IPhoneAndIPad_InsideTheSafeArea_EntryClearOfTheMenuButtons()
        {
            yield return LoadTraining(InputMode.Touch); // the fullest menu (retry and three scene buttons)
            Assert.IsNotNull(panel);
            menu.Open();
            yield return null;
            panel.Open();
            yield return null;
            var canvas = menu.GetComponentInParent<Canvas>().rootCanvas;
            var mode = canvas.renderMode;
            var camGo = new GameObject("AspectCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 0;
            var fitters = Object.FindObjectsByType<SafeAreaFitter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Rect Pixels(RectTransform r)
            {
                var w = new Vector3[4];
                r.GetWorldCorners(w);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, w[0]), b = RectTransformUtility.WorldToScreenPoint(cam, w[2]);
                return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            }
            bool Inside(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 0.5f && inner.yMin >= outer.yMin - 0.5f
                && inner.xMax <= outer.xMax + 0.5f && inner.yMax <= outer.yMax + 0.5f;
            var menuWindow = (RectTransform)menu.transform.Find("MenuLayer/MenuPanel/Window");
            var panelSafe = (RectTransform)menu.transform.Find($"MenuLayer/{DevLoadoutPanel.PanelName}/SafeArea");
            var panelWindow = (RectTransform)panelSafe.Find("Window");
            try
            {
                foreach (var (screen, sides, bottom) in new[] { (new Vector2Int(2622, 1206), 186f, 63f), (new Vector2Int(2048, 1536), 0f, 0f) })
                {
                    var rt = new RenderTexture(screen.x, screen.y, 16);
                    cam.targetTexture = rt;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 1f;
                    foreach (var fitter in fitters)
                    {
                        fitter.enabled = false;
                        var r = (RectTransform)fitter.transform;
                        r.anchorMin = new Vector2(sides / screen.x, bottom / screen.y);
                        r.anchorMax = new Vector2(1f - sides / screen.x, 1f);
                        r.offsetMin = r.offsetMax = Vector2.zero;
                    }
                    for (int i = 0; i < 4; i++)
                        yield return null;
                    Canvas.ForceUpdateCanvases();
                    string what = $"{screen.x}x{screen.y}";

                    Rect safe = Pixels(panelSafe);
                    Rect window = Pixels(panelWindow);
                    Assert.IsTrue(Inside(safe, window), $"{what}: panel window {window} inside the Safe Area {safe}");
                    foreach (var b in panelWindow.GetComponentsInChildren<Button>())
                        Assert.IsTrue(Inside(window, Pixels((RectTransform)b.transform)), $"{what}: {b.name} inside the panel");
                    var info = Pixels((RectTransform)panelWindow.Find("Info"));
                    var close = Pixels((RectTransform)panelWindow.Find("DevLoadoutClose"));
                    Assert.IsTrue(Inside(window, info), $"{what}: info inside");

                    Rect entry = Pixels((RectTransform)panel.EntryButton.transform);
                    Rect menuRect = Pixels(menuWindow);
                    Assert.IsTrue(Inside(menuRect, entry), $"{what}: entry inside the menu window");
                    foreach (var b in menuWindow.GetComponentsInChildren<Button>())
                        if (b != panel.EntryButton)
                            Assert.IsFalse(entry.Overlaps(Pixels((RectTransform)b.transform)), $"{what}: entry clear of {b.name}");
                    Assert.IsFalse(close.Overlaps(info), $"{what}: close button clear of the info");

                    cam.targetTexture = null;
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
            finally
            {
                canvas.renderMode = mode;
                canvas.worldCamera = null;
                Object.Destroy(camGo);
                panel.Close();
                menu.Close();
            }
        }
    }
}
