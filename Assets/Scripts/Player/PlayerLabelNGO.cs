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
    private PlayerNicknameNGO nicknameComp;
    private float nextRefreshTime;

    public override void OnNetworkSpawn()
    {
        bodySprite = GetComponent<SpriteRenderer>();
        nicknameComp = GetComponent<PlayerNicknameNGO>();
        RefreshLabel();
    }

    // 닉네임이 바뀔 때마다 별도로 구독하지 않는다 -- 아래 Update()가 이미 0.5초마다
    // RefreshLabel()을 불러 nicknameComp.nickname.Value를 다시 읽으므로, 이벤트
    // 구독은 최대 0.5초의 지연을 줄이는 대가로 구독/해제 쌍을 하나 더 관리해야 하는
    // 별도 경로만 늘릴 뿐이다.
    private void Update()
    {
        if (Time.time < nextRefreshTime) return;
        nextRefreshTime = Time.time + 0.5f;
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (label == null || NetworkManager.Singleton == null) return;

        // PlayerSetupNGO.GetRank()로 위임한다 -- 예전엔 여기서 직접 ConnectedClientsIds를
        // OwnerClientId 기준으로 세었는데, 서버가 직접 스폰하는 채움 봇은 ConnectedClientsIds에
        // 아예 없고(실제 접속이 아님) OwnerClientId도 전부 서버 자신으로 동일해 서로 구분이
        // 안 됐다 -- 실측: 채움 봇이 섞인 로비에서 라벨이 전부 "P1"로 표시됨. BotController가
        // 이미 겪고 고친 것과 같은 문제라 같은 해법(NetworkObjectId 기준)을 그대로 쓴다.
        int rank = PlayerSetupNGO.GetRank(NetworkObject);

        string nicknameValue = nicknameComp != null ? nicknameComp.nickname.Value.ToString() : string.Empty;
        string text = string.IsNullOrEmpty(nicknameValue) ? $"P{rank + 1}" : nicknameValue;
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
