using UnityEngine;

// 실제 사운드 에셋을 아직 마련하지 못한 상태에서도 "무음보다는 훨씬 낫다"는
// 목표로, 순수 코드로 짧은 효과음/앰비언트 루프를 합성한다. AudioClip.Create로
// 샘플 버퍼를 직접 채우는 방식이라 외부 파일이 전혀 필요 없다 -- 나중에 실제
// 오디오 에셋으로 교체할 때는 AudioManager의 BuildSfxClips/BuildMusicClips만
// AssetDatabase 로드로 바꾸면 된다.
public static class ProceduralSfx
{
    public enum WaveShape { Sine, Square, Triangle }

    private const int SampleRate = 44100;

    public static AudioClip CreateTone(string name, float startFreq, float endFreq, float duration, WaveShape shape, float volume)
    {
        int sampleCount = Mathf.Max(1, Mathf.RoundToInt(duration * SampleRate));
        float[] samples = new float[sampleCount];
        FillTone(samples, 0, sampleCount, startFreq, endFreq, shape, volume);

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // 여러 음을 순서대로 이어붙인 짧은 아르페지오(열쇠 습득/스테이지 클리어 등에 사용).
    public static AudioClip CreateChime(string name, float[] noteFrequencies, float noteDuration, float volume)
    {
        int perNote = Mathf.Max(1, Mathf.RoundToInt(noteDuration * SampleRate));
        int total = perNote * noteFrequencies.Length;
        float[] samples = new float[total];

        for (int i = 0; i < noteFrequencies.Length; i++)
        {
            FillTone(samples, i * perNote, perNote, noteFrequencies[i], noteFrequencies[i], WaveShape.Sine, volume);
        }

        AudioClip clip = AudioClip.Create(name, total, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // 몇 개 음을 반복 루프로 이어붙인 잔잔한 배경음. 진짜 작곡이 아니라 "완전한
    // 무음보다는 낫다"는 자리채움용이라 화음/리듬은 최대한 단순하게 잡았다 -- 나중에
    // 실제 BGM 트랙으로 교체하기 전까지의 자리표시자.
    public static AudioClip CreateAmbientLoop(string name, float[] noteFrequencies, float noteDuration, float volume)
    {
        int perNote = Mathf.Max(1, Mathf.RoundToInt(noteDuration * SampleRate));
        int total = perNote * noteFrequencies.Length;
        float[] samples = new float[total];

        for (int i = 0; i < noteFrequencies.Length; i++)
        {
            FillTone(samples, i * perNote, perNote, noteFrequencies[i], noteFrequencies[i], WaveShape.Sine, volume);
        }

        AudioClip clip = AudioClip.Create(name, total, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // 시작/끝에 짧은 선형 페이드(attack/release)를 걸어 샘플 경계에서 나는 디지털
    // 클릭 잡음을 없앤다 -- 합성 사운드라도 순수 파형을 그냥 잘라 붙이면 이 클릭
    // 잡음이 가장 먼저 귀에 걸린다.
    private static void FillTone(float[] buffer, int offset, int count, float startFreq, float endFreq, WaveShape shape, float volume)
    {
        float phase = 0f;
        int attackSamples = Mathf.Min(count / 4, Mathf.RoundToInt(SampleRate * 0.005f));
        int releaseSamples = Mathf.Min(count / 4, Mathf.RoundToInt(SampleRate * 0.03f));

        for (int i = 0; i < count; i++)
        {
            float t = count <= 1 ? 0f : (float)i / (count - 1);
            float freq = Mathf.Lerp(startFreq, endFreq, t);
            phase += freq / SampleRate;
            if (phase > 1f) phase -= 1f;

            float raw;
            switch (shape)
            {
                case WaveShape.Square:
                    raw = Mathf.Sign(Mathf.Sin(phase * Mathf.PI * 2f));
                    break;
                case WaveShape.Triangle:
                    raw = 2f * Mathf.Abs(2f * (phase - Mathf.Floor(phase + 0.5f))) - 1f;
                    break;
                default:
                    raw = Mathf.Sin(phase * Mathf.PI * 2f);
                    break;
            }

            float envelope = 1f;
            if (i < attackSamples) envelope = (float)i / attackSamples;
            else if (i > count - releaseSamples) envelope = (float)(count - i) / releaseSamples;

            buffer[offset + i] = raw * volume * envelope;
        }
    }
}
