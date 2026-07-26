using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// 색상만으로는 "저게 내 캐릭터인가"를 화면 구석에서 즉시 구분하기 어렵다 -- 캐릭터
// 머리 위에 접속 순서 기반 번호("P1", "P2"...)를 띄워 PlayerColorNGO가 이미 입힌
// 색과 함께 누가 누군지 바로 알아볼 수 있게 한다. 별도로 네트워크 동기화할 상태가
// 없다 -- ConnectedClientsIds는 이미 NGO가 모든 클라이언트에 동일하게 복제해주므로,
// BotController의 역할 순위 계산과 동일한 방식(OwnerClientId 오름차순 정렬)으로
// 각 클라이언트가 로컬에서 독립적으로 같은 번호를 계산한다.
public class PlayerLabelNGO : NetworkBehaviour
{
    public Text label;

    private SpriteRenderer bodySprite;
    private float nextRefreshTime;

    public override void OnNetworkSpawn()
    {
        bodySprite = GetComponent<SpriteRenderer>();
        RefreshLabel();
    }

    private void Update()
    {
        if (Time.time < nextRefreshTime) return;
        nextRefreshTime = Time.time + 0.5f;
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (label == null || NetworkManager.Singleton == null) return;

        int rank = 0;
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (id < OwnerClientId) rank++;
        }

        string text = $"P{rank + 1}";
        if (label.text != text)
        {
            label.text = text;
        }

        // 색은 PlayerColorNGO의 배정값을 다시 계산하지 않고 실제로 렌더 중인
        // SpriteRenderer.color를 그대로 읽는다 -- 두 스크립트가 서로 다른 공식으로
        // "같은" 색을 계산하려다 어긋나는 것보다, 눈에 보이는 값을 그대로 따라가는
        // 편이 항상 일치를 보장한다.
        if (bodySprite != null)
        {
            label.color = bodySprite.color;
        }
    }
}
