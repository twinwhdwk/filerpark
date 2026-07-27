using UnityEngine;

// 로컬 플레이어가 접속 화면에서 입력한 닉네임을 PlayerPrefs에 저장/복원하는 순수
// 로컬 상태. 네트워크와는 무관하다 -- 실제 다른 클라이언트에게 전파하는 건
// PlayerNicknameNGO(Owner 쓰기 권한 NetworkVariable)의 역할이고, 이 클래스는 그
// NetworkVariable에 스폰 시점에 채워 넣을 값을 어디서 가져오는지만 담당한다.
public static class PlayerProfile
{
    private const string PrefsKey = "PlayerNickname";
    private const int MaxLength = 12;

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
        if (trimmed.Length > MaxLength)
        {
            trimmed = trimmed.Substring(0, MaxLength);
        }

        PlayerPrefs.SetString(PrefsKey, trimmed);
        PlayerPrefs.Save();
    }
}
