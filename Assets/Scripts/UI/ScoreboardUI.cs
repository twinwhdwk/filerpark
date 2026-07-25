using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// Lobby 씬에 배치. 최신 스코어보드를 정적 캐시로 들고 있다가(GameFlowManager의
// ClientRpc는 Lobby 씬이 로드되어 있지 않은 타이밍에도 호출될 수 있으므로) 이
// 오브젝트가 실제로 활성화되어 있을 때 화면에 그린다.
public class ScoreboardUI : MonoBehaviour
{
    public Text scoreText;

    private static readonly Dictionary<ulong, int> LatestScores = new Dictionary<ulong, int>();

    public static void UpdateScoreboard(ulong[] clientIds, int[] values)
    {
        LatestScores.Clear();
        for (int i = 0; i < clientIds.Length; i++)
        {
            LatestScores[clientIds[i]] = values[i];
        }
    }

    private void Update()
    {
        if (scoreText == null) return;

        if (LatestScores.Count == 0)
        {
            scoreText.text = string.Empty;
            return;
        }

        StringBuilder sb = new StringBuilder("스코어보드\n");
        foreach (KeyValuePair<ulong, int> entry in LatestScores)
        {
            sb.AppendLine($"플레이어 {entry.Key}: {entry.Value}점");
        }
        scoreText.text = sb.ToString();
    }
}
