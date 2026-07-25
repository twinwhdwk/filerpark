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
    }

    private void OnEnable()
    {
        if (headerText != null)
        {
            headerText.text = "SCOREBOARD";
        }
    }

    private void Update()
    {
        if (scoreText == null) return;

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
