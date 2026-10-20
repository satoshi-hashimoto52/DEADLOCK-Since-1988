using Deadlock.Game;
using UnityEngine;

namespace Deadlock.UI
{
    /// <summary>Training round display: defeated zombies and time, and the cleared / failed panel with retry.</summary>
    public sealed class TrainingHud : MonoBehaviour
    {
        [SerializeField] TrainingSession session;

        UnityEngine.UI.Text status;
        GameObject resultPanel;
        UnityEngine.UI.Text resultTitle;
        UnityEngine.UI.Text resultDetail;

        public GameObject ResultPanel => resultPanel;

        void Awake()
        {
            var layer = UiBuild.Stretch(UiBuild.Rect("TrainingLayer", transform));
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", layer));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            status = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Status", safe), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(600f, 40f)),
                "", 28, TextAnchor.UpperCenter);

            resultPanel = UiBuild.Stretch(UiBuild.Rect("ResultPanel", layer)).gameObject;
            UiBuild.Image((RectTransform)resultPanel.transform, new Color(0f, 0f, 0f, 0.55f), true);
            var window = UiBuild.Place(UiBuild.Rect("Window", resultPanel.transform), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 420f));
            UiBuild.Image(window, new Color(0.08f, 0.09f, 0.12f, 0.95f), true);
            resultTitle = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", window), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(560f, 70f)),
                "", 56, TextAnchor.MiddleCenter);
            resultDetail = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Detail", window), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(560f, 40f)),
                "", 28, TextAnchor.MiddleCenter);
            UiBuild.Button(window, "RetryButton", "再挑戦", new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(420f, 76f), session.Retry);
            UiBuild.Button(window, "FiringRangeButton", "射撃場へ", new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(420f, 76f),
                () => GameScenes.Load(GameScenes.FiringRange));
            resultPanel.SetActive(false);
        }

        void OnEnable() => session.Finished += OnFinished;

        void OnDisable() => session.Finished -= OnFinished;

        void LateUpdate()
        {
            int seconds = Mathf.FloorToInt(session.ElapsedTime);
            status.text = $"トレーニング（試作）　討伐 {session.Defeated} / {session.Total}　{seconds / 60}:{seconds % 60:00}";
        }

        void OnFinished(TrainingResult result)
        {
            resultPanel.SetActive(true);
            resultTitle.text = result == TrainingResult.Cleared ? "クリア" : "失敗";
            resultTitle.color = result == TrainingResult.Cleared ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.4f, 0.35f);
            int seconds = Mathf.FloorToInt(session.ElapsedTime);
            resultDetail.text = result == TrainingResult.Cleared
                ? $"ゾンビを全滅させました（討伐 {session.Defeated} / {session.Total}、{seconds / 60}:{seconds % 60:00}）"
                : $"倒されました（討伐 {session.Defeated} / {session.Total}、{seconds / 60}:{seconds % 60:00}）";
        }
    }
}
