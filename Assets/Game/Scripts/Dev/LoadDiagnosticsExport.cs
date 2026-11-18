using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Deadlock.Game;
using Deadlock.Match;
using Deadlock.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Deadlock.Dev
{
    /// <summary>
    /// DEVELOPMENT ONLY (assembly Deadlock.Dev: Editor and development builds; never in a release build). Writes the
    /// load diagnostics (Stage 36B) as structured JSON to diagnostics/exports/ and, on an iOS development build, opens
    /// the system share sheet for it (Files, AirDrop, ...). Offered by a button in the home's settings screen. The log
    /// holds no personal data (times, steps, durations, counts, error kinds).
    /// </summary>
    public static class LoadDiagnosticsExport
    {
        public const string ButtonName = "LoadDiagnosticsExport";
        public const string ExportFolder = "exports";

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void DeadlockShareFile(string path);
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != GameScenes.Home || !Debug.isDebugBuild)
                return;
            var menu = UnityEngine.Object.FindAnyObjectByType<Deadlock.Home.HomeMenu>();
            if (menu == null)
                return;
            var panel = menu.PanelObject("SettingsPanel");
            var window = panel != null ? panel.transform.Find("SafeArea/Window") as RectTransform : null;
            if (window == null || window.Find(ButtonName) != null)
                return;
            Text status = null;
            var b = UiBuild.Button(window, ButtonName, "ロード診断ログを書き出す（開発用）", new Vector2(0.5f, 1f), new Vector2(0f, -350f), new Vector2(460f, 56f), () =>
            {
                string message = Export(out _);
                if (status != null)
                    status.text = message;
            }, 20);
            ((Image)b.targetGraphic).color = new Color(0.4f, 0.1f, 0.1f, 0.95f);
            status = UiBuild.Text(UiBuild.Place(UiBuild.Rect("LoadDiagnosticsStatus", window), new Vector2(0.5f, 1f), new Vector2(0f, -410f), new Vector2(700f, 40f)),
                "", 18, TextAnchor.MiddleCenter, new Color(1f, 0.74f, 0.36f));
        }

        /// <summary>Writes the JSON file and shares it (iOS). Returns the message to show; <paramref name="path"/> the file.</summary>
        public static string Export(out string path)
        {
            path = null;
            string json = LoadDiagnosticsAccount.Instance.ExportJson();
            if (json == null)
                return "書き出しは無効です（リリースビルド）";
            string dir = Path.Combine(LoadDiagnosticsAccount.Folder, ExportFolder);
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, $"load_diagnostics_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, json, Encoding.UTF8);
#if UNITY_IOS && !UNITY_EDITOR
            DeadlockShareFile(path);
            return "書き出しました（共有シートを開きます）";
#else
            return $"書き出しました：{LoadDiagnosticsAccount.FolderName}/{ExportFolder}/{Path.GetFileName(path)}（共有シートは iOS の開発ビルドだけ）";
#endif
        }
    }
}
