using UnityEngine;
using Unity.Netcode;

public class PlayerSetupNGO : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            MoveToSpawnPoint();
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
            Debug.LogWarning("현재 로드된 씬에 'SpawnPoint' 태그를 가진 오브젝트가 없습니다!");
        }
    }
}
