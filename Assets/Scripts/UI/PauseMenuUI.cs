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

    [Header("설정 슬라이더")]
    public Slider masterSlider;
    public Slider musicSlider;
    public Slider sfxSlider;

    private bool settingsOpen;

    private void OnEnable()
    {
        SetPauseActive(false);
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        if (settingsOpen)
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
        if (!active) CloseSettingsImmediate();
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

    public void OnQuitClicked()
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
