using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace PdFlIndicator;

[BepInPlugin("pdfl.indicator", "PD/FL Indicator", "1.0.0")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Log2;

    internal static ConfigEntry<bool> CfgEnabled;
    internal static ConfigEntry<float> CfgOffsetY;
    internal static ConfigEntry<float> CfgTextScale;
    internal static ConfigEntry<string> CfgPdColor;
    internal static ConfigEntry<string> CfgFlColor;
    internal static ConfigEntry<string> CfgClearColor;
    internal static ConfigEntry<string> CfgFormat;
    internal static ConfigEntry<string> CfgPdText;
    internal static ConfigEntry<string> CfgFlText;
    internal static ConfigEntry<float> CfgStable;
    internal static ConfigEntry<bool> CfgShowCounts;
    internal static ConfigEntry<bool> CfgVerbose;

    public override void Load()
    {
        Log2 = Log;

        CfgEnabled = Config.Bind("General", "Enabled", true, "显示顶部状态/判定计数");

        CfgOffsetY = Config.Bind("Layout", "OffsetY", -0.35f,
            "文字相对 clearstate 容器的垂直偏移（容器单位，负值向下）。默认 -0.35 = 回忆值条正下方");
        CfgTextScale = Config.Bind("Layout", "TextScale", 0.3f,
            "文字大小，相对于游戏分数文字的倍数");

        CfgPdColor = Config.Bind("Color", "PdColor", "#F09CFF", "PD 时文字颜色（游戏 PERFECT DIVE 粉紫）");
        CfgFlColor = Config.Bind("Color", "FlColor", "#B3B5FF", "FL 时文字颜色（游戏 FULL LINK 淡蓝紫）");
        CfgClearColor = Config.Bind("Color", "ClearColor", "#9CE8F7", "BREAK 后文字颜色（游戏 DIVE CLEARED 青色）");

        CfgFormat = Config.Bind("Text", "Format", "$s $e | $n | $b",
            "显示格式。$s=状态(PD/FL/空) $e=EXACT 总数 $be=大 EXACT 数 $se=小 EXACT 数 $n=NEAR 数 $b=BREAK 数，$$ 输出一个 $");
        CfgPdText = Config.Bind("Text", "PdText", "PD", "没有 NEAR / BREAK 时 $s 的值");
        CfgFlText = Config.Bind("Text", "FlText", "FL", "有 NEAR 但没有 BREAK 时 $s 的值");

        CfgStable = Config.Bind("Judgement", "StableSeconds", 0.35f,
            "NEAR/BREAK 需要持续这么久（秒）才计入，用于过滤 hold 的中间态抖动");

        CfgShowCounts = Config.Bind("Debug", "ShowCounts", false, "把计数变化打到日志");
        CfgVerbose = Config.Bind("Debug", "Verbose", false, "状态变化时写日志");

        AddComponent<PdFlBehaviour>();
        Log2.LogInfo("[pdfl] loaded 1.0.0");
    }
}
