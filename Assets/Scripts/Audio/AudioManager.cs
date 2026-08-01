using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 코옵 기믹에서 재생할 효과음 종류. 새 효과음이 필요하면 여기 값 하나를 추가하고
// AudioManager.BuildSfxClips()에 합성 방법(또는 나중에는 실제 에셋 로드)만 추가하면 된다.
public enum SfxId
{
    Jump,
    DoorOpen,
    DoorClose,
    KeyPickup,
    KeyDrop,
    BlockArrive,
    StageClear,
    StageFail,
    UIClick,
}

// 게임 전역 오디오 싱글턴. GameFlowManager와 마찬가지로 Bootstrap 씬에 배치되고
// (Bootstrap은 절대 언로드되지 않으므로) Lobby/Stage를 오가도 그대로 유지된다.
// 순수 로컬 프레젠테이션 레이어라 일부러 NetworkBehaviour로 만들지 않았다 -- 코옵
// 기믹의 NetworkVariable.OnValueChanged 콜백은 이미 서버/클라이언트 모두에서 동일한
// 값 전이 시점에 실행되므로, 그 콜백 안에서 이 매니저를 부르기만 하면 별도의 RPC
// 없이 모든 클라이언트가 같은 타이밍에 소리를 재생한다. 서버만 알 수 있는 판정
// 결과(예: 점프 성공 여부)처럼 NetworkVariable로 안 드러나는 경우만 명시적으로
// ClientRpc를 통해 브로드캐스트한다 (PlayerMovementNGO/RisingHazardNGO 참고).
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private const string PrefMaster = "audio_master_volume";
    private const string PrefMusic = "audio_music_volume";
    private const string PrefSfx = "audio_sfx_volume";

    public float MasterVolume { get; private set; } = 1f;
    public float MusicVolume { get; private set; } = 0.5f;
    public float SfxVolume { get; private set; } = 0.8f;

    private AudioSource sfxSource;
    private AudioSource musicSource;
    private readonly Dictionary<SfxId, AudioClip> sfxClips = new Dictionary<SfxId, AudioClip>();

    private AudioClip lobbyMusicClip;
    private AudioClip stageMusicClip;
    private Coroutine musicFadeRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

#if UNITY_SERVER
        // 데디케이티드 서버는 소리를 들을 사람이 없다. AudioClip.Create()로 만든
        // 클립에 SetData()를 호출하면, 오디오 디바이스가 없는 헤드리스 서버
        // 빌드에서는 매번 실패해 "AudioClip.SetData failed; AudioClip contains no
        // data" 경고만 시작할 때마다 스팸으로 남긴다(실측: 실제 GCP 서버 로그에서
        // 합성 클립 개수만큼 반복 확인). sfxSource/musicSource가 null로 남으므로
        // PlaySfx/PlayLobbyMusic/PlayStageMusic은 이미 있는 null 체크로 조용히
        // 아무 일도 하지 않는다 -- 서버에서 이 메서드들을 부르는 코드도 없다
        // (전부 클라이언트 전용 UI/프레젠테이션 스크립트에서만 호출됨).
        return;
#else
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;

        LoadVolumePrefs();
        BuildSfxClips();
        BuildMusicClips();
