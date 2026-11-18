using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using Deadlock.Gacha;
using Deadlock.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.Dev
{
    /// <summary>
    /// DEVELOPMENT ONLY (assembly Deadlock.Dev: Editor and development builds; never in a release build). Tools in the
    /// gacha screen until the official parts exist: points for testing (matches give none yet, Stage 35), the test items
    /// (the official items alone cannot fill every rarity, so the official gacha stays closed), a simulated LEGENDARY
    /// event (first run / rerun; the official decision needs the server's time), and the export / import of the gacha
    /// data (D-059: checked, confirmed, backed up, replaced as a whole, checked again). Switches last for the app session.
    /// </summary>
    public static class GachaDevTools
    {
        public const int PointsStep = 1000;
        public const string ExportFolder = "exports";
        public const string ImportFile = "import/gacha_import.json";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            GachaScreen.DevToolsBuilt -= Build;
            GachaScreen.DevToolsBuilt += Build;
        }

        static string GachaFolder => Path.Combine(Application.persistentDataPath, GachaAccount.FolderName);

        /// <summary>Where an import is read from (the app's data folder).</summary>
        public static string ImportPath => Path.Combine(GachaFolder, ImportFile);

        static void Build(GachaScreen screen, RectTransform area)
        {
            if (!GachaDev.Enabled)
                return;
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("DevTitle", area), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1300f, 30f)),
                "開発用（正式版には表示されません。ここで得たものは正式な資産ではありません）", 18, TextAnchor.MiddleLeft, new Color(1f, 0.6f, 0.45f));
            const float w = 250f, h = 52f;
            Button Add(string name, string label, float x, float y, Action onClick)
            {
                var b = UiBuild.Button(area, name, label, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(w, h), onClick, 19);
                ((Image)b.targetGraphic).color = new Color(0.4f, 0.1f, 0.1f, 0.95f);
                return b;
            }
            Text Label(Button b) => b.GetComponentInChildren<Text>();

            Add("DevPoints", $"+{PointsStep:N0}pt", -520f, -36f, () =>
            {
                var r = GachaAccount.Service.GrantDevPoints(PointsStep);
                screen.ShowMessage(r.Ok ? $"開発用に {PointsStep:N0}pt を加えました" : r.Message);
                screen.Refresh();
            });
            Button items = null;
            items = Add("DevTestItems", "", -260f, -36f, () =>
            {
                GachaDev.UseTestItems = !GachaDev.UseTestItems;
                Label(items).text = TestItemsLabel();
                screen.Refresh();
            });
            Label(items).text = TestItemsLabel();
            Button ev = null;
            ev = Add("DevEvent", "", 0f, -36f, () =>
            {
                var current = GachaDev.SimulatedEvent;
                GachaDev.SimulatedEvent = current == null ? GachaDev.TestEvent(false) : !current.rerun ? GachaDev.TestEvent(true) : null;
                Label(ev).text = EventLabel();
                screen.Refresh();
            });
            Label(ev).text = EventLabel();
            Add("DevExport", "書き出し", 260f, -36f, () => screen.ShowMessage(Export()));
            Add("DevImport", "読み込み", 520f, -36f, () => AskImport(screen));
            Button server = null;
            server = Add("DevServer", "", -390f, -96f, () => ToggleServer(screen, () => Label(server).text = ServerLabel()));
            ((RectTransform)server.transform).sizeDelta = new Vector2(510f, h);
            Label(server).text = ServerLabel();
        }

        // ------------------------------------------------------------------------------------------- server (Stage 37E)

        /// <summary>The economy service of the development connection (this Mac only): -economy-url, else the default.</summary>
        public static string ServerUrl = Arg("-economy-url") ?? ServerGachaSource.DefaultUrl;
        /// <summary>The local development account: -economy-account, else the default.</summary>
        public static string ServerAccount = Arg("-economy-account") ?? ServerGachaSource.DefaultAccount;
        /// <summary>Where an unconfirmed server action is kept (tests: their own folder).</summary>
        public static string ServerPendingFolder;
        public static ServerGachaSource Server { get; private set; }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }

        static string ServerLabel() => Server != null ? $"サーバー接続（開発用）：ON {Server.AccountId}" : "サーバー接続（開発用・このMac）：OFF";

        /// <summary>
        /// Switches the gacha screen between the device's own data and the economy service (development sign-in of a
        /// local account). The two are never mixed: the screen shows one of them.
        /// </summary>
        public static void ToggleServer(GachaScreen screen, Action changed = null)
        {
            if (Server != null)
            {
                UseLocal();
                changed?.Invoke();
                GachaScreen.Open(screen.CurrentPage).ShowMessage("端末のデータ（ローカル）に戻しました");
                return;
            }
            screen.ShowMessage("サーバーにサインインしています（開発用）…");
            screen.StartCoroutine(Wait(ServerGachaSource.SignIn(ServerUrl, ServerAccount, ServerPendingFolder), r =>
            {
                if (r.source == null)
                {
                    screen.ShowMessage("サーバーに接続できません：" + r.error);
                    return;
                }
                UseServer(r.source);
                changed?.Invoke();
                GachaScreen.Open(screen.CurrentPage);
            }));
        }

        public static void UseServer(ServerGachaSource source)
        {
            Server?.Dispose();
            Server = source;
            GachaScreen.SourceProvider = () => Server;
        }

        public static void UseLocal()
        {
            Server?.Dispose();
            Server = null;
            GachaScreen.SourceProvider = null;
        }

        static IEnumerator Wait<T>(Task<T> task, Action<T> then)
        {
            while (!task.IsCompleted)
                yield return null;
            then(task.IsFaulted || task.IsCanceled ? default : task.Result);
        }

        static string TestItemsLabel() => "テスト用アイテム：" + (GachaDev.UseTestItems ? "ON" : "OFF");

        static string EventLabel()
        {
            var e = GachaDev.SimulatedEvent;
            return "模擬イベント：" + (e == null ? "なし" : e.rerun ? "復刻" : "初回");
        }

        /// <summary>Writes the gacha data to exports/ as structured JSON. Returns the message to show.</summary>
        public static string Export()
        {
            string json = GachaAccount.Service.Export();
            if (json == null)
                return "書き出しは無効です";
            string dir = Path.Combine(GachaFolder, ExportFolder);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"gacha_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, json, Encoding.UTF8);
            return $"書き出しました：{GachaAccount.FolderName}/{ExportFolder}/{Path.GetFileName(path)}";
        }

        /// <summary>Reads import/gacha_import.json, checks it, and asks before replacing the gacha data.</summary>
        public static void AskImport(GachaScreen screen)
        {
            if (!File.Exists(ImportPath))
            {
                screen.ShowMessage($"読み込むファイルがありません（{GachaAccount.FolderName}/{ImportFile}）");
                return;
            }
            var service = GachaAccount.Service;
            var check = service.CheckImport(File.ReadAllText(ImportPath, Encoding.UTF8));
            if (!check.Ok)
            {
                screen.ShowMessage("読み込めません（何も変えていません）：" + check.Error);
                return;
            }
            screen.ShowConfirm("ガチャのデータを置き換えますか？",
                "読み込むデータ：" + check.Summary + "\n今のデータはバックアップしてから、ガチャ関連のデータ全体を置き換えます。",
                "置き換える", true, () =>
                {
                    var result = service.ApplyImport(check);
                    screen.PressConfirmNo();
                    screen.ShowMessage(result.Message);
                    screen.Refresh();
                });
        }
    }
}
