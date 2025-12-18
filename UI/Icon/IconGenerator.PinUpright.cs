using UnityEngine;

namespace JmcModLib.UI.Icon;

public static partial class IconGenerator
{
    private static Sprite CreatePinUprightSprite()
    {
        const int w = 16;
        const int h = 16;
        var tex = CreateTexture(w);
        ClearTexture(tex);

        Color c = Color.white;

        // --- 绘制直立图钉 ---

        // 1. 钉帽 (Head)
        // 顶部两层矩形
        for (int x = 4; x <= 11; x++) tex.SetPixel(x, 12, c);
        for (int x = 5; x <= 10; x++) tex.SetPixel(x, 13, c);

        // 2. 钉身 (Body)
        // 中间较粗的部分
        for (int y = 6; y <= 11; y++)
        {
            tex.SetPixel(7, y, c);
            tex.SetPixel(8, y, c);
        }

        // 3. 针尖 (Tip)
        // 底部较细的部分
        for (int y = 2; y <= 5; y++) tex.SetPixel(7, y, c);
        tex.SetPixel(7, 1, c); // 尖端点

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
    }
}