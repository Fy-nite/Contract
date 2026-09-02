using System;
using System.Runtime.InteropServices;
using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.Memory;

/// <summary>
/// A self-contained managed, bound-checked buffer used by <c>ManagedPtr&lt;T&gt;</c>.
/// Lives inside the binding so the stdlib needs no dependency on the VM. A
/// <c>PtrBuffer</c> is kept alive as an opaque object handle on the Contract side
/// (the same mechanism as <c>System.DateTime</c> wrappers); its native storage is
/// explicit and must be released via <see cref="PtrHost.Free"/>.
/// </summary>
public sealed class PtrBuffer
{
    private IntPtr _data;
    private readonly int _count;
    private readonly int _elementSize;
    private bool _freed;

    internal PtrBuffer(IntPtr data, int count, int elementSize)
    {
        _data = data;
        _count = count;
        _elementSize = elementSize;
    }

    public IntPtr Address => _data;
    public int Count => _count;
    public int ElementSize => _elementSize;
    public long ByteLength => (long)_count * _elementSize;
    public bool IsFreed => _freed;

    public void Free()
    {
        if (_freed) return;
        if (_data != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_data);
            _data = IntPtr.Zero;
        }
        _freed = true;
    }

    public void ValidateRange(long byteOffset, int size)
    {
        if (_freed)
            throw new InvalidOperationException("PtrBuffer has been freed");
        if (_data == IntPtr.Zero)
            throw new InvalidOperationException("PtrBuffer has no backing storage");
        if (byteOffset < 0 || byteOffset + size > ByteLength)
            throw new IndexOutOfRangeException($"Ptr access out of range at byte {byteOffset}, length {ByteLength}");
    }

    /// <summary>Address of element <paramref name="index"/> (element size from <see cref="ElementSize"/>).</summary>
    public long ElementAddress(int index) => (long)_data + (long)index * _elementSize;
}

/// <summary>
/// A C# host binding (<c>[ClassBinding("ManagedPtr")]</c>) exposing explicit,
/// checked native memory to the Contract language through the <c>host.</c>
/// keyword. The <c>ManagedPtr&lt;T&gt;</c> Contract wrapper auto-shadows this
/// binding (name match) and holds an opaque <see cref="PtrBuffer"/> handle,
/// delegating all operations here.
/// </summary>
[ClassBinding("ManagedPtr")]
public static class PtrHost
{
    private static PtrBuffer Unwrap(object handle) => handle as PtrBuffer
        ?? throw new InvalidOperationException("PtrHost: handle is not a PtrBuffer");

    /// <summary>
    /// Allocates a zeroed buffer of <paramref name="count"/> elements each
    /// <paramref name="size"/> bytes. Returns an opaque handle.
    /// </summary>
    public static object Alloc(int count, int size)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 0");
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size), "size must be > 0");
        long total = count == 0 ? 1L : (long)count * size;
        IntPtr p = Marshal.AllocHGlobal((IntPtr)total);
        byte[] zeros = new byte[(int)total];
        Marshal.Copy(zeros, 0, p, (int)total);
        return new PtrBuffer(p, count, size);
    }

    /// <summary>Releases the buffer. The handle becomes unusable.</summary>
    public static void Free(object handle) => Unwrap(handle).Free();

    /// <summary>Element count of the buffer.</summary>
    public static int Length(object handle) => Unwrap(handle).Count;

    /// <summary>Raw address of the start of the buffer, as an 8-byte integer.</summary>
    public static long Address(object handle) => Unwrap(handle).Address.ToInt64();

    /// <summary>True once the buffer has been freed.</summary>
    public static bool IsFreed(object handle) => Unwrap(handle).IsFreed;

    // ── Typed element read/write (bounds-checked) ───────────────────

    private static PtrBuffer AtElement(object handle, int index, int size)
    {
        var b = Unwrap(handle);
        b.ValidateRange((long)index * b.ElementSize, size);
        return b;
    }

    public static int ReadI4(object handle, int index)
    {
        var b = AtElement(handle, index, sizeof(int));
        return Marshal.ReadInt32(b.Address, index * b.ElementSize);
    }

    public static void WriteI4(object handle, int index, int value)
    {
        var b = AtElement(handle, index, sizeof(int));
        Marshal.WriteInt32(b.Address, index * b.ElementSize, value);
    }

    public static long ReadI8(object handle, int index)
    {
        var b = AtElement(handle, index, sizeof(long));
        return Marshal.ReadInt64(b.Address, index * b.ElementSize);
    }

    public static void WriteI8(object handle, int index, long value)
    {
        var b = AtElement(handle, index, sizeof(long));
        Marshal.WriteInt64(b.Address, index * b.ElementSize, value);
    }

    public static float ReadR4(object handle, int index)
    {
        var b = AtElement(handle, index, sizeof(float));
        return BitConverter.Int32BitsToSingle(Marshal.ReadInt32(b.Address, index * b.ElementSize));
    }

    public static void WriteR4(object handle, int index, float value)
    {
        var b = AtElement(handle, index, sizeof(float));
        Marshal.WriteInt32(b.Address, index * b.ElementSize, BitConverter.SingleToInt32Bits(value));
    }

    public static double ReadR8(object handle, int index)
    {
        var b = AtElement(handle, index, sizeof(double));
        return BitConverter.Int64BitsToDouble(Marshal.ReadInt64(b.Address, index * b.ElementSize));
    }

    public static void WriteR8(object handle, int index, double value)
    {
        var b = AtElement(handle, index, sizeof(double));
        Marshal.WriteInt64(b.Address, index * b.ElementSize, BitConverter.DoubleToInt64Bits(value));
    }
}
