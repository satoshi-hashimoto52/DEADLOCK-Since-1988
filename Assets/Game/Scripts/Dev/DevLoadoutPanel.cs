using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Deadlock.Combat;
using Deadlock.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Deadlock.Dev
{
    /// <summary>
    /// DEVELOPMENT ONLY (assembly Deadlock.Dev: Editor and development builds; never in a release build). The development
    /// equipment of Stage 33, before the gacha exists: Slot 1 any main gun of the weapon catalog, Slot 2 the handgun or
    /// nothing, Slot 3 the knife (fixed), with each gun's values (confirmed and provisional told apart) and rounds. Opened
    /// from the menu ("開発用装備", in the free right end of the 設定 heading, so no menu button moves) of every offline
    /// scene that has the test choice of the gun (<see cref="DevGunSelection"/>, which does the equipping and refuses an
    /// invalid combination). Nothing is saved and nothing becomes an owned item.
    /// </summary>
    public sealed class DevLoadoutPanel : MonoBehaviour
    {
        public const string EntryButtonName = "DevLoadoutButton";
        public const string PanelName = "DevLoadoutPanel";
        public const string Title = "開発用装備（テスト用）";
        public const string Note = "開発・検証専用です。ここで装備した武器はガチャの所持品になりません（保存せず、アプリを終了すると元に戻ります）。\n" +
                                   "装備した銃は満タン・開始予備弾から始まります。正式版（リリースビルド）には表示されません。";

        static readonly Color Chosen = new Color(0.55f, 0.38f, 0.1f, 1f);
        static readonly Color Normal = new Color(0.15f, 0.16f, 0.2f, 0.92f);

        GameMenu menu;
        DevGunSelection selection;
        GameObject panel;
        Text info, message;
        readonly Dictionary<WeaponDefinition, Image> mainButtons = new Dictionary<WeaponDefinition, Image>();
        readonly Dictionary<WeaponDefinition, Image> subButtons = new Dictionary<WeaponDefinition, Image>();
        Image noSubButton;

        public bool IsOpen => panel != null && panel.activeSelf;
        public Button EntryButton { get; private set; }
        public string Message => message != null ? message.text : "";
        public string Info => info != null ? info.text : "";

        // ------------------------------------------------------------------------------------------- start-up

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!DevGunSelection.Enabled)
                return;
            var go = new GameObject("DevLoadoutPanelBoot");
            DontDestroyOnLoad(go);
            go.AddComponent<Starter>();
        }

        /// <summary>Puts the panel on every scene that loads with a test choice of the gun and a menu.</summary>
        sealed class Starter : MonoBehaviour
        {
            void Awake()
            {
                SceneManager.sceneLoaded += (s, m) => { if (this != null) StartCoroutine(Attach()); };
                StartCoroutine(Attach());
            }

            static IEnumerator Attach()
            {
                yield return null;
                if (!DevGunSelection.Enabled)
                    yield break;
                var sel = FindAnyObjectByType<DevGunSelection>();
                var m = FindAnyObjectByType<GameMenu>();
                if (sel != null && m != null && m.GetComponent<DevLoadoutPanel>() == null)
                    Create(m, sel);
            }
        }

        public static DevLoadoutPanel Create(GameMenu menu, DevGunSelection selection)
        {
            var p = menu.gameObject.AddComponent<DevLoadoutPanel>();
            p.menu = menu;
            p.selection = selection;
            p.Build();
            return p;
        }

        void OnEnable()
        {
            if (menu != null)
                menu.Closed += Close;
        }

        void OnDisable()
        {
            if (menu != null)
                menu.Closed -= Close;
        }

        // ------------------------------------------------------------------------------------------- open / equip

        public void Open()
        {
            if (panel == null)
                return;
            message.text = "";
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
            Refresh();
        }

        public void Close()
        {
            if (panel != null)
                panel.SetActive(false);
        }

        /// <summary>Same as tapping a Slot 1 button.</summary>
        public bool ChooseMain(WeaponDefinition gun)
        {
            bool ok = selection.TryEquipMain(gun, out string error);
            Report(ok, error, $"Slot 1 に {Name(gun)} を装備しました。");
            return ok;
        }

        /// <summary>Same as tapping a Slot 2 button (null: なし).</summary>
        public bool ChooseSub(WeaponDefinition gun)
        {
            bool ok = selection.TryEquipSub(gun, out string error);
            Report(ok, error, gun != null ? $"Slot 2 に {Name(gun)} を装備しました。" : "Slot 2 を空きにしました。");
            return ok;
        }

        void Report(bool ok, string error, string done)
        {
            message.text = ok ? done : "変更できません：" + error;
            message.color = ok ? new Color(0.7f, 1f, 0.7f) : new Color(1f, 0.6f, 0.4f);
            Refresh();
        }

        string Name(WeaponDefinition gun)
        {
            var e = selection.Catalog != null ? selection.Catalog.Find(gun) : null;
            return e != null ? e.displayName : gun != null ? gun.displayName : "なし";
        }

        static string CategoryLabel(CatalogCategory c) => c switch
        {
            CatalogCategory.Rifle => "ライフル",
            CatalogCategory.Smg => "SMG",
            CatalogCategory.Shotgun => "ショットガン",
            CatalogCategory.Lmg => "LMG",
            CatalogCategory.Handgun => "ハンドガン",
            _ => "ナイフ",
        };

        static string RarityLabel(WeaponRarity r) => r == WeaponRarity.Unassigned ? "レアリティ未定" : r.ToString().ToUpperInvariant();

        void Refresh()
        {
            if (panel == null)
                return;
            foreach (var kv in mainButtons)
                kv.Value.color = kv.Key == selection.MainGun ? Chosen : Normal;
            foreach (var kv in subButtons)
                kv.Value.color = kv.Key == selection.SubGun ? Chosen : Normal;
            if (noSubButton != null)
                noSubButton.color = selection.SubGun == null ? Chosen : Normal;

            var gun = selection.Gun;
            var sb = new StringBuilder();
            foreach (var slot in new[] { LoadoutSlot.Primary, LoadoutSlot.Secondary })
            {
                var d = gun.Inventory.Gun(slot);
                string slotName = slot == LoadoutSlot.Primary ? "Slot 1" : "Slot 2";
                if (d == null)
                {
                    sb.Append(slotName).Append("：なし\n\n");
                    continue;
                }
                var e = selection.Catalog != null ? selection.Catalog.Find(d) : null;
                var ammo = gun.Inventory.Ammo(slot);
                string reserve = ammo.InfiniteReserve ? "∞" : ammo.Reserve.ToString(CultureInfo.InvariantCulture);
                sb.Append(slotName).Append("：").Append(Name(d));
                if (e != null)
                    sb.Append("　").Append(RarityLabel(e.rarity)).Append("・").Append(CategoryLabel(e.category));
                sb.Append(gun.ActiveSlot == slot && gun.Definition == d ? "　（手に持っている銃）" : "").Append('\n');
                sb.Append("　装填 ").Append(ammo.Loaded).Append("／予備 ").Append(reserve)
                    .Append(string.Format(CultureInfo.InvariantCulture, "　（今の値：ダメージ {0:0.#}・{1:0}RPM・装弾 {2}・予備 開始{3}／上限{4}・弾薬箱 +{5}・リロード {6:0.0#}秒）",
                        d.damage, 60f / d.fireInterval, d.magazineSize, d.startReserve, d.reserveMax, AmmoRules.AmmoBoxRounds(d.magazineSize), d.reloadTime))
                    .Append('\n');
                if (e != null && !string.IsNullOrEmpty(e.confirmedNote))
                    sb.Append("　確定：").Append(e.confirmedNote).Append('\n');
                if (e != null && !string.IsNullOrEmpty(e.provisionalNote))
                    sb.Append("　仮値：").Append(e.provisionalNote).Append('\n');
                sb.Append('\n');
            }
            var knife = selection.Knife;
            sb.Append("Slot 3：").Append(knife != null ? knife.displayName : "なし").Append("（固定）");
            info.text = sb.ToString();
        }

        // ------------------------------------------------------------------------------------------- build

        void Build()
        {
            var layer = menu.transform.Find("MenuLayer") as RectTransform;
            var menuWindow = menu.transform.Find("MenuLayer/MenuPanel/Window") as RectTransform;
            var header = menuWindow != null ? menuWindow.Find("SettingsHeader") as RectTransform : null;
            if (layer == null || header == null)
                return;

            // Entry: the right end of the 設定 heading row (free; the buttons above and below keep their places).
            EntryButton = UiBuild.Button(menuWindow, EntryButtonName, "開発用装備", new Vector2(0.5f, 1f),
                new Vector2(header.anchoredPosition.x + header.sizeDelta.x * 0.5f - 95f, header.anchoredPosition.y - 2f), new Vector2(190f, 38f), Open, 22);
            ((Image)EntryButton.targetGraphic).color = new Color(0.4f, 0.1f, 0.1f, 0.95f);

            panel = UiBuild.Stretch(UiBuild.Rect(PanelName, layer)).gameObject;
            UiBuild.Image((RectTransform)panel.transform, new Color(0f, 0f, 0f, 0.6f), true);
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", panel.transform));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var window = UiBuild.Place(UiBuild.Rect("Window", safe), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240f, 900f));
            UiBuild.Image(window, new Color(0.08f, 0.09f, 0.12f, 0.97f), true);
            Vector2 top = new Vector2(0.5f, 1f);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", window), top, new Vector2(0f, -14f), new Vector2(1180f, 48f)), Title, 34, TextAnchor.MiddleCenter);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Note", window), top, new Vector2(0f, -66f), new Vector2(1180f, 60f)), Note, 20,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f));

            float y = -140f;
            void Label(string name, string text)
            {
                UiBuild.Text(UiBuild.Place(UiBuild.Rect(name, window), top, new Vector2(0f, y), new Vector2(1180f, 36f)), text, 24, TextAnchor.MiddleLeft,
                    new Color(1f, 1f, 1f, 0.7f));
                y -= 40f;
            }
            void Row(IReadOnlyList<(string name, string label, System.Action action, WeaponDefinition gun)> items, Dictionary<WeaponDefinition, Image> into)
            {
                const float w = 228f, gap = 8f;
                float x0 = -(items.Count * w + (items.Count - 1) * gap) * 0.5f + w * 0.5f;
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    var b = UiBuild.Button(window, it.name, it.label, top, new Vector2(x0 + i * (w + gap), y), new Vector2(w, 66f), it.action, 22);
                    if (it.gun != null)
                        into[it.gun] = (Image)b.targetGraphic;
                    else
                        noSubButton = (Image)b.targetGraphic;
                }
                y -= 84f;
            }

            Label("MainLabel", "Slot 1　メイン銃");
            var mains = new List<(string, string, System.Action, WeaponDefinition)>();
            foreach (var g in selection.MainGunChoices)
            {
                var d = g;
                mains.Add(("Main_" + d.weaponId, Name(d), () => ChooseMain(d), d));
            }
            Row(mains, mainButtons);

            Label("SubLabel", "Slot 2　サブ銃（ハンドガンのみ）");
            var subs = new List<(string, string, System.Action, WeaponDefinition)> { ("Sub_none", "なし", () => ChooseSub(null), null) };
            foreach (var g in selection.SubGunChoices)
            {
                var d = g;
                subs.Add(("Sub_" + d.weaponId, Name(d), () => ChooseSub(d), d));
            }
            Row(subs, subButtons);

            Label("KnifeLabel", "Slot 3　ナイフ（固定。変更できません）");
            info = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Info", window), top, new Vector2(0f, y - 4f), new Vector2(1180f, 360f)), "", 20, TextAnchor.UpperLeft);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            message = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Message", window), new Vector2(0.5f, 0f), new Vector2(-130f, 30f), new Vector2(900f, 40f)), "", 22,
                TextAnchor.MiddleLeft);
            var close = UiBuild.Button(window, "DevLoadoutClose", "閉じる", new Vector2(1f, 0f), new Vector2(-20f, 20f), new Vector2(220f, 64f), Close, 26);
            ((RectTransform)close.transform).pivot = new Vector2(1f, 0f);
            panel.SetActive(false);
        }
    }
}
