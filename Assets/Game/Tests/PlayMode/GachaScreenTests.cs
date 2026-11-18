using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Gacha;
using Deadlock.Game;
using Deadlock.Home;
using Deadlock.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 34 in the running home: Crate → the gacha screen (closed while the official items cannot fill every rarity;
    /// open with the development test items), the confirmation of points before every draw (also "again"), the opening
    /// one by one with its effect per rarity and skip, the results, double taps, too few points, the exchange shop, the
    /// history, a draw saved but not shown, the development tools off as in a release, the development equipment kept
    /// apart from owned items, and the screen inside the Safe Area of an iPhone and an iPad (measured). The gacha data is
    /// kept in memory: the app's file is never touched.
    /// </summary>
    public class GachaScreenTests : InputTestFixture
    {
        sealed class QueueRandom : IGachaRandom
        {
            public readonly Queue<int> Queue = new Queue<int>();
            readonly System.Random fallback = new System.Random(11);

            public int Next(int max) => Queue.Count > 0 ? Mathf.Min(Queue.Dequeue(), max - 1) : fallback.Next(max);
        }

        MemoryGachaStore store;
        QueueRandom random;
        HomeMenu menu;

        public override void Setup()
        {
            base.Setup();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            store = new MemoryGachaStore();
            random = new QueueRandom();
            GachaAccount.StoreOverride = store;
            GachaAccount.RandomOverride = random;
            GachaAccount.Reset();
            GachaDev.Reset();
        }

        public override void TearDown()
        {
            GachaAccount.StoreOverride = null;
            GachaAccount.RandomOverride = null;
            GachaAccount.Reset();
            GachaDev.Reset();
            DevGunSelection.ClearSelection();
            base.TearDown();
        }

        IEnumerator LoadHome()
        {
            SceneManager.LoadScene(GameScenes.Home);
            yield return null;
            yield return null;
            menu = Object.FindAnyObjectByType<HomeMenu>();
        }

        static Button Find(GachaScreen screen, string name) =>
            screen.GetComponentsInChildren<Button>(true).First(b => b.name == name);

        static void Click(GachaScreen screen, string name) => Find(screen, name).onClick.Invoke();

        string PointsText => menu.Layout.Element(HomeMenu.Points).GetComponentInChildren<Text>().text;

        IEnumerator OpenFromCrate(string button)
        {
            menu.Layout.Element(HomeMenu.Crate).GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.AreEqual("CratePanel", menu.OpenPanelName);
            menu.PanelObject("CratePanel").GetComponentsInChildren<Button>(true).First(b => b.name == button).onClick.Invoke();
            yield return null;
            Assert.IsNull(menu.OpenPanelName, "the hub closes");
        }

        [UnityTest]
        public IEnumerator Crate_Gacha_ConfirmOpenSkipResultsAgain_DoubleTap_TooFewPoints()
        {
            yield return LoadHome();
            Assert.AreEqual("－ PT", PointsText, "no gacha data yet");
            yield return OpenFromCrate("CrateGacha");
            var screen = Object.FindAnyObjectByType<GachaScreen>();
            Assert.IsNotNull(screen);
            Assert.AreEqual(GachaScreen.Page.Gacha, screen.CurrentPage);
            // Official items only: every rarity cannot be filled, so no draw (and nothing spent).
            Assert.IsFalse(Find(screen, "DrawSingle").interactable);
            Assert.IsFalse(Find(screen, "DrawTen").interactable);
            StringAssert.Contains("揃っていない", screen.GetComponentsInChildren<Text>(true).First(t => t.name == "Status").text);
            StringAssert.Contains("60.30%", screen.GetComponentsInChildren<Text>(true).First(t => t.name == "Rates").text);
            StringAssert.Contains("82.76%", screen.GetComponentsInChildren<Text>(true).First(t => t.name == "Rates").text);

            // Development: points, test items.
            Click(screen, "DevPoints");
            Click(screen, "DevPoints");
            yield return null;
            Assert.AreEqual(2000, screen.Service.State.points);
            Assert.AreEqual("2,000 PT", PointsText, "the home shows the balance");
            Click(screen, "DevTestItems");
            yield return null;
            Assert.IsTrue(Find(screen, "DrawSingle").interactable);

            // Single: confirmation (current, cost, after), then the opening, then the list.
            Click(screen, "DrawSingle");
            yield return null;
            Assert.IsTrue(screen.IsConfirmOpen);
            StringAssert.Contains("現在のポイント　2,000 pt", screen.ConfirmBody);
            StringAssert.Contains("消費ポイント　　100 pt", screen.ConfirmBody);
            StringAssert.Contains("実行後の残高　　1,900 pt", screen.ConfirmBody);
            Click(screen, "ConfirmNo");
            Assert.AreEqual(2000, screen.Service.State.points, "やめる: nothing spent");
            Click(screen, "DrawSingle");
            screen.PressConfirmYes();
            screen.PressConfirmYes(); // a double tap
            Click(screen, "ConfirmYes");
            yield return null;
            Assert.AreEqual(1900, screen.Service.State.points, "charged once");
            Assert.AreEqual(1, screen.Service.State.history.Count);
            Assert.IsTrue(screen.IsOpening);
            Assert.IsFalse(screen.ShownRecord.revealed, "saved before the opening");
            Click(screen, "OpenNext");
            yield return null;
            Assert.IsTrue(screen.IsResultsOpen);
            Assert.AreEqual(1, screen.ResultCells.Count);
            Assert.IsTrue(screen.Service.State.history[0].revealed);

            // もう一度引く: the same kind, a new draw, confirmed again.
            Click(screen, "Again");
            yield return null;
            Assert.IsTrue(screen.IsConfirmOpen);
            StringAssert.Contains("実行後の残高　　1,800 pt", screen.ConfirmBody);
            Click(screen, "ConfirmYes");
            yield return null;
            Assert.AreEqual(1800, screen.Service.State.points);
            Assert.AreEqual(2, screen.Service.State.history.Count);
            Click(screen, "OpenNext");
            Click(screen, "ResultsClose");

            // 11-draw: one by one, then skip to the list; skipping never changes a result.
            Click(screen, "DrawTen");
            Click(screen, "ConfirmYes");
            yield return null;
            Assert.AreEqual(800, screen.Service.State.points);
            var record = screen.ShownRecord;
            var ids = record.results.Select(r => r.itemId).ToArray();
            Assert.AreEqual(11, ids.Length);
            Click(screen, "OpenNext");
            Click(screen, "OpenNext");
            Assert.AreEqual(2, screen.OpeningIndex);
            Click(screen, "OpenSkip");
            yield return null;
            Assert.IsTrue(screen.IsResultsOpen);
            Assert.AreEqual(11, screen.ResultCells.Count);
            CollectionAssert.AreEqual(ids, screen.Service.State.history.Last().results.Select(r => r.itemId).ToArray());
            Assert.IsTrue(screen.Service.State.history.Last().revealed);
            Assert.GreaterOrEqual((int)record.results[10].Rarity, (int)WeaponRarity.Rare);

            // Too few points for an 11-draw: the confirmation says so and cannot draw.
            Click(screen, "Again");
            yield return null;
            Assert.IsFalse(screen.ConfirmEnabled);
            StringAssert.Contains("不足", screen.ConfirmBody);
            screen.PressConfirmYes();
            Assert.AreEqual(800, screen.Service.State.points);
            Click(screen, "ConfirmNo");

            // History: one row per purchase.
            Click(screen, "TabHistory");
            yield return null;
            Assert.AreEqual(3, screen.HistoryRows);
            Click(screen, "GachaClose");
            yield return null;
            Assert.IsFalse(screen.gameObject.activeSelf);
            Assert.AreEqual("800 PT", PointsText);
        }

        [UnityTest]
        public IEnumerator Opening_EffectPerRarity_LegendaryOnlyInTheEvent()
        {
            yield return LoadHome();
            yield return OpenFromCrate("CrateGacha");
            var screen = Object.FindAnyObjectByType<GachaScreen>();
            Click(screen, "DevPoints");
            Click(screen, "DevTestItems");
            Click(screen, "DevEvent"); // simulated first-run event
            yield return null;
            Assert.IsNotNull(screen.Service.ActiveEvent);
            StringAssert.Contains("0.50%", screen.GetComponentsInChildren<Text>(true).First(t => t.name == "Rates").text);
            // Event weights 120 / 50 / 24 / 5 / 1 of 200: 0 COMMON, 194 EPIC, 199 LEGENDARY.
            foreach (var (roll, effect, rarity) in new[] { (0, GachaEffect.Standard, WeaponRarity.Common), (194, GachaEffect.Emphasis, WeaponRarity.Epic), (199, GachaEffect.Legendary, WeaponRarity.Legendary) })
            {
                random.Queue.Enqueue(roll);
                random.Queue.Enqueue(0);
                random.Queue.Enqueue(0);
                Click(screen, "DrawSingle");
                Click(screen, "ConfirmYes");
                yield return null;
                Assert.AreEqual(rarity, screen.ShownRecord.results[0].Rarity);
                Assert.AreEqual(effect, screen.CurrentEffect);
                Assert.Less(GachaPresentation.StandardSeconds, GachaPresentation.EmphasisSeconds);
                Assert.Less(GachaPresentation.EmphasisSeconds, GachaPresentation.LegendarySeconds);
                yield return null;
                Click(screen, "OpenSkip");
                Click(screen, "ResultsClose");
            }
            Assert.AreEqual("devtest_event_1", screen.Service.State.history.Last().eventId);
            // The event ends (development switch: rerun, then none): no LEGENDARY can come out.
            Click(screen, "DevEvent");
            Click(screen, "DevEvent");
            yield return null;
            Assert.IsNull(screen.Service.ActiveEvent);
            random.Queue.Enqueue(198); // the highest roll of 199 → EPIC, never LEGENDARY
            random.Queue.Enqueue(0);
            random.Queue.Enqueue(0);
            Click(screen, "DrawSingle");
            Click(screen, "ConfirmYes");
            yield return null;
            Assert.AreEqual(WeaponRarity.Epic, screen.ShownRecord.results[0].Rarity);
            Assert.IsTrue(screen.Service.State.Owns(ItemCatalog.DevLegendaryWeapon), "kept after the event");
        }

        [UnityTest]
        public IEnumerator Shop_ExchangeEpicSmg_OwnedCannotBeBoughtAgain_HistoryPage()
        {
            yield return LoadHome();
            yield return OpenFromCrate("CrateShop");
            var screen = Object.FindAnyObjectByType<GachaScreen>();
            Assert.AreEqual(GachaScreen.Page.Shop, screen.CurrentPage);
            var epic = screen.ShopButton("weapon:proto_epic_smg");
            Assert.IsNotNull(epic, "the EPIC SMG is in the shop");
            Assert.IsFalse(epic.interactable, "no medals");
            StringAssert.Contains("1,000 枚で交換", epic.GetComponentInChildren<Text>().text);
            // Medals (as from duplicates) put in the saved data.
            var data = screen.Service.State.Clone();
            data.medals = 1500;
            store.Write(GachaSave.Serialize(data));
            screen.Service.Load();
            screen.Refresh();
            yield return null;
            epic = screen.ShopButton("weapon:proto_epic_smg");
            Assert.IsTrue(epic.interactable);
            epic.onClick.Invoke();
            yield return null;
            StringAssert.Contains("所持メダル　1,500 枚", screen.ConfirmBody);
            StringAssert.Contains("交換後　　　500 枚", screen.ConfirmBody);
            Click(screen, "ConfirmYes");
            yield return null;
            Assert.AreEqual(500, screen.Service.State.medals);
            Assert.IsTrue(screen.Service.State.Owns("weapon:proto_epic_smg"));
            epic = screen.ShopButton("weapon:proto_epic_smg");
            Assert.IsFalse(epic.interactable);
            Assert.AreEqual("所持済み", epic.GetComponentInChildren<Text>().text);
            Assert.IsNull(screen.ShopButton(ItemCatalog.DevGachaOnlyItem), "test items only with the switch, gacha-only never");
            Click(screen, "DevTestItems");
            yield return null;
            Assert.IsNull(screen.ShopButton(ItemCatalog.DevGachaOnlyItem));
            Assert.IsNull(screen.ShopButton(ItemCatalog.DevLegendarySkin), "LEGENDARY only in its event");
            Click(screen, "TabHistory");
            yield return null;
            Assert.AreEqual(1, screen.HistoryRows, "the empty line");
            Assert.AreEqual(0, screen.Service.State.history.Count, "exchanges are not draws");
        }

        [UnityTest]
        public IEnumerator SavedButNotShown_ShownWhenTheScreenOpensAgain()
        {
            yield return LoadHome();
            var service = GachaAccount.Service;
            GachaDev.UseTestItems = true;
            Assert.IsTrue(service.GrantDevPoints(1000).Ok);
            // The draw is saved, then the app stops before any opening.
            var r = service.Draw(GachaKind.Ten, "interrupted");
            Assert.AreEqual(GachaOutcome.Done, r.Outcome);
            GachaAccount.Reset(); // a restart reads the saved data again
            SceneManager.LoadScene(GameScenes.Home);
            yield return null;
            yield return null;
            menu = Object.FindAnyObjectByType<HomeMenu>();
            Assert.AreEqual("0 PT", PointsText);
            yield return OpenFromCrate("CrateGacha");
            var screen = Object.FindAnyObjectByType<GachaScreen>();
            Assert.IsTrue(screen.IsOpening, "shown again");
            Assert.AreEqual("interrupted", screen.ShownRecord.requestId);
            StringAssert.Contains("前回確定した結果", screen.Message);
            Click(screen, "OpenSkip");
            Assert.IsNull(GachaAccount.Service.Unrevealed());
            Assert.AreEqual(11, screen.ResultCells.Count);
        }

        [UnityTest]
        public IEnumerator DevelopmentTools_OffAsInARelease()
        {
            GachaDev.SwitchedOff = true;
            yield return LoadHome();
            yield return OpenFromCrate("CrateGacha");
            var screen = Object.FindAnyObjectByType<GachaScreen>();
            Assert.IsFalse(screen.GetComponentsInChildren<Button>(true).Any(b => b.name.StartsWith("Dev")), "no development button");
            Assert.IsFalse(Find(screen, "DrawSingle").interactable, "official items only: closed");
            Assert.AreEqual(GachaOutcome.Disabled, screen.Service.GrantDevPoints(100).Outcome);
            Assert.AreEqual(0, store.Writes, "nothing saved");
        }

        [UnityTest]
        public IEnumerator DevelopmentEquipment_IsNotAnOwnedItem()
        {
            SceneManager.LoadScene(GameScenes.Training);
            yield return null;
            yield return null;
            var selection = GameObject.Find("Player").GetComponent<DevGunSelection>();
            var epic = selection.Catalog.Find(EpicSmgSpec.WeaponId).gun;
            Assert.IsTrue(selection.TryEquipMain(epic, out string error), error);
            Assert.IsTrue(selection.TryEquipSub(selection.Catalog.Find("proto_handgun").gun, out error), error);
            Assert.IsFalse(GachaAccount.Service.State.Owns(ItemDefinition.WeaponItemId(EpicSmgSpec.WeaponId)), "equipping for tests owns nothing");
            Assert.IsEmpty(GachaAccount.Service.State.owned);
            Assert.AreEqual(0, store.Writes, "the gacha data is not written");
            // Owning the EPIC SMG does not change the development equipment either.
            var data = new GachaStateData();
            data.owned.Add(new OwnedItem { itemId = ItemDefinition.WeaponItemId(EpicSmgSpec.WeaponId), source = "exchange" });
            store.Write(GachaSave.Serialize(data));
            GachaAccount.Service.Load();
            Assert.AreEqual(5, selection.MainGunChoices.Count);
            CollectionAssert.AreEqual(new[] { "proto_rifle", "proto_smg" }, selection.Options.Select(o => o.weaponId).ToArray());
        }

        /// <summary>The screen and its parts inside the Safe Area of an iPhone 16 Pro (2622 × 1206) and an iPad (2048 × 1536).</summary>
        [UnityTest]
        public IEnumerator Screen_IPhoneAndIPad_InsideTheSafeArea()
        {
            yield return LoadHome();
            yield return OpenFromCrate("CrateGacha");
            var screen = Object.FindAnyObjectByType<GachaScreen>();
            Click(screen, "DevPoints");
            Click(screen, "DevTestItems");
            Click(screen, "DrawTen");
            Click(screen, "ConfirmYes");
            Click(screen, "OpenSkip");
            yield return null;
            var canvas = screen.GetComponent<Canvas>();
            var camGo = new GameObject("AspectCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 0;
            var fitter = screen.GetComponentInChildren<SafeAreaFitter>(true);
            Rect Pixels(RectTransform r)
            {
                var w = new Vector3[4];
                r.GetWorldCorners(w);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, w[0]), b = RectTransformUtility.WorldToScreenPoint(cam, w[2]);
                return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            }
            bool Inside(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 0.5f && inner.yMin >= outer.yMin - 0.5f
                && inner.xMax <= outer.xMax + 0.5f && inner.yMax <= outer.yMax + 0.5f;
            try
            {
                foreach (var (size, sides, bottom) in new[] { (new Vector2Int(2622, 1206), 186f, 63f), (new Vector2Int(2048, 1536), 0f, 0f) })
                {
                    var rt = new RenderTexture(size.x, size.y, 16);
                    cam.targetTexture = rt;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 1f;
                    fitter.enabled = false;
                    var fr = (RectTransform)fitter.transform;
                    fr.anchorMin = new Vector2(sides / size.x, bottom / size.y);
                    fr.anchorMax = new Vector2(1f - sides / size.x, 1f);
                    fr.offsetMin = fr.offsetMax = Vector2.zero;
                    for (int i = 0; i < 4; i++)
                        yield return null;
                    Canvas.ForceUpdateCanvases();
                    string what = $"{size.x}x{size.y}";
                    Rect safe = Pixels(fr);
                    Rect window = Pixels(screen.Window);
                    Assert.IsTrue(Inside(safe, window), $"{what}: window {window} inside the Safe Area {safe}");
                    foreach (var b in screen.Window.GetComponentsInChildren<Button>())
                        Assert.IsTrue(Inside(window, Pixels((RectTransform)b.transform)), $"{what}: {b.name} inside the window");
                    foreach (var cell in screen.ResultCells)
                        Assert.IsTrue(Inside(window, Pixels((RectTransform)cell)), $"{what}: {cell.name} inside the window");
                    cam.targetTexture = null;
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
            finally
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                Object.Destroy(camGo);
            }
        }
    }
}
