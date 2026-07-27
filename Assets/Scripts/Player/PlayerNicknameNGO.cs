using Unity.Collections;
using Unity.Netcode;

// Player Prefab에 부착. PlayerMovementNGO의 horizontalInput과 동일한 패턴(Owner
// 쓰기 권한 NetworkVariable을 RPC 없이 직접 대입)으로, 접속 화면에서 입력한 닉네임을
// 스폰 즉시 다른 모든 클라이언트에게 뿌린다. 표시 전용 정보라 서버 검증은 하지 않는다
// -- 이 프로젝트의 위협 모델(지인끼리 하는 소규모 협동 서버)에서 닉네임 위조로 얻을
// 수 있는 이득이 없다.
public class PlayerNicknameNGO : NetworkBehaviour
{
    public readonly NetworkVariable<FixedString32Bytes> nickname = new NetworkVariable<FixedString32Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            nickname.Value = PlayerProfile.GetLocalNickname();
        }
    }
}
