extern alias GameData;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using _cH = GameData::_K._cH;

namespace PdFlIndicator;

/// <summary>
/// 与游戏原生判定结构体 `_cH`（40 字节）二进制兼容的视图。
/// Result: 6=Exact, 4/5=SmallExact, 2/3=Near, 1=Break, 0=未判定。
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 40)]
public struct JudgementRecord
{
    [FieldOffset(0)] public long Id;
    [FieldOffset(8)] public double RelativeMs;
    [FieldOffset(16)] public double AbsoluteMs;
    [FieldOffset(24)] public int Kind;
    [FieldOffset(28)] public int Metadata;
    [FieldOffset(32)] public byte Result;
    [FieldOffset(33)] public byte Finalized;
}

public static class NativeJudgementArray
{
    public const int Stride = 40;

    public static Span<JudgementRecord> View(Il2CppStructArray<_cH> root)
    {
        Span<_cH> arrayView = root;
        if (arrayView.IsEmpty) return Span<JudgementRecord>.Empty;
        return MemoryMarshal.CreateSpan<JudgementRecord>(
            ref Unsafe.As<_cH, JudgementRecord>(ref MemoryMarshal.GetReference(arrayView)),
            arrayView.Length);
    }
}