#endif
    }

    private void LoadVolumePrefs()
    {
        MasterVolume = PlayerPrefs.GetFloat(PrefMaster, 1f);
        // 배경음악 기본값은 꺼둔다(요청: 효과음은 괜찮지만 배경음이 거슬림) -- 필요하면
        // 설정 패널의 슬라이더로 언제든 직접 올릴 수 있다. 효과음 기본값은 그대로.
        MusicVolume = PlayerPrefs.GetFloat(PrefMusic, 0f);
        SfxVolume = PlayerPrefs.GetFloat(PrefSfx, 0.8f);
    }

    private void BuildSfxClips()
    {
        sfxClips[SfxId.Jump] = ProceduralSfx.CreateTone("sfx_jump", 420f, 720f, 0.12f, ProceduralSfx.WaveShape.Sine, 0.5f);
        sfxClips[SfxId.DoorOpen] = ProceduralSfx.CreateTone("sfx_door_open", 300f, 520f, 0.22f, ProceduralSfx.WaveShape.Triangle, 0.45f);
        sfxClips[SfxId.DoorClose] = ProceduralSfx.CreateTone("sfx_door_close", 520f, 260f, 0.18f, ProceduralSfx.WaveShape.Triangle, 0.4f);
        sfxClips[SfxId.KeyPickup] = ProceduralSfx.CreateChime("sfx_key_pickup", new[] { 660f, 880f }, 0.08f, 0.45f);
        sfxClips[SfxId.KeyDrop] = ProceduralSfx.CreateTone("sfx_key_drop", 440f, 220f, 0.12f, ProceduralSfx.WaveShape.Sine, 0.4f);
        sfxClips[SfxId.BlockArrive] = ProceduralSfx.CreateTone("sfx_block_arrive", 180f, 140f, 0.25f, ProceduralSfx.WaveShape.Square, 0.35f);
        sfxClips[SfxId.StageClear] = ProceduralSfx.CreateChime("sfx_stage_clear", new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.14f, 0.55f);
        sfxClips[SfxId.StageFail] = ProceduralSfx.CreateChime("sfx_stage_fail", new[] { 392f, 329.6f, 261.6f }, 0.18f, 0.5f);
        sfxClips[SfxId.UIClick] = ProceduralSfx.CreateTone("sfx_ui_click", 900f, 900f, 0.04f, ProceduralSfx.WaveShape.Square, 0.3f);
    }

    private void BuildMusicClips()
    {
        lobbyMusicClip = ProceduralSfx.CreateAmbientLoop("music_lobby", new[] { 261.6f, 329.6f, 392f, 329.6f }, 0.9f, 0.18f);
        stageMusicClip = ProceduralSfx.CreateAmbientLoop("music_stage", new[] { 293.7f, 349.2f, 440f, 349.2f }, 0.8f, 0.16f);
    }

    public void PlaySfx(SfxId id)
    {
        if (sfxSource == null || !sfxClips.TryGetValue(id, out AudioClip clip) || clip == null) return;
        sfxSource.PlayOneShot(clip, SfxVolume * MasterVolume);
    }

    public void PlayLobbyMusic() => CrossfadeMusic(lobbyMusicClip);
    public void PlayStageMusic() => CrossfadeMusic(stageMusicClip);

    private void CrossfadeMusic(AudioClip clip)
    {
        if (musicSource == null || clip == null || musicSource.clip == clip) return;
        if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);
        musicFadeRoutine = StartCoroutine(CrossfadeRoutine(clip));
    }

    // targetVolume은 매 프레임 MusicVolume/MasterVolume에서 다시 계산한다 -- 한 번만
    // 캡처해두면, 페이드가 도는 동안(씬 전환 시 흔히 발생) 슬라이더를 움직여도 그
    // 조작이 무시되고 코루틴이 끝난 뒤 페이드 시작 시점의 낡은 값으로 덮어써진다.
    private IEnumerator CrossfadeRoutine(AudioClip nextClip)
    {
        const float fadeTime = 0.6f;

        float t = 0f;
        float startVolume = musicSource.volume;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(startVolume, 0f, t / fadeTime);
            yield return null;
        }

        musicSource.clip = nextClip;
        musicSource.Play();

        t = 0f;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(0f, MusicVolume * MasterVolume, t / fadeTime);
            yield return null;
        }
        musicSource.volume = MusicVolume * MasterVolume;
        musicFadeRoutine = null;
    }

    // 세 세터 모두 PlayerPrefs.Save()를 즉시 호출한다 -- Unity의 자동 저장(주기적/종료 시)에
    // 맡기면, 이 프로젝트에서 이미 실측된 "하드 프로세스 킬" 시나리오(봇 클라이언트 강제
    // 종료 테스트 등, CLAUDE.md 참고)와 똑같이 사람도 슬라이더만 만지고 정상 종료 없이
    // 창을 닫아버릴 수 있어, 그사이 볼륨 설정이 저장되지 않고 날아갈 수 있다.
    // StageClearUI의 최고 기록 저장과 동일한 패턴으로 맞춘다.
    public void SetMasterVolume(float value)
    {
        MasterVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(PrefMaster, MasterVolume);
        PlayerPrefs.Save();
        ApplyMusicVolume();
    }

    public void SetMusicVolume(float value)
    {
        MusicVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(PrefMusic, MusicVolume);
        PlayerPrefs.Save();
        ApplyMusicVolume();
    }

    public void SetSfxVolume(float value)
    {
        SfxVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(PrefSfx, SfxVolume);
        PlayerPrefs.Save();
    }

    // 페이드가 진행 중일 때는 건드리지 않는다 -- 슬라이더 조작 도중 크로스페이드가
    // 끝나면 그 목표 볼륨은 CrossfadeRoutine이 이미 최신 MasterVolume/MusicVolume
    // 기준으로 다시 계산해서 덮어쓴다.
    private void ApplyMusicVolume()
    {
        if (musicSource == null || musicFadeRoutine != null) return;
        musicSource.volume = MusicVolume * MasterVolume;
    }
}
