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
            MoveToSpawnPoint();
        }
    }

    public override void OnNetworkDespawn()
    {
        ActivePlayers.Remove(gameObject);
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
            Debug.LogWarning("현재 로드된 씬에 'SpawnPoint' 태그를 가진 오브젝트가 없습니다!");
        }
    }
}
