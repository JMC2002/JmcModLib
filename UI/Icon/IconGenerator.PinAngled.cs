using UnityEngine;

namespace JmcModLib.UI.Icon;

public static partial class IconGenerator
{
    private static Sprite CreatePinAngledSprite()
    {
        const int w = 16;
        const int h = 16;
        var tex = CreateTexture(w);
        ClearTexture(tex);

        Color c = Color.white;

        // --- 绘制 45度 倾斜图钉 (指向左下角) ---

        // 1. 针尖 (最左下)
        tex.SetPixel(3, 3, c);

        // 2. 针身 (细线)
        tex.SetPixel(4, 4, c);
        tex.SetPixel(5, 5, c);
        tex.SetPixel(6, 6, c);

        // 3. 钉体 (加粗部分)
        // 主对角线
        tex.SetPixel(7, 7, c);
        tex.SetPixel(8, 8, c);
        tex.SetPixel(9, 9, c);
        // 上方加厚像素 (增加立体感)
        tex.SetPixel(6, 7, c);
        tex.SetPixel(7, 8, c);
        tex.SetPixel(8, 9, c);

        // 4. 钉帽 (顶部的大头)
        // 垂直于针身的横杠 (左上到右下方向)
        tex.SetPixel(7, 10, c);
        tex.SetPixel(8, 11, c);
        tex.SetPixel(9, 10, c);  // 中心连接点
        tex.SetPixel(10, 9, c);
        tex.SetPixel(11, 8, c);

        // 钉帽的圆顶 (最右上)
        // 使用集合表达式简化坐标点的遍历 (C# 12+)
        Vector2Int[] headPixels =
        [
            new(8, 12), new(9, 11), new(10, 11), new(11, 10), new(11, 9), new(12, 8),
            // 封顶像素
            new(9, 12), new(10, 12), new(11, 11), new(12, 10), new(12, 9)
        ];

        foreach (var p in headPixels)
        {
            tex.SetPixel(p.x, p.y, c);
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
    }
}