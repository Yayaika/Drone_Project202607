using UnityEngine;

/// <summary>
/// 程式合成的 placeholder 音效 (正弦/方波音調、滑音、噪音)，在 Resources/Audio 缺檔時使用。
/// 只是暫代品：放入正式音效檔後，GameAudio 會自動優先使用正式檔案。
/// </summary>
public static class SynthSfx
{
    private const int SampleRate = 44100;

    private enum Wave
    {
        Sine,
        Square,
        Triangle,
        Noise
    }

    /// <summary>一段音：起始/結束頻率、長度、音量、波形</summary>
    private struct Note
    {
        public float From;
        public float To;
        public float Duration;
        public float Volume;
        public Wave Wave;

        public Note(float from, float to, float duration, float volume = 0.6f, Wave wave = Wave.Sine)
        {
            From = from;
            To = to;
            Duration = duration;
            Volume = volume;
            Wave = wave;
        }
    }

    public static AudioClip Create(GameSfx sfx)
    {
        Note[] notes = sfx switch
        {
            GameSfx.UIHover => new[] { new Note(1900f, 2100f, 0.035f, 0.22f) },
            GameSfx.UIClick => new[] { new Note(1300f, 900f, 0.06f, 0.5f, Wave.Triangle) },
            GameSfx.PanelOpen => new[] { new Note(420f, 900f, 0.16f, 0.35f, Wave.Triangle) },
            GameSfx.CountdownBeep => new[] { new Note(880f, 880f, 0.16f, 0.5f) },
            GameSfx.CountdownGo => new[] { new Note(1320f, 1320f, 0.45f, 0.55f) },
            GameSfx.Checkpoint => new[] { new Note(988f, 988f, 0.09f, 0.5f), new Note(1319f, 1319f, 0.16f, 0.5f) },
            GameSfx.Finish => new[]
            {
                new Note(523f, 523f, 0.12f), new Note(659f, 659f, 0.12f),
                new Note(784f, 784f, 0.12f), new Note(1047f, 1047f, 0.4f)
            },
            GameSfx.NewRecord => new[]
            {
                new Note(784f, 784f, 0.08f), new Note(988f, 988f, 0.08f),
                new Note(1175f, 1175f, 0.08f), new Note(1568f, 1568f, 0.35f)
            },
            GameSfx.CargoPickup => new[] { new Note(300f, 650f, 0.12f, 0.4f, Wave.Square) },
            GameSfx.CargoDrop => new[] { new Note(180f, 70f, 0.16f, 0.6f, Wave.Triangle), new Note(0f, 0f, 0.05f, 0.25f, Wave.Noise) },
            GameSfx.Delivered => new[] { new Note(784f, 784f, 0.1f, 0.5f), new Note(1047f, 1047f, 0.1f, 0.5f), new Note(1568f, 1568f, 0.25f, 0.45f) },
            GameSfx.CargoBroken => new[] { new Note(0f, 0f, 0.35f, 0.55f, Wave.Noise) },
            GameSfx.Crash => new[] { new Note(0f, 0f, 0.22f, 0.6f, Wave.Noise), new Note(120f, 50f, 0.12f, 0.5f, Wave.Triangle) },
            GameSfx.Warning => new[] { new Note(1000f, 1000f, 0.1f, 0.35f, Wave.Square), new Note(0f, 0f, 0.06f, 0f), new Note(1000f, 1000f, 0.1f, 0.35f, Wave.Square) },
            GameSfx.DialoguePop => new[] { new Note(520f, 1040f, 0.05f, 0.4f, Wave.Triangle), new Note(1320f, 1560f, 0.07f, 0.3f) },
            _ => new[] { new Note(880f, 880f, 0.1f) }
        };

        return Render("Synth_" + sfx, notes);
    }

    private static AudioClip Render(string clipName, Note[] notes)
    {
        float totalDuration = 0f;
        foreach (Note note in notes) totalDuration += note.Duration;

        int totalSamples = Mathf.Max(1, Mathf.CeilToInt(totalDuration * SampleRate));
        float[] data = new float[totalSamples];
        System.Random random = new System.Random(clipName.GetHashCode());

        int offset = 0;
        foreach (Note note in notes)
        {
            int length = Mathf.CeilToInt(note.Duration * SampleRate);
            double phase = 0.0;
            float lowPass = 0f;
            for (int i = 0; i < length && offset + i < totalSamples; i++)
            {
                float t = (float)i / length;
                float frequency = Mathf.Lerp(note.From, note.To, t);
                phase += frequency / SampleRate;

                float sample;
                switch (note.Wave)
                {
                    case Wave.Square:
                        sample = (phase % 1.0) < 0.5 ? 0.6f : -0.6f;
                        break;
                    case Wave.Triangle:
                        sample = (float)(4.0 * System.Math.Abs((phase % 1.0) - 0.5) - 1.0);
                        break;
                    case Wave.Noise:
                        // 簡單低通讓噪音聽起來像撞擊而非嘶聲
                        lowPass = Mathf.Lerp(lowPass, (float)(random.NextDouble() * 2.0 - 1.0), 0.25f);
                        sample = lowPass * 1.6f;
                        break;
                    default:
                        sample = Mathf.Sin((float)(phase * 2.0 * System.Math.PI));
                        break;
                }

                // 快速起音 + 指數衰減，避免爆音
                float attack = Mathf.Clamp01(i / (SampleRate * 0.004f));
                float release = Mathf.Pow(1f - t, 1.6f);
                data[offset + i] += sample * note.Volume * attack * release;
            }
            offset += length;
        }

        AudioClip clip = AudioClip.Create(clipName, totalSamples, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
