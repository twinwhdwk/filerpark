using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// Bootstrap 씬에 배치 -- Bootstrap은 절대 언로드되지 않으므로 Lobby/Stage 어디서든
// ESC로 열 수 있는 유일한 오버레이가 된다. 온라인 협동 게임이라 Time.timeScale을
// 0으로 만들지 않는다 -- 서버 권위 물리는 서버 시간 기준으로 계속 흐르고, 클라이언트가
// 로컬로 시간을 멈추면 같은 화면을 보는 다른 플레이어와 어긋나기만 한다. 그래서 이
// "일시정지"는 실제 게임 진행은 그대로 둔 채 메뉴만 띄우는, 온라인 협동 게임 대부분이
// 쓰는 방식이다.
public class PauseMenuUI : MonoBehaviour
{
    [Header("패널")]
    public GameObject pausePanel;
    public GameObject settingsPanel;
    public GameObject quitConfirmPanel;

    [Header("설정 슬라이더")]
    public Slider masterSlider;
    public Slider musicSlider;
    public Slider sfxSlider;

    private bool settingsOpen;
    private bool quitConfirmOpen;

    private void OnEnable()
    {
        SetPauseActive(false);
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        if (quitConfirmOpen)
        {
            OnQuitCancelled();
        }
        else if (settingsOpen)
        {
            CloseSettings();
        }
        else
        {
            TogglePause();
        }
    }

    public void TogglePause()
    {
        bool willOpen = pausePanel != null && !pausePanel.activeSelf;
        SetPauseActive(willOpen);
    }

    private void SetPauseActive(bool active)
    {
        if (pausePanel != null) pausePanel.SetActive(active);
        if (!active)
        {
            CloseSettingsImmediate();
            CloseQuitConfirmImmediate();
        }
    }

    public void OnResumeClicked()
    {
        PlayClick();
        SetPauseActive(false);
    }

    public void OnSettingsClicked()
    {
        PlayClick();
        settingsOpen = true;
        if (settingsPanel != null) settingsPanel.SetActive(true);

        if (AudioManager.Instance != null)
        {
            if (masterSlider != null) masterSlider.SetValueWithoutNotify(AudioManager.Instance.MasterVolume);
            if (musicSlider != null) musicSlider.SetValueWithoutNotify(AudioManager.Instance.MusicVolume);
            if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(AudioManager.Instance.SfxVolume);
        }
    }

    // 접속 화면의 "설정" 버튼용 진입점 -- settingsPanel이 pausePanel의 자식이라
    // pausePanel부터 활성화해야 보인다. ESC로 들어왔을 때와 똑같이 Resume/Settings/
    // Quit 카드가 뒤에 깔린 채로 열리지만(OnSettingsClicked을 그대로 재사용), 설정
    // 패널이 같은 크기로 완전히 덮으면서 나중에 그려지므로 시각적으로도 입력
    // 처리로도 동일하게 동작한다 -- ESC 경로에서 이미 검증된 것과 같은 상태다.
    public void OpenSettingsDirectly()
    {
        if (pausePanel != null) pausePanel.SetActive(true);
        OnSettingsClicked();
    }

    public void CloseSettings()
    {
        PlayClick();
        CloseSettingsImmediate();
    }

    private void CloseSettingsImmediate()
    {
        settingsOpen = false;
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    public void OnMasterVolumeChanged(float value) => AudioManager.Instance?.SetMasterVolume(value);
    public void OnMusicVolumeChanged(float value) => AudioManager.Instance?.SetMusicVolume(value);
    public void OnSfxVolumeChanged(float value) => AudioManager.Instance?.SetSfxVolume(value);

    // 협동 게임이라 다른 플레이어가 아직 하고 있는 도중에 실수로 ESC -> 종료를
    // 연달아 눌러 세션을 끊어버리는 사고가 나기 쉽다 -- 바로 종료하지 않고 확인
    // 패널을 한 번 더 거친다.
    public void OnQuitClicked()
    {
        PlayClick();
        quitConfirmOpen = true;
        if (quitConfirmPanel != null) quitConfirmPanel.SetActive(true);
    }

    public void OnQuitCancelled()
    {
        PlayClick();
        CloseQuitConfirmImmediate();
    }

    private void CloseQuitConfirmImmediate()
    {
        quitConfirmOpen = false;
        if (quitConfirmPanel != null) quitConfirmPanel.SetActive(false);
    }

    public void OnQuitConfirmed()
    {
        PlayClick();
        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
        {
            NetworkManager.Singleton.Shutdown();
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void PlayClick() => AudioManager.Instance?.PlaySfx(SfxId.UIClick);
}
