using System;
using Deadlock.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>
    /// "設定 → サウンド" (Stage 39): Master, BGM, SE and UI volume (0–100 %) and mute, applied as they change and saved
    /// on this device (<see cref="SoundSettings"/>). The same panel opens from the game menu and the home's settings.
    /// </summary>
    public sealed class SoundSettingsPanel : MonoBehaviour
    {
        public const string Title = "設定 → サウンド";
        /// <summary>Said on the panel while the game has no sound files (none is approved yet).</summary>
        public const string NoSoundNote = "音源は制作中です。現在は音声ファイルがないため、音は鳴りません（設定は保存されます）。";

        readonly Text[] labels = new Text[4];
        readonly Slider[] sliders = new Slider[4];
        Text muteText;

        public Slider SliderOf(int row) => sliders[row];

        static readonly string[] Names = { "マスター", "BGM", "効果音（SE）", "画面の音（UI）" };

        static int Get(int row) => row == 0 ? SoundSettings.Master : SoundSettings.Volume((SoundGroup)(row - 1));

        static void Set(int row, int value)
        {
            if (row == 0)
                SoundSettings.Master = value;
            else
                SoundSettings.SetVolume((SoundGroup)(row - 1), value);
        }

        /// <summary>Builds the panel (hidden) on <paramref name="layer"/>; "戻る" calls <paramref name="onBack"/>.</summary>
        public static SoundSettingsPanel Build(RectTransform layer, Action onBack, Action<Button> style = null)
        {
            var root = UiBuild.Stretch(UiBuild.Rect("SoundSettingsPanel", layer));
            UiBuild.Image(root, new Color(0f, 0f, 0f, 0.6f), true);
            var panel = root.gameObject.AddComponent<SoundSettingsPanel>();
            var window = UiBuild.Place(UiBuild.Rect("Window", root), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 860f));
            UiBuild.Image(window, new Color(0.08f, 0.09f, 0.12f, 0.95f), true);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", window), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(840f, 60f)),
                Title, 40, TextAnchor.MiddleCenter);
            var note = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Note", window), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(860f, 64f)),
                NoSoundNote, 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            float y = -180f;
            for (int i = 0; i < 4; i++)
            {
                int row = i;
                panel.labels[i] = UiBuild.Text(UiBuild.Place(UiBuild.Rect(RowName(i) + "Label", window), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(900f, 40f)),
                    "", 26, TextAnchor.MiddleCenter);
                panel.sliders[i] = UiBuild.Slider(window, RowName(i) + "Slider", new Vector2(0.5f, 1f), new Vector2(0f, y - 54f), new Vector2(640f, 56f), 0f, 100f,
                    v => { Set(row, Mathf.RoundToInt(v)); panel.Refresh(); });
                y -= 120f;
            }
            panel.muteText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("MuteLabel", window), new Vector2(0.5f, 1f), new Vector2(-190f, y - 15f), new Vector2(520f, 40f)),
                "", 26, TextAnchor.MiddleLeft);
            var on = UiBuild.Button(window, "MuteOn", "ON", new Vector2(0.5f, 1f), new Vector2(170f, y), new Vector2(180f, 70f),
                () => { SoundSettings.Muted = true; panel.Refresh(); }, 26);
            var off = UiBuild.Button(window, "MuteOff", "OFF", new Vector2(0.5f, 1f), new Vector2(370f, y), new Vector2(180f, 70f),
                () => { SoundSettings.Muted = false; panel.Refresh(); }, 26);
            var back = UiBuild.Button(window, "BackButton", "戻る", new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(300f, 70f), onBack, 28);
            if (style != null)
            {
                style(on);
                style(off);
                style(back);
            }
            root.gameObject.SetActive(false);
            return panel;
        }

        static string RowName(int row) => row switch { 0 => "Master", 1 => "Bgm", 2 => "Se", _ => "Ui" };

        void OnEnable() => Refresh();

        public void Refresh()
        {
            for (int i = 0; i < 4; i++)
            {
                if (labels[i] == null)
                    return;
                int v = Get(i);
                labels[i].text = $"{Names[i]}：{v}%";
                sliders[i].SetValueWithoutNotify(v);
            }
            muteText.text = $"ミュート　現在：{(SoundSettings.Muted ? "ON" : "OFF")}";
        }
    }
}
