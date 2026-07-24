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
        int connectedPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;
        if (connectedPlayers > 0 && playersInZone.Count >= connectedPlayers)
        {
            stageCleared.Value = true;
        }
    }
}
