using System;
using UnityEngine;

namespace JmcModLib.UI.Icon;

/// <summary>
/// 图标生成器核心入口。
/// 利用分部类 (partial) 将不同图标的绘制逻辑拆分到不同文件。
/// 利用 Lazy&lt;T&gt; 实现惰性加载和缓存。
/// </summary>
public static partial class IconGenerator
{
    // 使用 Lazy<T> 保证线程安全且只在第一次访问时生成
    private static readonly Lazy<Sprite> _restartIcon = new(CreateRestartSprite);
    private static readonly Lazy<Sprite> _pinUprightIcon = new(CreatePinUprightSprite);
    private static readonly Lazy<Sprite> _pinAngledIcon = new(CreatePinAngledSprite);
    private static readonly Lazy<Sprite> _lockClosedIcon = new(() => CreateLockSprite(true));
    private static readonly Lazy<Sprite> _lockOpenIcon = new(() => CreateLockSprite(false));
    private static readonly Lazy<Sprite> _stickTopIcon = new(() => CreateBarArrowSprite(true));
    private static readonly Lazy<Sprite> _stickBottomIcon = new(() => CreateBarArrowSprite(false));

    /// <summary>
    /// 获取重启图标 (128x128, 平滑)
    /// </summary>
    public static Sprite Restart => _restartIcon.Value;

    /// <summary>
    /// 获取直立图钉图标 (16x16, 像素风)
    /// </summary>
    public static Sprite PinUpright => _pinUprightIcon.Value;

    /// <summary>
    /// 获取倾斜图钉图标 (16x16, 像素风)
    /// </summary>
    public static Sprite PinAngled => _pinAngledIcon.Value;

    /// <summary>
    /// 获取闭合的锁图标 (16x16, 像素风)
    /// </summary>
    public static Sprite LockClosed => _lockClosedIcon.Value;

    /// <summary>
    /// 获取打开的锁图标 (16x16, 像素风)
    /// </summary>
    public static Sprite LockOpen => _lockOpenIcon.Value;

    /// <summary>
    /// 获取置顶图标 (16x16, 箭头指向上方横线)
    /// </summary>
    public static Sprite StickTop => _stickTopIcon.Value;

    /// <summary>
    /// 获取置底图标 (16x16, 箭头指向下方横线)
    /// </summary>
    public static Sprite StickBottom => _stickBottomIcon.Value;

    // 通用辅助：创建一个干净的像素风 Texture
    private static Texture2D CreateTexture(int size) => new(size, size, TextureFormat.RGBA32, false)
    {
        filterMode = FilterMode.Point,
        wrapMode = TextureWrapMode.Clamp
    };

    // 通用辅助：创建一个干净的平滑 Texture
    private static Texture2D CreateSmoothTexture(int size) => new(size, size, TextureFormat.RGBA32, false)
    {
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp
    };

    // 通用辅助：清空画布
    private static void ClearTexture(Texture2D tex)
    {
        // C# 12/13 集合表达式优化，虽然 SetPixels 需要数组，但在某些上下文中可以简化
        var clearColor = Color.clear;
        var cols = new Color[tex.width * tex.height];
        Array.Fill(cols, clearColor);
        tex.SetPixels(cols);
    }
}