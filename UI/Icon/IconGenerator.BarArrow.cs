using UnityEngine;

namespace JmcModLib.UI.Icon;

public static partial class IconGenerator
{
    private static Sprite CreateBarArrowSprite(bool isTop)
    {
        const int w = 16;
        const int h = 16;
        var tex = CreateTexture(w);
        ClearTexture(tex);
        Color c = Color.white;

        int MapY(int y) => isTop ? y : (h - 1 - y);

        // ==========================
        // 1. 绘制横线 (Bar) - 收窄
        // ==========================
        // 原来是 2~13 (12px), 现在改为 3~12 (10px)
        for (int x = 3; x <= 12; x++)
        {
            tex.SetPixel(x, MapY(13), c);
            tex.SetPixel(x, MapY(14), c);
        }

        // ==========================
        // 2. 绘制箭头 (Arrow) - 加宽
        // ==========================

        // A. 箭头尖端 (Tip)
        // y=11 (保持与横线的空隙)
        tex.SetPixel(7, MapY(11), c);
        tex.SetPixel(8, MapY(11), c);

        // B. 箭头双翼 (Wings) - 增加了一层，使其更宽
        Vector2Int[] arrowHead =
        [
            // y=10 (4px宽)
            new(6, 10), new(7, 10), new(8, 10), new(9, 10),
            // y=9 (6px宽)
            new(5, 9), new(6, 9), new(7, 9), new(8, 9), new(9, 9), new(10, 9),
            // y=8 (8px宽) - 新增的一层，让箭头看起来更饱满
            new(4, 8), new(5, 8), new(6, 8), new(7, 8), new(8, 8), new(9, 8), new(10, 8), new(11, 8)
        ];

        foreach (var p in arrowHead)
        {
            tex.SetPixel(p.x, MapY(p.y), c);
        }

        // C. 箭头柄 (Shaft)
        // 宽度 2px (x=7,8)
        // 高度缩短一点以适应变大的箭头头 (y=2 到 y=7)
        for (int y = 2; y <= 7; y++)
        {
            tex.SetPixel(7, MapY(y), c);
            tex.SetPixel(8, MapY(y), c);
        }

        tex.Apply(false, true); // 上传后释放 CPU 内存
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
    }
}