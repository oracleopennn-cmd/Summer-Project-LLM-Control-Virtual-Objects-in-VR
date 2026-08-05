using System;
using System.IO;
using UnityEngine;

public static class WavUtility
{
    private const int HEADER_SIZE = 44;

    // 新增 recordedPosition 参数，用来指定实际录制了多长
    public static byte[] FromAudioClip(AudioClip clip, int recordedPosition = 0)
    {
        using (var stream = new MemoryStream())
        {
            var writer = new BinaryWriter(stream);

            // 核心修改：动态计算有效采样点。如果传入了有效位置，就按有效位置截断；否则用最大长度
            int validSamples = (recordedPosition > 0 && recordedPosition <= clip.samples) ? recordedPosition : clip.samples;

            var samples = new float[validSamples * clip.channels];
            // 只从 0 提取到有效位置的数据，抛弃后面的静音
            clip.GetData(samples, 0);

            // WAV Header
            writer.Write(new char[4] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + samples.Length * 2);
            writer.Write(new char[4] { 'W', 'A', 'V', 'E' });
            writer.Write(new char[4] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)clip.channels);
            writer.Write(clip.frequency);
            writer.Write(clip.frequency * clip.channels * 2);
            writer.Write((short)(clip.channels * 2));
            writer.Write((short)16);
            writer.Write(new char[4] { 'd', 'a', 't', 'a' });
            writer.Write(samples.Length * 2);

            // Convert Float samples to 16-bit PCM
            foreach (var sample in samples)
            {
                short sampleAsShort = (short)(Mathf.Clamp(sample, -1f, 1f) * 32767);
                writer.Write(sampleAsShort);
            }

            return stream.ToArray();
        }
    }
}