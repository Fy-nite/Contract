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
/// The element kind of a native block. Distinguishes 4-byte int (I4) from
/// 4-byte float (R4) which share a size but not an integer/float semantics.
public enum PtrElementKind : byte
{
    I4,
    I8,
    R4,
    R8,
}

public sealed class PtrBuffer
{
    private IntPtr _data;
    private readonly int _count;
    private readonly int _elementSize;
    private readonly PtrElementKind _kind;
    private bool _freed;

    internal PtrBuffer(IntPtr data, int count, int elementSize, PtrElementKind kind)
    {
        _data = data;
        _count = count;
        _elementSize = elementSize;
        _kind = kind;
    }

    public IntPtr Address => _data;
    public int Count => _count;
    public int ElementSize => _elementSize;
    public PtrElementKind Kind => _kind;
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
    /// Allocates a zeroed buffer of <paramref name="count"/> elements whose
    /// size and kind come from the wire type name <paramref name="elemWireName"/>
    /// (e.g. <c>"int32"</c>, <c>"float32"</c>). The generic
    /// <c>ManagedPtr&lt;T&gt;</c> wrapper passes the literal <c>"T"</c>, which
    /// generic materialization rewrites to the concrete wire name (int32,
    /// int64, float32, float64, ...). Returns an opaque handle.
    /// </summary>
    public static object Alloc(int count, string elemWireName)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 0");
        var (size, kind) = MapWireName(elemWireName);
        long total = count == 0 ? 1L : (long)count * size;
        IntPtr p = Marshal.AllocHGlobal((IntPtr)total);
        byte[] zeros = new byte[(int)total];
        Marshal.Copy(zeros, 0, p, (int)total);
        return new PtrBuffer(p, count, size, kind);
    }

    /// <summary>Maps a Contract wire element type name to its native size and kind.</summary>
    private static (int Size, PtrElementKind Kind) MapWireName(string t) => (t ?? "").Trim().ToLowerInvariant() switch
    {
        "int" or "int32" or "uint" or "uint32" or "byte" or "uint8" or "sbyte" or "int8" or "short" or "int16" or "ushort" or "uint16" => (sizeof(int), PtrElementKind.I4),
        "long" or "int64" or "ulong" or "uint64" => (sizeof(long), PtrElementKind.I8),
        "float" or "float32" => (sizeof(float), PtrElementKind.R4),
        "double" or "float64" => (sizeof(double), PtrElementKind.R8),
        "bool" => (1, PtrElementKind.I4),
        _ => throw new ArgumentException($"PtrHost: unsupported element type '{t}'", nameof(t)),
    };

    /// <summary>Element size (bytes) for a kind.</summary>
    private static int SizeOfKind(PtrElementKind kind) => kind switch
    {
        PtrElementKind.I4 => 4,
        PtrElementKind.I8 => 8,
        PtrElementKind.R4 => 4,
        PtrElementKind.R8 => 8,
        _ => 4,
    };

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

    // ── Typed dispatch on the buffer's element kind ──────────────────

    /// <summary>Reads the element at <paramref name="index"/> as the buffer's
    /// element kind, boxed to its CLR type.</summary>
    public static object Read(object handle, int index)
    {
        var b = Unwrap(handle);
        b.ValidateRange((long)index * b.ElementSize, SizeOfKind(b.Kind));
        long off = (long)index * b.ElementSize;
        var r = b.Kind switch
        {
            PtrElementKind.I4 => Marshal.ReadInt32(b.Address, (int)off),
            PtrElementKind.I8 => Marshal.ReadInt64(b.Address, (int)off),
            PtrElementKind.R4 => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(b.Address, (int)off)),
            PtrElementKind.R8 => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(b.Address, (int)off)),
            _ => throw new InvalidOperationException($"PtrHost: unhandled kind {b.Kind}"),
        };
        return r;
    }

    /// <summary>Writes <paramref name="value"/> at <paramref name="index"/> in the
    /// buffer's element kind. The boxed value's CLR type must match the buffer's.</summary>
    public static void Write(object handle, int index, object value)
    {
        var b = Unwrap(handle);
        b.ValidateRange((long)index * b.ElementSize, SizeOfKind(b.Kind));
        long off = (long)index * b.ElementSize;
        switch (b.Kind)
        {
            case PtrElementKind.I4: Marshal.WriteInt32(b.Address, (int)off, Convert.ToInt32(value)); break;
            case PtrElementKind.I8: Marshal.WriteInt64(b.Address, (int)off, Convert.ToInt64(value)); break;
            case PtrElementKind.R4: Marshal.WriteInt32(b.Address, (int)off, BitConverter.SingleToInt32Bits(Convert.ToSingle(value))); break;
            case PtrElementKind.R8: Marshal.WriteInt64(b.Address, (int)off, BitConverter.DoubleToInt64Bits(Convert.ToDouble(value))); break;
            default: throw new InvalidOperationException($"PtrHost: unhandled kind {b.Kind}");
        }
    }
}
