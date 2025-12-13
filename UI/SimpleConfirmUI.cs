using Duckov.UI.MainMenu;
using JmcModLib.Utils;
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JmcModLib.UI
{
    /// <summary>
    /// 一个通用的简易确认弹窗组件，用于在 Canvas 上显示带有遮罩的模态对话框（包含确认/取消按钮）。
    /// </summary>
    public class SimpleConfirmUI : MonoBehaviour
    {
        /// <summary>
        /// 是否active
        /// </summary>
        public static bool IsActive { get; private set; } = false;
        private static SimpleConfirmUI? _instance;
        private Action? _onCancelAction;

        // =======================================================================
        // 1. 重载方法：传入组件作为模板
        // =======================================================================
        /// <summary>
        /// 显示确认弹窗，并尝试从指定的 UI 组件模板中提取字体样式（推荐使用此重载以保持游戏风格一致）。
        /// </summary>
        /// <param name="contextObject">上下文对象，系统将从该对象的父级中查找 Canvas 以决定弹窗挂载位置。若找不到则会在场景中全局查找。</param>
        /// <param name="message">弹窗中间显示的提示消息内容。</param>
        /// <param name="onConfirm">点击“确认”按钮时的回调操作。</param>
        /// <param name="styleTemplate">样式模板组件。弹窗将尝试从该组件（或其子物体）上的 TextMeshProUGUI 中提取字体，以便让弹窗字体与游戏原生 UI 保持一致。</param>
        /// <param name="onCancel">点击“取消”按钮或按下 ESC 键时的回调操作。默认为 null。</param>
        /// <param name="confirmText">确认按钮显示的文本。默认为 "Confirm"。</param>
        /// <param name="cancelText">取消按钮显示的文本。默认为 "Cancel"。</param>
        /// <param name="confirmColor">确认按钮的文本颜色。默认为红色（警示色），若传 null 则使用默认样式。</param>
        public static void Show(
            Transform contextObject,
            string message,
            Action? onConfirm,
            Component styleTemplate,
            Action? onCancel = null,
            string confirmText = "Confirm",
            string cancelText = "Cancel",
            Color? confirmColor = null)
        {
            TMP_FontAsset? font = null;
            if (styleTemplate != null)
            {
                var tmp = styleTemplate.GetComponent<TextMeshProUGUI>() 
                       ?? styleTemplate.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) font = tmp.font;
            }

            Show(contextObject, message, onConfirm, onCancel, font, confirmText, cancelText, confirmColor);
        }

        // =======================================================================
        // 2. 主方法
        // =======================================================================
        /// <summary>
        /// 显示确认弹窗，允许直接指定字体资源（核心实现方法）。
        /// </summary>
        /// <param name="contextObject">上下文对象，系统将从该对象的父级中查找 Canvas 以决定弹窗挂载位置。若找不到则会在场景中全局查找。</param>
        /// <param name="message">弹窗中间显示的提示消息内容。</param>
        /// <param name="onConfirm">点击“确认”按钮时的回调操作。</param>
        /// <param name="onCancel">点击“取消”按钮或按下 ESC 键时的回调操作。默认为 null。</param>
        /// <param name="font">指定的 TextMeshPro 字体资源。如果为 null，将使用 TMP 的默认字体。</param>
        /// <param name="confirmText">确认按钮显示的文本。默认为 "Confirm"。</param>
        /// <param name="cancelText">取消按钮显示的文本。默认为 "Cancel"。</param>
        /// <param name="confirmColor">确认按钮的文本颜色。默认为红色（警示色），若传 null 则使用默认样式。</param>
        public static void Show(
            Transform contextObject,
            string message,
            Action? onConfirm,
            Action? onCancel = null,
            TMP_FontAsset? font = null,
            string confirmText = "Confirm",
            string cancelText = "Cancel",
            Color? confirmColor = null)
        {
            if (IsActive) return;

            // --- 安全获取 Canvas ---
            Canvas? canvas = null;
            try
            {
                if (contextObject != null) canvas = contextObject.GetComponentInParent<Canvas>();
                if (canvas == null) canvas = FindObjectOfType<Canvas>();
            }
            catch (Exception) { /* 忽略查找错误 */ }

            if (canvas == null)
            {
                ModLogger.Error("[JmcModLib] 找不到 Canvas，无法显示弹窗。");
                onCancel?.Invoke();
                return;
            }

            try
            {
                // --- 创建遮罩 ---
                GameObject overlayObj = new("Jmc_Confirm_Overlay");
                overlayObj.transform.SetParent(canvas.transform, false);
                overlayObj.transform.SetAsLastSibling();

                var ui = overlayObj.AddComponent<SimpleConfirmUI>();
                _instance = ui;
                ui._onCancelAction = onCancel;
                IsActive = true;

                // 遮罩布局
                RectTransform overlayRect = overlayObj.AddComponent<RectTransform>();
                overlayRect.anchorMin = Vector2.zero;
                overlayRect.anchorMax = Vector2.one;
                overlayRect.offsetMin = Vector2.zero;
                overlayRect.offsetMax = Vector2.zero;

                // 覆盖层级
                Canvas overlayCanvas = overlayObj.AddComponent<Canvas>();
                overlayCanvas.overrideSorting = true;
                overlayCanvas.sortingOrder = 30000;
                overlayObj.AddComponent<GraphicRaycaster>();

                // 背景
                Image bg = overlayObj.AddComponent<Image>();
                bg.color = new Color(0, 0, 0, 0.9f);
                bg.raycastTarget = true;

                // --- 内容面板 ---
                GameObject panelObj = new("ContentPanel");
                panelObj.transform.SetParent(overlayObj.transform, false);
                RectTransform panelRect = panelObj.AddComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.25f, 0.35f);
                panelRect.anchorMax = new Vector2(0.75f, 0.65f);
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;

                GameObject? btnTemplate = GetGameButtonTemplate();
                if (btnTemplate is null)
                    ModLogger.Debug("未找到按钮模板");
                else
                    ModLogger.Debug("找到按钮模板");
                // --- 消息文本 ---
                GameObject textObj = new("Message");
                textObj.transform.SetParent(panelObj.transform, false);

                TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
                tmp.text = message ?? ""; // 防止 message 为 null
                if (font != null) tmp.font = font;

                tmp.fontSize = 40;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 20;
                tmp.fontSizeMax = 50;
                tmp.alignment = TextAlignmentOptions.Bottom;
                tmp.color = Color.white;
                tmp.richText = true;

                RectTransform textRect = textObj.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0, 0.4f);
                textRect.anchorMax = new Vector2(1, 1);
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = new Vector2(0, -20);

                // --- 按钮容器 ---
                GameObject btnContainer = new("Buttons");
                btnContainer.transform.SetParent(panelObj.transform, false);
                RectTransform btnConRect = btnContainer.AddComponent<RectTransform>();
                btnConRect.anchorMin = new Vector2(0, 0);
                btnConRect.anchorMax = new Vector2(1, 0.4f);
                btnConRect.offsetMin = Vector2.zero;
                btnConRect.offsetMax = Vector2.zero;

                // --- 创建按钮 ---
                // 创建确认按钮
                SimpleButton.Create<ContinueButton>(
                    parent: btnContainer,
                    text: confirmText ?? "Confirm",
                    onClick: () => { Close(); onConfirm?.Invoke(); },
                    font: font,
                    width: 220,
                    height: 60,
                    anchor: new Vector2(0.3f, 0.5f) // 左侧
                )
                .SetTextColor(confirmColor ?? Color.red); // 链式设置颜色

                // 创建取消按钮
                SimpleButton.Create<ContinueButton>(
                    parent: btnContainer,
                    text: cancelText ?? "Cancel",
                    onClick: () => { Close(); onCancel?.Invoke(); },
                    font: font,
                    width: 220,
                    height: 60,
                    anchor: new Vector2(0.7f, 0.5f) // 右侧
                )
                .SetTextColor(Color.white);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"显示弹窗时发生未知错误: {ex}");
                // 发生错误时尝试清理，防止残留遮罩锁死游戏
                Close();
                onCancel?.Invoke();
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _onCancelAction?.Invoke();
                CloseInstance();
            }
        }

        /// <summary>
        /// 强制关闭并销毁当前显示的确认弹窗。
        /// </summary>
        public static void Close()
        {
            if (_instance != null) _instance.CloseInstance();
            else
            {
                // 兜底：如果 instance 丢失但物体还在
                var leftover = GameObject.Find("Jmc_Confirm_Overlay");
                if (leftover != null) Destroy(leftover);
                IsActive = false;
            }
        }

        private void CloseInstance()
        {
            IsActive = false;
            _instance = null;
            if (gameObject != null) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            IsActive = false;
        }

        // === 辅助逻辑 ===
        private static GameObject? GetGameButtonTemplate()
        {
            try
            {
                var templates = Resources.FindObjectsOfTypeAll<ContinueButton>();
                if (templates == null || templates.Length == 0)
                    templates = UnityEngine.Object.FindObjectsOfType<ContinueButton>(true);

                if (templates == null || templates.Length == 0) return null;
                return templates.FirstOrDefault().gameObject;

                // 4. 实例化
                // GameObject newBtnObj = UnityEngine.Object.Instantiate(templates[0].gameObject, slotScript.transform);
                var all = Resources.FindObjectsOfTypeAll<GameObject>();
                // 增加 null 检查，因为 Resources 可能会返回 weird stuff
                var target = all.FirstOrDefault(g => g != null && g.name == "ContinueButton");
                return target;
            }
            catch { return null; }
        }
    }
}