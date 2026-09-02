using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ObjektRT.Core.Attributes;

namespace ObjektRT.HostBindings.NativeAudio;

/// <summary>
/// A custom C# host binding demonstrating zero-copy native interop with a
/// Contract <c>ManagedPtr</c>.
///
/// The Contract side holds a <c>ManagedPtr&lt;int&gt;</c> whose storage is a
/// native (HGlobal) buffer. <c>&amp;m</c> lowers to <c>m.Address()</c>, which
/// returns the buffer's real native address as a <c>long</c>. That address is
/// handed across the dispatch boundary as a plain <c>long</c>; here it is
/// re-created as an <c>IntPtr</c> and spanned as a <c>float</c> stream — the
/// same memory, no copying. (A 4-byte <c>int</c> element is bit-identical to
/// a 4-byte <c>float</c>, so a <c>ManagedPtr&lt;int&gt;</c> is a legal
/// zero-copy <c>float[]</c> buffer.)
///
/// Lifetime stays on the Contract side: the host must not touch the address
/// after the Contract calls <c>Free()</c>.
/// </summary>
[ClassBinding("NativeAudio")]
public static class NativeAudioHost
{
    /// <summary>Returns the raw pointer-sized address as a long — the same
    /// thing <c>&amp;m</c> already gives, expressed as an IntPtr for clarity.</summary>
    public static long EchoAddress(long address) => address;

    /// <summary>
    /// Views the buffer as a <c>Span&lt;byte&gt;</c> from its native address and
    /// returns its first few bytes so we can see the same memory both sides.
    /// </summary>
    public static unsafe long PeekBytes(long address, int length, int at)
    {
        var span = new Span<byte>((void*)new IntPtr(address).ToPointer(), length);
        return span[at];
    }

    /// <summary>
    /// Zero-copy audio "mixer": treat the block as a <c>float</c> stream and
    /// apply a per-sample gain in place. Returns the peak value afterwards so
    /// the Contract side can confirm the memory was mutated through the
    /// host. No marshalling of array content — only the native address moves.
    /// </summary>
    public static unsafe float ApplyGain(long address, int elementCount, float gain)
    {
        var bytes = new Span<byte>((void*)new IntPtr(address).ToPointer(), elementCount * sizeof(float));
        var samples = MemoryMarshal.Cast<byte, float>(bytes);
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= gain;                 // write to the SAME native memory
            float v = MathF.Abs(samples[i]);
            if (v > peak) peak = v;
        }
        return peak;
    }

    /// <summary>
    /// Zero-copy feature extraction: reduce the block to an RMS value by
    /// reading the native pointer directly. Classic audio/analysis op.
    /// </summary>
    public static unsafe double Rms(long address, int elementCount)
    {
        var bytes = new Span<byte>((void*)new IntPtr(address).ToPointer(), elementCount * sizeof(float));
        var samples = MemoryMarshal.Cast<byte, float>(bytes);
        double sum = 0;
        foreach (var s in samples) sum += (double)s * s;
        return Math.Sqrt(sum / Math.Max(1, samples.Length));
    }

    /// <summary>
    /// Zero-copy write of a generated waveform straight into the Contract
    /// buffer. Demonstrates the host producing data the .ct side then reads.
    /// </summary>
    public static unsafe double FillSine(long address, int elementCount, double frequency, double sampleRate)
    {
        var bytes = new Span<byte>((void*)new IntPtr(address).ToPointer(), elementCount * sizeof(float));
        var samples = MemoryMarshal.Cast<byte, float>(bytes);
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate);
        return Rms(address, elementCount);
    }
}