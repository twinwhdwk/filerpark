using UnityEngine;
using UnityEngine.UI;

// 접속 화면의 닉네임 입력칸에 부착. 저장된 값을 미리 채워두고, 접속 버튼을 누르는
// 순간 입력값을 PlayerProfile에 반영한다 -- OnConnectClicked이 NetworkBootstrapper.
// ConnectToServer보다 먼저 onClick에 등록되어야, PlayerNicknameNGO가 스폰 시점에
// 읽는 값이 이미 최신 상태다.
public class NicknameFieldUI : MonoBehaviour
{
    public InputField field;

    private void Start()
    {
        if (field != null)
        {
            field.text = PlayerProfile.GetLocalNickname();
        }
    }

    public void OnConnectClicked()
    {
        if (field != null)
        {
            PlayerProfile.SetLocalNickname(field.text);
        }
    }
}
