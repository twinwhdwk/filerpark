using UnityEngine;
using Unity.Netcode;

public class PlayerSetupNGO : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            int playerID = (int)OwnerClientId;

            GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag("SpawnPoint");

            if (spawnPoints.Length > 0)
            {
                int index = playerID % spawnPoints.Length;
                transform.position = spawnPoints[index].transform.position;
            }
            else
            {
                Debug.LogWarning("맵에 'SpawnPoint' 태그를 가진 오브젝트가 없습니다!");
            }
        }
    }
}
