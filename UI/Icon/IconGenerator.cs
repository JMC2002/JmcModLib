using UnityEngine;

namespace JmcModLib.UI.Icon
{
    /// <summary>
    /// 一些图标的生成器工具
    /// </summary>
    public static class IconGenerator
    {
        /// <summary>
        /// 生成一个重启图标
        /// </summary>
        public static Sprite GenerateRestartIcon()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[size * size];
            Vector2 center = new(size / 2f, size / 2f);

            float radius = size * 0.36f;
            float thickness = size * 0.08f;

            // === 关键：逆时针圆弧角度 ===
            float startAngle = 45f;   // 起点
            float endAngle = 315f;  // 终点（逆时针）

            // === 箭头参数 ===
            float arrowLen = thickness * 2.5f;
            float arrowAngle = 30f;

            // 末端角度（箭头放这里）
            float gapLength = thickness * 0.9f;

            // 圆弧末端角度
            float endRad = endAngle * Mathf.Deg2Rad;

            // 圆弧终点
            Vector2 arcEndPos = center + (new Vector2(
                Mathf.Cos(endRad),
                Mathf.Sin(endRad)
            ) * radius);

            // 逆时针切线（运动方向）
            Vector2 tangent = new Vector2(
                -Mathf.Sin(endRad),
                 Mathf.Cos(endRad)
            ).normalized;

            // 箭头尖端：与圆弧之间留 gap
            Vector2 arrowTipPos = arcEndPos + (tangent * gapLength);

            // 箭头方向（角平分线）
            Vector2 arrowDir = tangent;

            // 两侧对称展开
            Vector2 sideA = Rotate(arrowDir, +arrowAngle);
            Vector2 sideB = Rotate(arrowDir, -arrowAngle);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new(x + 0.5f, y + 0.5f);
                    float alpha = 0f;

                    // ===== 圆弧 =====
                    Vector2 d = p - center;
                    float dist = d.magnitude;

                    float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    if (ang < 0) ang += 360f;

                    bool inArc =
                        IsAngleBetween(ang, startAngle, endAngle);

                    if (inArc)
                    {
                        float ringDist = Mathf.Abs(dist - radius) - (thickness * 0.5f);
                        float ringA = 1f - Mathf.SmoothStep(0f, 1.2f, ringDist);
                        alpha = Mathf.Max(alpha, ringA);
                    }

                    // 绘制箭头（从尖端往回）
                    alpha = Mathf.Max(alpha,
                        LineAlpha(p, arrowTipPos, arrowTipPos - (sideA * arrowLen), thickness * 0.4f));
                    alpha = Mathf.Max(alpha,
                        LineAlpha(p, arrowTipPos, arrowTipPos - (sideB * arrowLen), thickness * 0.4f));

                    pixels[(y * size) + x] = new Color(1, 1, 1, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
        }

        // ===== 工具函数 =====
        private static bool IsAngleBetween(float a, float start, float end)
        {
            if (start <= end)
                return a >= start && a <= end;
            return a >= start || a <= end;
        }

        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r);
            float s = Mathf.Sin(r);
            return new Vector2(
                (v.x * c) - (v.y * s),
                (v.x * s) + (v.y * c)
            );
        }

        private static float LineAlpha(Vector2 p, Vector2 a, Vector2 b, float width)
        {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            float d = (pa - (ba * h)).magnitude - width;
            return 1f - Mathf.SmoothStep(0f, 1.2f, d);
        }
    }
}
