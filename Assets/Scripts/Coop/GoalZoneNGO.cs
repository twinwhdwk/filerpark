using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// 문(CoopDoorNGO)이 "인원수 조건"이라면 이건 "전원 동시 도달" 조건이다.
// 접속한 모든 플레이어가 동시에 이 존 안에 있어야 스테이지 클리어로 판정한다.
public class GoalZoneNGO : NetworkBehaviour
{
    [Header("클리어 조건")]
    public string playerTag = "Player";

    private readonly HashSet<Collider2D> playersInZone = new HashSet<Collider2D>();

    public readonly NetworkVariable<bool> stageCleared = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsServer || stageCleared.Value) return;
        if (!collision.CompareTag(playerTag)) return;

        playersInZone.Add(collision);
        CheckAllPlayersPresent();
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (!IsServer) return;
        playersInZone.Remove(collision);
    }

    private void CheckAllPlayersPresent()
    {
        // 씬 언로드/디스폰 타이밍에 따라 OnTriggerExit2D가 안 불리고 콜라이더가
        // 파괴되는 경우가 있다 -- 그러면 Unity의 오버로드된 == 연산자상으로는 null이지만
        // HashSet 안의 참조 자체는 그대로 남아 있는 "가짜 null"이 되어 Count가 실제보다
        // 부풀려진다. PushableBlockNGO/SeesawPlatformNGO가 이미 쓰는 것과 동일한 방어.
        playersInZone.RemoveWhere(c => c == null);

        int connectedPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;
        if (connectedPlayers > 0 && playersInZone.Count >= connectedPlayers)
        {
            Debug.Log($"[GoalZone] {gameObject.name} 스테이지 클리어 -- 전원({connectedPlayers}명) 도달");
            stageCleared.Value = true;
        }
    }
}
