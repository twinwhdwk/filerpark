using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class PlayerSetupNGO : NetworkBehaviour
{
    // CoopCameraFollow/BotController가 매 프레임 GameObject.FindGameObjectsWithTag("Player")로
    // 새 배열을 할당하며 씬을 스캔하던 것을, 스폰/디스폰 시점에만 갱신되는 공유 목록으로
    // 대체한다 -- 스폰/디스폰은 이미 신뢰할 수 있게 한 번씩만 불리므로(late-join/disconnect
    // 모두 검증됨) 별도 정리 로직 없이 항상 정확하다.
    public static readonly List<GameObject> ActivePlayers = new List<GameObject>();

    public override void OnNetworkSpawn()
    {
        ActivePlayers.Add(gameObject);

        if (IsServer)
        {
            Debug.Log($"[PlayerSetup] 플레이어 스폰: clientId={OwnerClientId} (현재 {ActivePlayers.Count}명)");
            MoveToSpawnPoint();
        }
    }

    public override void OnNetworkDespawn()
    {
        ActivePlayers.Remove(gameObject);

        if (IsServer)
        {
            Debug.Log($"[PlayerSetup] 플레이어 디스폰: clientId={OwnerClientId} (남은 {ActivePlayers.Count}명)");
        }
    }

    // GameFlowManager가 Lobby<->Stage 전환마다(씬 로드 완료 후) 호출해서, 현재
    // 로드되어 있는 씬의 SpawnPoint로 다시 위치시킨다. 매 순간 로드된 씬(Lobby 또는
    // 현재 스테이지) 하나에만 SpawnPoint 태그 오브젝트가 존재하므로, 이 메서드는
    // "어느 씬인지"를 전혀 몰라도 항상 올바른 위치를 찾는다.
    public void MoveToSpawnPoint()
    {
        if (!IsServer) return;

        GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag("SpawnPoint");

        if (spawnPoints.Length > 0)
        {
            int index = (int)(OwnerClientId % (ulong)spawnPoints.Length);
            transform.position = spawnPoints[index].transform.position;
        }
        else
        {
            // 어느 클라이언트가 걸렸는지 몰라 원인 조사가 오래 걸린 적이 있어(씬 전환
            // 사이 창에서 낙사한 특정 플레이어를 이 경고만으로는 구분할 수 없었다)
            // clientId를 같이 남긴다.
            Debug.LogWarning($"[PlayerSetup] clientId={OwnerClientId}: 현재 로드된 씬에 'SpawnPoint' 태그를 가진 오브젝트가 없습니다!");
        }
    }
}
