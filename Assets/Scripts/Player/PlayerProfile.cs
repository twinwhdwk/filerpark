using System.Text;
using UnityEngine;

// 로컬 플레이어가 접속 화면에서 입력한 닉네임을 PlayerPrefs에 저장/복원하는 순수
// 로컬 상태. 네트워크와는 무관하다 -- 실제 다른 클라이언트에게 전파하는 건
// PlayerNicknameNGO(Owner 쓰기 권한 NetworkVariable)의 역할이고, 이 클래스는 그
// NetworkVariable에 스폰 시점에 채워 넣을 값을 어디서 가져오는지만 담당한다.
public static class PlayerProfile
{
    private const string PrefsKey = "PlayerNickname";

    // PlayerNicknameNGO가 쓰는 FixedString32Bytes의 실제 용량은 "문자 32개"가 아니라
    // "UTF-8 29바이트"다 (Unity.Collections.FixedString32Bytes.utf8MaxLengthInBytes).
    // 한글은 UTF-8에서 글자당 3바이트라, 글자 수만 세서 자르면 한글 10자(30바이트)에서
    // 이 한도를 넘어 NetworkVariable 대입이 ArgumentException을 던진다 -- 그것도 NGO의
    // OnNetworkSpawn 호출 루프 안이라 그 한 예외가 같은 오브젝트의 나머지 컴포넌트
    // (BotController, PlayerLabelNGO)의 OnNetworkSpawn까지 통째로 건너뛴다. 여유를 두고
    // 28바이트를 상한으로 잡는다.
    public const int MaxBytes = 28;

    // 접속 화면 InputField.characterLimit에 그대로 쓰는 "글자 수" 상한 -- 최악의 경우
    // (전부 한글, 글자당 3바이트)를 기준으로 잡아야 언어와 무관하게 항상 MaxBytes 안에
    // 들어온다.
    public const int MaxCharsWorstCase = MaxBytes / 3;

    // 봇 프로세스(-bot)는 사람이 입력할 화면 자체가 없으므로 항상 빈 문자열 --
    // 빈 문자열을 받은 쪽(PlayerLabelNGO)이 "P1"/"P2" 기존 폴백으로 표시한다.
    public static string GetLocalNickname()
    {
        if (BotProcess.IsBot) return string.Empty;
        return PlayerPrefs.GetString(PrefsKey, string.Empty);
    }

    public static void SetLocalNickname(string nickname)
    {
        string trimmed = (nickname ?? string.Empty).Trim();
        while (Encoding.UTF8.GetByteCount(trimmed) > MaxBytes && trimmed.Length > 0)
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
        }

        PlayerPrefs.SetString(PrefsKey, trimmed);
        PlayerPrefs.Save();
    }
}
