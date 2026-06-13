using UnityEngine;

/// <summary>
/// 入店・退店時のチャイム音（実行時に波形を合成。音声アセット不要）。
/// 入店: 「ピンポーン」と上昇する2音（お店のドアチャイム風）
/// 退店: ゆるやかに下降する3音（おつかれさま感）
/// ベル風の音色（基音＋倍音＋減衰エンベロープ）で生成する。
/// </summary>
public static class VisitChime
{
    private const int SAMPLE_RATE = 44100;

    private static AudioClip _checkInClip;
    private static AudioClip _checkOutClip;
    private static AudioSource _source;

    /// <summary>入店チャイムを鳴らす</summary>
    public static void PlayCheckIn()
    {
        if (_checkInClip == null)
        {
            // ピンポーン（E5 → A5 上昇）
            _checkInClip = CreateClip("se_checkin", new (float freq, float start, float dur)[]
            {
                (659.3f, 0.00f, 0.20f), // E5
                (880.0f, 0.18f, 0.60f), // A5
            });
        }
        Play(_checkInClip);
    }

    /// <summary>退店チャイムを鳴らす</summary>
    public static void PlayCheckOut()
    {
        if (_checkOutClip == null)
        {
            // ポン・ポン・ポーン（G5 → E5 → C5 下降）
            _checkOutClip = CreateClip("se_checkout", new (float freq, float start, float dur)[]
            {
                (784.0f, 0.00f, 0.18f), // G5
                (659.3f, 0.16f, 0.20f), // E5
                (523.3f, 0.32f, 0.70f), // C5
            });
        }
        Play(_checkOutClip);
    }

    private static void Play(AudioClip clip)
    {
        if (_source == null)
        {
            var go = new GameObject("__VisitChime");
            Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f; // 2D再生
        }
        _source.PlayOneShot(clip, 0.9f);
    }

    /// <summary>
    /// ベル風の音色でノート列を1つのクリップに合成する。
    /// 各ノート: 基音＋2・3倍音（倍音ほど早く減衰）、5msアタック＋指数減衰。
    /// </summary>
    private static AudioClip CreateClip(string name, (float freq, float start, float dur)[] notes)
    {
        float total = 0f;
        foreach (var n in notes) total = Mathf.Max(total, n.start + n.dur);
        int length = (int)((total + 0.05f) * SAMPLE_RATE);
        var data = new float[length];

        foreach (var n in notes)
        {
            int offset = (int)(n.start * SAMPLE_RATE);
            int count = (int)(n.dur * SAMPLE_RATE);
            for (int i = 0; i < count && offset + i < length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float attack = Mathf.Min(1f, t / 0.005f); // クリックノイズ防止
                float env = attack * Mathf.Exp(-3.5f * t / n.dur);

                float w = Mathf.Sin(2f * Mathf.PI * n.freq * t)
                        + 0.35f * Mathf.Sin(4f * Mathf.PI * n.freq * t) * Mathf.Exp(-6f * t / n.dur)
                        + 0.12f * Mathf.Sin(6f * Mathf.PI * n.freq * t) * Mathf.Exp(-9f * t / n.dur);

                data[offset + i] += w * env * 0.5f;
            }
        }

        for (int i = 0; i < length; i++)
            data[i] = Mathf.Clamp(data[i], -1f, 1f);

        var clip = AudioClip.Create(name, length, 1, SAMPLE_RATE, false);
        clip.SetData(data, 0);
        return clip;
    }
}
