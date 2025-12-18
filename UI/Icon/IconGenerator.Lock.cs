using System;
using UnityEngine;

namespace JmcModLib.UI.Icon;

public static partial class IconGenerator
{
    /// <summary>
    /// 内部共享的锁图标生成逻辑
    /// </summary>
    private static Sprite CreateLockSprite(bool isClosed)
    {
        const int w = 16;
        const int h = 16;

        // 使用主文件定义的辅助方法创建和清空纹理
        var tex = CreateTexture(w);
        ClearTexture(tex);

        Color c = Color.white;

        // --- 1. 绘制锁身 (底部矩形) ---
        // 范围 x[3~12], y[0~7]
        for (int x = 3; x <= 12; x++)
        {
            for (int y = 0; y <= 7; y++)
            {
                tex.SetPixel(x, y, c);
            }
        }

        // --- 2. 绘制锁梁 (U型) ---
        // 闭合时 yOffset=0, 打开时 yOffset=3
        int yOffset = isClosed ? 0 : 3;

        // 基础高度 (闭合时从 y=8 开始)
        int yStart = 8 + yOffset;
        int yEnd = Math.Min(12 + yOffset, 15); // 防止越界 (Max Index 15)

        // 绘制左右两根柱子
        for (int y = yStart; y <= yEnd; y++)
        {
            tex.SetPixel(4, y, c);  // 左柱 outer
            tex.SetPixel(5, y, c);  // 左柱 inner
            tex.SetPixel(10, y, c); // 右柱 inner
            tex.SetPixel(11, y, c); // 右柱 outer
        }

        // 绘制顶部拱形 (连接处)
        int yTop = yEnd + 1;
        if (yTop <= 15)
        {
            for (int x = 4; x <= 11; x++)
            {
                tex.SetPixel(x, yTop, c);
                // 加粗拱形内部连接点 (平滑转角)
                if (yTop > 0) tex.SetPixel(x, yTop - 1, c);
            }
        }

        // --- 3. 绘制钥匙孔 (透明色挖空) ---
        // 使用 C# 12 集合表达式简化坐标定义
        Vector2Int[] keyholePixels =
        [
            new(7, 4), new(8, 4), // 中间
            new(7, 5), new(8, 5), // 上部
            new(7, 3), new(8, 3)  // 下部
        ];

        foreach (var p in keyholePixels)
        {
            tex.SetPixel(p.x, p.y, Color.clear);
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
    }
}