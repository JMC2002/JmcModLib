using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JmcModLib.UI
{
    /// <summary>
    /// 通用按钮组件，支持从模板克隆并自动修复布局/图标问题
    /// </summary>
    public class SimpleButton : MonoBehaviour
    {
        // 公开引用，方便外部修改样式
        /// <summary>
        /// 绑定的按钮组件。可能为 null（初始化前或模板不含按钮时会创建）。
        /// </summary>
        public Button? ButtonComp { get; private set; }
        /// <summary>
        /// 按钮的文本组件。可能为 null（初始化前），创建后指向 `TextMeshProUGUI`。
        /// </summary>
        public TextMeshProUGUI? TextComp { get; private set; }
        /// <summary>
        /// 背景图组件。优先使用 `Button.targetGraphic`，否则回退到自身 `Image`。
        /// </summary>
        public Image? BackgroundComp { get; private set; }
        /// <summary>
        /// 按钮的 `RectTransform`。初始化时确保存在。
        /// </summary>
        public RectTransform? Rect { get; private set; }

        /// <summary>
        /// 创建一个按钮实例
        /// </summary>
        /// <param name="parent">父物体</param>
        /// <param name="text">按钮文字</param>
        /// <param name="onClick">点击回调</param>
        /// <param name="template">可选的模板物体 (GameObject)</param>
        /// <param name="font">可选字体</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        /// <param name="anchor">锚点位置 (默认居中)</param>
        /// <returns>返回挂载了 SimpleButton 的组件</returns>
        public static SimpleButton Create(
            GameObject parent,
            string? text,
            Action? onClick,
            GameObject? template = null,
            TMP_FontAsset? font = null,
            float width = 220f,
            float height = 60f,
            Vector2? anchor = null)
        {
            GameObject? btnObj;

            // 1. 实例化或创建基础物体
            if (template != null)
            {
                btnObj = Instantiate(template, parent.transform);
                btnObj.name = $"Btn_{text}";

                // 清理不必要的组件
                var comps = btnObj.GetComponents<MonoBehaviour>();
                foreach (var c in comps)
                {
                    if (c is not null and not Image and not Button) DestroyImmediate(c);
                }

                // 清理布局组件 (防止自动排版干扰)
                foreach (var layout in btnObj.GetComponentsInChildren<LayoutElement>(true)) if (layout) DestroyImmediate(layout);
                foreach (var fitter in btnObj.GetComponentsInChildren<ContentSizeFitter>(true)) if (fitter) DestroyImmediate(fitter);
                foreach (var group in btnObj.GetComponentsInChildren<LayoutGroup>(true)) if (group) DestroyImmediate(group);
            }
            else
            {
                btnObj = new GameObject($"Btn_{text}");
                btnObj.transform.SetParent(parent.transform, false);
                var img = btnObj.AddComponent<Image>();
                img.color = new Color(0.2f, 0.2f, 0.2f, 1f);
                btnObj.AddComponent<Button>();
            }

            // 2. 挂载脚本并初始化
            var instance = btnObj.AddComponent<SimpleButton>();
            instance.Initialize(text, onClick, font, width, height, anchor ?? new Vector2(0.5f, 0.5f));

            return instance;
        }

        private void Initialize(string? text, Action? onClick, TMP_FontAsset? font, float width, float height, Vector2 anchor)
        {
            Rect = GetComponent<RectTransform>();
            if (Rect == null) Rect = gameObject.AddComponent<RectTransform>();

            // 设置布局
            Rect.anchorMin = anchor;
            Rect.anchorMax = anchor;
            Rect.pivot = new Vector2(0.5f, 0.5f);
            Rect.sizeDelta = new Vector2(width, height);
            Rect.anchoredPosition = Vector2.zero;

            // 获取组件
            ButtonComp = GetComponent<Button>();
            if (ButtonComp == null) ButtonComp = gameObject.AddComponent<Button>();

            BackgroundComp = ButtonComp.targetGraphic as Image;
            if (BackgroundComp == null) BackgroundComp = GetComponent<Image>();

            // --- 图标修复逻辑 ---
            FixIconLayout();

            // --- 文本逻辑 ---
            SetupText(text, font);

            // --- 事件绑定 ---
            ButtonComp.onClick.RemoveAllListeners();
            if (onClick != null)
            {
                ButtonComp.onClick.AddListener(() => onClick());
            }
        }

        private void FixIconLayout()
        {
            // 寻找除了背景图以外的所有 Image (通常是图标)
            Image[]? allImages = GetComponentsInChildren<Image>(true);
            if (allImages == null) return;

            foreach (var img in allImages)
            {
                if (img != null && img != BackgroundComp)
                {
                    // 提级到按钮根节点
                    img.transform.SetParent(transform, false);

                    RectTransform? iconRect = img.GetComponent<RectTransform>();
                    if (iconRect != null)
                    {
                        // 强制靠左居中
                        iconRect.anchorMin = new Vector2(0, 0.5f);
                        iconRect.anchorMax = new Vector2(0, 0.5f);
                        iconRect.pivot = new Vector2(0.5f, 0.5f);
                        iconRect.anchoredPosition = new Vector2(25, 0); // 固定左边距
                        iconRect.sizeDelta = new Vector2(20, 20);       // 固定图标大小
                    }
                    img.gameObject.SetActive(true);
                }
            }
        }

        private void SetupText(string? text, TMP_FontAsset? font)
        {
            TextComp = GetComponentInChildren<TextMeshProUGUI>();
            if (TextComp == null)
            {
                GameObject tObj = new("Text");
                tObj.transform.SetParent(transform, false);
                TextComp = tObj.AddComponent<TextMeshProUGUI>();
            }
            else
            {
                // 确保 Text 是 Button 的直接子物体，防止层级混乱
                TextComp.transform.SetParent(transform, false);
            }

            // 文本布局 (左侧留出空间给图标)
            RectTransform? textRect = TextComp.GetComponent<RectTransform>();
            if (textRect != null)
            {
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.sizeDelta = Vector2.zero;
                textRect.offsetMin = new Vector2(30, 0);
                textRect.offsetMax = Vector2.zero;
            }

            // 样式设置
            TextComp.margin = Vector4.zero;
            TextComp.text = text ?? "";
            if (font != null) TextComp.font = font;

            TextComp.color = Color.white;
            TextComp.enableAutoSizing = false;
            TextComp.fontSize = 32;
            TextComp.alignment = TextAlignmentOptions.Center;
            TextComp.overflowMode = TextOverflowModes.Overflow;
            TextComp.richText = true;
        }

        /// <summary>
        /// 链式调用：设置文字颜色
        /// </summary>
        public SimpleButton SetTextColor(Color color)
        {
            TextComp?.color = color;
            return this;
        }

        /// <summary>
        /// 链式调用：设置背景颜色
        /// </summary>
        public SimpleButton SetBackgroundColor(Color color)
        {
            BackgroundComp?.color = color;
            return this;
        }
    }
}