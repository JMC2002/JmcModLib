using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JmcModLib.Core.AttributeRouter
{
    /// <summary>
    /// AttributeRouter: 按 Attribute 类型把扫描到的访问器分发到对应 Handler。
    /// 线程安全，支持按 Assembly 扫描与撤销（若 Handler 支持）。
    /// </summary>
    public static class AttributeRouter
    {
        // 存放每个 Attribute 类型对应的处理器列表（线程安全）
        // 注意：List 内部操作在写入时锁定，读取时快照避免锁竞争
        private static readonly ConcurrentDictionary<Type, List<IAttributeHandler>> _handlers
            = new();

        // 已扫描的 Assembly 集合，防止重复扫描
        private static readonly ConcurrentDictionary<Assembly, byte> _scannedAssemblies
            = new();

        // 为每个 Assembly 记录被哪个 handler 处理了哪些 accessor，以便 Unscan 时撤销
        // Assembly -> Handler -> accessors
        private static readonly ConcurrentDictionary<Assembly, ConcurrentDictionary<IAttributeHandler, List<ReflectionAccessorBase>>> _assemblyHandlerRecords
            = new();

        /// <summary>
        /// 当一个 MOD 完成注册后触发。
        /// 参数：Assembly（唯一标识MOD）（该MOD元信息）
        /// </summary>
        internal static event Action<Assembly>? OnRegistered;

        /// <summary>
        /// 反注册 MOD 时触发。
        /// </summary>
        internal static event Action<Assembly>? OnUnRegistered;

        private static bool _initialized = false;
        // 初始化：默认和 ModRegistry 绑定（可在程序入口调用一次）
        public static void Init()
        {
            if (_initialized)
            {
                ModLogger.Warn("AttributeRouter 已初始化，重复调用 Init() 被忽略。");
                return;
            }
            _initialized = true;
            // subscribe once
            ModRegistry.OnRegistered += OnModRegistered;
            ModRegistry.OnUnRegistered += OnModUnRegistered;
            ModLogger.Debug("AttributeRouter初始化挂载监听完毕.");
        }

        // 清理：解绑事件（可在 Dispose 时调用）
        public static void Dispose()
        {
            try
            {
                ModRegistry.OnRegistered -= OnModRegistered;
                ModRegistry.OnUnRegistered -= OnModUnRegistered;
            }
            catch { /* ignore */ }
            _handlers.Clear();
            _scannedAssemblies.Clear();
            _assemblyHandlerRecords.Clear();
            ModLogger.Debug("AttributeRouter 卸载.");
        }

        // ========== Handler 注册 API ==========

        /// <summary>
        /// 注册一个 handler 用于处理指定 Attribute 类型。重复注册会追加。
        /// </summary>
        public static void RegisterHandler<TAttr>(IAttributeHandler handler) where TAttr : Attribute
        {
            var attrType = typeof(TAttr);
            var list = _handlers.GetOrAdd(attrType, _ => []);
            lock (list)
            {
                list.Add(handler);
            }
            ModLogger.Debug($"已注册 handler {handler.GetType().Name} 用于处理 attribute {attrType.Name}");
        }

        /// <summary>
        /// 注册一个基于委托的简单 handler。会被包装成 SimpleAttributeHandler。
        /// </summary>
        public static void RegisterHandler<TAttr>(Action<Assembly, ReflectionAccessorBase, TAttr> action) where TAttr : Attribute
        {
            RegisterHandler<TAttr>(new SimpleAttributeHandler<TAttr>(action));
        }

        /// <summary>
        /// 取消注册 handler（移除该 handler 对应的所有映射）
        /// </summary>
        public static bool UnregisterHandler(IAttributeHandler handler)
        {
            var removed = false;
            foreach (var kv in _handlers)
            {
                var list = kv.Value;
                lock (list)
                {
                    if (list.Remove(handler))
                        removed = true;
                }
            }
            return removed;
        }

        // ========== 扫描与分发 ==========

        private static void OnModRegistered(Assembly asm)
        {
            try
            {
                ScanAssembly(asm);
                OnRegistered?.Invoke(asm);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"AttributeRouter: 扫描程序集 {ModRegistry.GetTag(asm)} 失败", ex);
            }
        }

        private static void OnModUnRegistered(Assembly asm)
        {
            try
            {
                UnscanAssembly(asm);
                OnUnRegistered?.Invoke(asm);

                if (_scannedAssemblies.ContainsKey(asm))
                {
                    ModLogger.Warn($"AttributeRouter: 卸载程序集 {ModRegistry.GetTag(asm)} 后仍然标记为已扫描，可能未正确清理。");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"AttributeRouter: 卸载程序集 {ModRegistry.GetTag(asm)} 时执行 Unscan 失败", ex);
            }
        }

        /// <summary>
        /// 扫描 Assembly 中的所有 Type / Method / Member并分发 attribute。
        /// 如果已扫描过会跳过（幂等）。
        /// </summary>
        public static void ScanAssembly(Assembly asm)
        {
            if (asm is null) throw new ArgumentNullException(nameof(asm));

            // 幂等：若已扫描过则跳过
            if (!_scannedAssemblies.TryAdd(asm, 0))
            {
                ModLogger.Debug($"AttributeRouter: 跳过已扫描的程序集 {ModRegistry.GetTag(asm)}");
                return;
            }

            // 为该 asm 建立记录表
            var handlerRecord = new ConcurrentDictionary<IAttributeHandler, List<ReflectionAccessorBase>>();
            _assemblyHandlerRecords[asm] = handlerRecord;

            ModLogger.Debug($"AttributeRouter: 开始扫描程序集 {ModRegistry.GetTag(asm)}");

            // 1. 扫描类型本身（TypeAccessor）
            foreach (var t in asm.GetTypesSafe())
            {
                // 这里不应使用 IsSaveOwner（它会过滤掉 internal 类型），否则会导致标记在 internal 类型上的 Attribute 无法被扫描。
                // 仅排除不支持的类型形态（接口、数组、指针、ByRef、未闭包泛型等），其余类型均应扫描。
                if (!IsScannableType(t)) continue;

                // Type 本身的 attribute
                var tacc = TypeAccessor.Get(t);
                DispatchAccessor(asm, tacc, handlerRecord);

                // 方法
                foreach (var m in MethodAccessor.GetAll(t))
                {
                    DispatchAccessor(asm, m, handlerRecord);
                }

                // 字段/属性等（MemberAccessor）
                foreach (var m in MemberAccessor.GetAll(t))
                {
                    DispatchAccessor(asm, m, handlerRecord);
                }
            }

            ModLogger.Info($"AttributeRouter: 完成扫描程序集 {ModRegistry.GetTag(asm)}");
        }

        /// <summary>
        /// 撤销某个 Assembly 的所有 Handler 注册效果（如果 Handler 实现了 IAttributeHandler.Unregister，则会调用）。
        /// 最后从已扫描集合中移除记录。
        /// </summary>
        public static void UnscanAssembly(Assembly asm)
        {
            if (asm == null) throw new ArgumentNullException(nameof(asm));

            if (!_assemblyHandlerRecords.TryRemove(asm, out var handlerRecord))
            {
                // 没记录，可能未扫描
                _scannedAssemblies.TryRemove(asm, out _);
                ModLogger.Debug($"AttributeRouter: UnscanAssembly 找不到记录 {ModRegistry.GetTag(asm)}，已忽略。");
                return;
            }

            // 调用每个 handler 的 Unregister（如果实现）
            foreach (var kv in handlerRecord)
            {
                var handler = kv.Key;
                var list = kv.Value;
                try
                {
                    handler.Unregister?.Invoke(asm, list);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"AttributeRouter: 在为 handler {handler.GetType().Name} 执行 Unregister 时发生异常", ex);
                }
            }

            // 最后把 scanned 标记移除
            _scannedAssemblies.TryRemove(asm, out _);
            ModLogger.Info($"AttributeRouter: 已清理程序集 {ModRegistry.GetTag(asm)} 的扫描记录");
        }

        // 查找这个 accessor 的 attributes，并把它们分发给对应 handler
        private static void DispatchAccessor(Assembly asm, ReflectionAccessorBase acc, ConcurrentDictionary<IAttributeHandler, List<ReflectionAccessorBase>> handlerRecord)
        {
            if (acc == null) return;

            var attrs = acc.GetAllAttributes();
            if (attrs == null || attrs.Length == 0) return;

            foreach (var attr in attrs)
            {
                var at = attr.GetType();
                if (!_handlers.TryGetValue(at, out var handlers)) continue;

                // 为避免 handler 在执行过程中被移除/追加，先复制快照
                List<IAttributeHandler> snapshot;
                lock (handlers)
                {
                    snapshot = [.. handlers];
                }

                foreach (var h in snapshot)
                {
                    try
                    {
                        // 执行 handler
                        h.Handle(asm, acc, attr);

                        // 记录这个 handler 对这个 accessor 的操作（用于撤销）
                        var list = handlerRecord.GetOrAdd(h, _ => []);
                        lock (list) { list.Add(acc); }
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Error($"AttributeRouter: handler {h.GetType().Name} 在处理 attribute {at.Name} 时发生异常", ex);
                    }
                }
            }
        }

        // 仅用于扫描阶段的类型过滤：允许 internal/non-public 类型被扫描
        private static bool IsScannableType(Type? t)
        {
            return t != null
                   && !t.IsInterface
                   && !t.IsArray
                   && !t.IsPointer
                   && !t.IsByRef
                   && !t.IsByRefLike
                   && !t.ContainsGenericParameters;
        }
    }

    // ========== 辅助类型与扩展方法 ==========



    /// <summary>
    /// 简单的基于泛型 Attribute 的实现，使用委托包装 Handle，
    /// 并且默认不支持 Unregister。
    /// </summary>
    public sealed class SimpleAttributeHandler<TAttr> : IAttributeHandler where TAttr : Attribute
    {
        private readonly Action<Assembly, ReflectionAccessorBase, TAttr> _action;

        public SimpleAttributeHandler(Action<Assembly, ReflectionAccessorBase, TAttr> action)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
        }

        public void Handle(Assembly asm, ReflectionAccessorBase accessor, Attribute attribute)
        {
            if (attribute is TAttr attr) _action(asm, accessor, attr);
        }

        public Action<Assembly, IReadOnlyList<ReflectionAccessorBase>>? Unregister => null;
    }

    // ========== 小工具：安全获取 types（防止在枚举时某些动态/未完全加载的 assembly 抛异常） ==========
    internal static class ReflectionExtensions
    {
        public static IEnumerable<Type> GetTypesSafe(this Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null)!;
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        public static TValue GetOrAdd<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> factory)
            where TKey : notnull
        {
            if (dict.TryGetValue(key, out var v)) return v;
            return dict.GetOrAdd(key, factory(key));
        }
    }
}
