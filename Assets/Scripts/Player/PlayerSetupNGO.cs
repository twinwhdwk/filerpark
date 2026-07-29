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

    // 랭크 기반 역할 배정(BotController의 문지기/열쇠 릴레이/쌍둥이 문지기 등)은
    // 원래 OwnerClientId 오름차순으로 계산했다. 그런데 서버가 실제 접속 없이 직접
    // 스폰하는 "채움 봇"(인원 부족 시 자동 추가)은 전부 서버 소유라 OwnerClientId가
    // 서로 동일해서(모두 서버 자신의 ID) 그 기준으로는 순위를 구분할 수 없다.
    // NetworkObjectId는 스폰되는 모든 NetworkObject마다 고유하게 배정되므로(실제
    // 클라이언트든 채움 봇이든 상관없이), 이걸 랭크 기준으로 쓰면 두 종류가 섞여도
    // 항상 안정적으로 구분된다. BotController/PlayerLabelNGO가 공유하는 유일한 랭크
    // 산출 지점이라, 여기서 한 번만 정의한다.
    public static int GetRank(NetworkObject self)
    {
        int rank = 0;
        foreach (GameObject candidate in ActivePlayers)
        {
            NetworkObject candidateNo = candidate.GetComponent<NetworkObject>();
            if (candidateNo != null && candidateNo.NetworkObjectId < self.NetworkObjectId) rank++;
        }
        return rank;
    }

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
            // OwnerClientId 대신 NetworkObjectId로 스폰 지점을 고른다 -- 서버가 직접
            // 스폰하는 채움 봇은 전부 OwnerClientId가 서버 자신으로 동일해서, 그
            // 기준으로는 채움 봇 여러 명이 전부 같은 스폰 지점에 겹쳐 스폰된다.
            int index = (int)(NetworkObjectId % (ulong)spawnPoints.Length);
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
