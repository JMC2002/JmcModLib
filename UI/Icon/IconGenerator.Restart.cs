using UnityEngine;

namespace JmcModLib.UI.Icon;

public static partial class IconGenerator
{
    private static Sprite CreateRestartSprite()
    {
        const int size = 128;
        var tex = CreateSmoothTexture(size); // 使用主文件中的辅助方法

        var pixels = new Color[size * size];
        Vector2 center = new(size / 2f, size / 2f);

        float radius = size * 0.36f;
        float thickness = size * 0.08f;

        // === 关键参数 ===
        const float startAngle = 45f;
        const float endAngle = 315f;
        float arrowLen = thickness * 2.5f;
        const float arrowAngle = 30f;
        float gapLength = thickness * 0.9f;

        // 计算几何数据
        float endRad = endAngle * Mathf.Deg2Rad;

        Vector2 arcEndPos = center + new Vector2(Mathf.Cos(endRad), Mathf.Sin(endRad)) * radius;

        Vector2 tangent = new Vector2(-Mathf.Sin(endRad), Mathf.Cos(endRad)).normalized;
        Vector2 arrowTipPos = arcEndPos + (tangent * gapLength);
        Vector2 arrowDir = tangent; // 箭头方向

        // 预计算旋转向量
        Vector2 sideA = MathUtils.Rotate(arrowDir, +arrowAngle);
        Vector2 sideB = MathUtils.Rotate(arrowDir, -arrowAngle);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new(x + 0.5f, y + 0.5f);
                float alpha = 0f;

                // 1. 绘制圆弧
                Vector2 d = p - center;
                float dist = d.magnitude;
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                if (ang < 0) ang += 360f;

                if (MathUtils.IsAngleBetween(ang, startAngle, endAngle))
                {
                    float ringDist = Mathf.Abs(dist - radius) - (thickness * 0.5f);
                    float ringA = 1f - Mathf.SmoothStep(0f, 1.2f, ringDist);
                    alpha = Mathf.Max(alpha, ringA);
                }

                // 2. 绘制箭头
                alpha = Mathf.Max(alpha, MathUtils.LineAlpha(p, arrowTipPos, arrowTipPos - (sideA * arrowLen), thickness * 0.4f));
                alpha = Mathf.Max(alpha, MathUtils.LineAlpha(p, arrowTipPos, arrowTipPos - (sideB * arrowLen), thickness * 0.4f));

                pixels[(y * size) + x] = new Color(1, 1, 1, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
    }

    // 私有数学工具类，避免污染外部
    private static class MathUtils
    {
        public static bool IsAngleBetween(float a, float start, float end)
            => start <= end ? (a >= start && a <= end) : (a >= start || a <= end);

        public static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r);
            float s = Mathf.Sin(r);
            return new((v.x * c) - (v.y * s), (v.x * s) + (v.y * c));
        }

        public static float LineAlpha(Vector2 p, Vector2 a, Vector2 b, float width)
        {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            float d = (pa - (ba * h)).magnitude - width;
            return 1f - Mathf.SmoothStep(0f, 1.2f, d);
        }
    }
}