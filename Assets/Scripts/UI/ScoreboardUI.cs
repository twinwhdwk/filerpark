using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// Lobby 씬에 배치. 최신 스코어보드를 정적 캐시로 들고 있다가(GameFlowManager의
// ClientRpc는 Lobby 씬이 로드되어 있지 않은 타이밍에도 호출될 수 있으므로) 이
// 오브젝트가 실제로 활성화되어 있을 때 화면에 그린다.
public class ScoreboardUI : MonoBehaviour
{
    public Text headerText;
    public Text scoreText;

    private static readonly Dictionary<ulong, int> LatestScores = new Dictionary<ulong, int>();
    // clientId가 스폰 순서와 무관하게 커질 수 있어(재접속 등), 처음 본 순서대로 색을
    // 고정 배정한다 -- PlayerColorNGO가 OwnerClientId로 색을 정하는 것과 별개로,
    // 로비에서도 "이 색 = 이 사람"이 스폰 전부터 일관되게 보이도록.
    private static readonly Dictionary<ulong, int> ColorSlotByClientId = new Dictionary<ulong, int>();

    // 데이터가 실제로 바뀌었을 때만 다시 그린다 -- 예전엔 Update()마다(초당 60번)
    // StringBuilder + rich text를 새로 만들어 Text.text에 대입했는데, 스코어는
    // ClientRpc가 올 때만 바뀌므로 대부분의 프레임은 완전히 낭비되는 작업이었다
    // (Text.text 대입은 Canvas 리빌드를 유발해서 특히 더 비싸다).
    private static bool dirty = true;

    public static void UpdateScoreboard(ulong[] clientIds, int[] values)
    {
        LatestScores.Clear();
        for (int i = 0; i < clientIds.Length; i++)
        {
            LatestScores[clientIds[i]] = values[i];
            if (!ColorSlotByClientId.ContainsKey(clientIds[i]))
            {
                ColorSlotByClientId[clientIds[i]] = ColorSlotByClientId.Count;
            }
        }
        dirty = true;
    }

    private void OnEnable()
    {
        if (headerText != null)
        {
            headerText.text = "SCOREBOARD";
        }
        // 씬이 막 로드되어 이 오브젝트가 처음 활성화되는 경우에도 한 번은 그려야 하므로.
        dirty = true;
    }

    // 점수가 바뀌었을 때 텍스트만 조용히 갈아끼우면 놓치기 쉽다 -- 짧은 스케일 펄스로
    // "방금 바뀌었다"는 걸 눈에 띄게 한다. UIPunchIn과 달리 SetActive로 트리거되는
    // 것이 아니라(이 오브젝트는 계속 활성 상태) 데이터 변경(dirty) 시점에 직접 건다.
    private const float PulseDuration = 0.2f;
    private const float PulseScale = 1.12f;
    private float pulseStartTime = -1f;

    private void Update()
    {
        if (scoreText != null && dirty)
        {
            dirty = false;
            RedrawScoreText();
            pulseStartTime = Time.unscaledTime;
        }

        if (pulseStartTime >= 0f)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - pulseStartTime) / PulseDuration);
            float scale = Mathf.Lerp(PulseScale, 1f, t);
            scoreText.transform.localScale = Vector3.one * scale;
            if (t >= 1f) pulseStartTime = -1f;
        }
    }

    private void RedrawScoreText()
    {
        if (LatestScores.Count == 0)
        {
            scoreText.text = "<i>아직 접속한 플레이어가 없습니다</i>";
            return;
        }

        StringBuilder sb = new StringBuilder();
        int row = 1;
        foreach (KeyValuePair<ulong, int> entry in LatestScores)
        {
            int colorSlot = ColorSlotByClientId.TryGetValue(entry.Key, out int slot) ? slot : 0;
            string hex = ColorUtility.ToHtmlStringRGB(PlayerColorNGO.GetColor(colorSlot));
            sb.AppendLine($"<color=#{hex}>●</color> 플레이어 {row} <color=#{ColorUtility.ToHtmlStringRGB(UITheme.ColorPrimary)}>{entry.Value}점</color>");
            row++;
        }
        scoreText.text = sb.ToString();
    }
}
