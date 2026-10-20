using System;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.Network
{
    /// <summary>
    /// Online test scene UI. While not connected: a lobby panel (this device's address, Host, address field + Join,
    /// back to the firing range, the reason the last attempt / session ended). While connected: a status line
    /// (role, address, players, the other player's HP), a respawn button when dead (test button; the host performs
    /// the respawn) and "Leave" in the in-game menu. Player input is blocked while the lobby is shown.
    /// Put it on the same GameObject as <see cref="GameMenu"/>.
    /// </summary>
    [DefaultExecutionOrder(-10)] // Start before GameMenu.Start, which puts the menu on top
    public sealed class OnlineSessionUi : MonoBehaviour, IGameMenuItems
    {
        const string LastHostKey = "deadlock.net.lastHost";

        [SerializeField] NetworkSession session;
        [SerializeField] PlayerInputRouter input;

        GameObject lobby;
        Text addressText;
        Text messageText;
        InputField hostField;
        Button hostButton;
        Button joinButton;
        Button cancelButton;
        Text statusText;
        Button respawnButton;
        GameMenu menu;

        public GameObject Lobby => lobby;
        public InputField HostField => hostField;
        public Button HostButton => hostButton;
        public Button JoinButton => joinButton;
        public Button RespawnButton => respawnButton;
        public string Message => messageText.text;
        public string Status => statusText.text;

        public void AddMenuItems(Action<string, string, Action> add) =>
            add("LeaveButton", "退出（接続を終了）", () =>
            {
                session.Leave();
                if (menu != null)
                    menu.Close();
            });

        void Awake()
        {
            menu = GetComponent<GameMenu>();
            Build();
        }

        void Start()
        {
            session.Changed += Refresh;
            // Above the HUD, below the menu (the menu moves itself to the top in its Start).
            lobby.transform.parent.SetAsLastSibling();
            Refresh();
        }

        void OnDestroy()
        {
            if (session != null)
                session.Changed -= Refresh;
            if (input != null)
                input.Unblock("lobby");
        }

        void Refresh()
        {
            bool offline = !session.IsActive;
            bool connecting = session.State == SessionState.Connecting;
            lobby.SetActive(offline || connecting);
            if (offline || connecting)
                input.Block("lobby");
            else
                input.Unblock("lobby");

            var addresses = NetworkSession.LocalAddresses();
            addressText.text = addresses.Count > 0
                ? $"この端末のアドレス：{string.Join(" / ", addresses)}　ポート {session.Port}"
                : $"この端末のアドレスを取得できませんでした（Wi-Fi接続を確認）　ポート {session.Port}";
            messageText.text = connecting ? $"接続中…（{session.HostAddress}:{session.Port}）" : session.LastMessage;
            hostButton.gameObject.SetActive(!connecting);
            joinButton.gameObject.SetActive(!connecting);
            hostField.interactable = !connecting;
            cancelButton.gameObject.SetActive(connecting);
        }

        void Update()
        {
            bool busy = session.IsBusy;
            hostButton.interactable = !busy;
            joinButton.interactable = !busy;

            if (!session.IsActive || session.State == SessionState.Connecting)
            {
                statusText.text = "";
                respawnButton.gameObject.SetActive(false);
                return;
            }

            NetworkAvatar mine = null, other = null;
            foreach (var a in NetworkAvatar.All)
            {
                if (a.IsLocalPlayerAvatar) mine = a;
                else other = a;
            }
            string role = session.State == SessionState.Hosting ? $"ホスト（{session.HostAddress}:{session.Port}）" : $"接続中（ホスト {session.HostAddress}）";
            string others = other == null ? "相手：未接続" : other.Dead.Value ? "相手：死亡" : $"相手 HP {Mathf.CeilToInt(other.Health.Value)}";
            statusText.text = $"オンライン検証　{role}　{NetworkAvatar.All.Count}/{NetworkSession.MaxPlayers}人　{others}";
            if (!string.IsNullOrEmpty(session.LastMessage) && session.State == SessionState.Hosting && other == null)
                statusText.text += $"　（{session.LastMessage}）";
            respawnButton.gameObject.SetActive(mine != null && mine.Dead.Value);
        }

        void OnHost() => session.StartHost();

        void OnJoin()
        {
            string address = hostField.text.Trim();
            try { PlayerPrefs.SetString(LastHostKey, address); } catch (Exception) { /* storage optional */ }
            session.Join(address);
        }

        void OnRespawn()
        {
            foreach (var a in NetworkAvatar.All)
                if (a.IsLocalPlayerAvatar)
                    a.RespawnRpc();
        }

        void Build()
        {
            var root = UiBuild.Stretch(UiBuild.Rect("OnlineLayer", transform));
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", root));
            safe.gameObject.AddComponent<SafeAreaFitter>();

            statusText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("OnlineStatus", safe), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(1300f, 36f)),
                "", 24, TextAnchor.MiddleCenter, new Color(0.7f, 0.95f, 1f));
            respawnButton = UiBuild.Button(safe, "RespawnButton", "リスポーン（検証用）", new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(420f, 90f), OnRespawn, 32);
            respawnButton.gameObject.SetActive(false);

            lobby = UiBuild.Stretch(UiBuild.Rect("Lobby", root)).gameObject;
            UiBuild.Image((RectTransform)lobby.transform, new Color(0f, 0f, 0f, 0.7f), true);
            var window = UiBuild.Place(UiBuild.Rect("Window", lobby.transform), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1100f, 640f));
            UiBuild.Image(window, new Color(0.08f, 0.09f, 0.12f, 0.96f), true);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", window), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1000f, 56f)),
                "オンライン検証（同じLAN・2人まで）", 40, TextAnchor.MiddleCenter);
            addressText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Address", window), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(1040f, 40f)),
                "", 26, TextAnchor.MiddleCenter, new Color(0.7f, 0.95f, 1f));

            hostButton = UiBuild.Button(window, "HostButton", "ホスト開始", new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(520f, 84f), OnHost, 32);

            UiBuild.Text(UiBuild.Place(UiBuild.Rect("JoinLabel", window), new Vector2(0.5f, 1f), new Vector2(0f, -268f), new Vector2(1000f, 36f)),
                "参加する：ホストの端末に表示されたアドレスを入力", 24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f));
            hostField = UiBuild.InputField(window, "HostAddressField", "192.168.x.x", new Vector2(0.5f, 1f), new Vector2(-150f, -316f), new Vector2(560f, 80f),
                32, InputField.ContentType.Standard);
            hostField.keyboardType = TouchScreenKeyboardType.DecimalPad;
            try { hostField.text = PlayerPrefs.GetString(LastHostKey, ""); } catch (Exception) { /* storage optional */ }
            joinButton = UiBuild.Button(window, "JoinButton", "参加", new Vector2(0.5f, 1f), new Vector2(300f, -316f), new Vector2(240f, 80f), OnJoin, 32);
            cancelButton = UiBuild.Button(window, "CancelButton", "キャンセル", new Vector2(0.5f, 1f), new Vector2(0f, -316f), new Vector2(320f, 80f), session.Leave, 30);

            messageText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Message", window), new Vector2(0.5f, 1f), new Vector2(0f, -420f), new Vector2(1000f, 110f)),
                "", 24, TextAnchor.UpperCenter, new Color(1f, 0.75f, 0.5f));
            messageText.horizontalOverflow = HorizontalWrapMode.Wrap;

            UiBuild.Button(window, "BackButton", "射撃場へ戻る", new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(360f, 70f),
                () => GameScenes.Load(GameScenes.FiringRange), 28);
        }
    }
}
