using JmcModLib.Config.Entry;
using JmcModLib.Config.UI;
using JmcModLib.Core;
using JmcModLib.Core.AttributeRouter;
using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace JmcModLib.Config
{
    internal class ConfigAttributeHandler : IAttributeHandler
    {
        public void Handle(Assembly asm, ReflectionAccessorBase accessor, Attribute attribute)
        {
            if (accessor is MemberAccessor mac && attribute is ConfigAttribute cfg)
            {
                var entry = ConfigManager.BuildConfigEntry(asm, mac.DeclaringType, mac, cfg);
                if (entry != null)  // 验证失败，跳过
                {
                    ConfigManager.RegisterEntry(entry);
                    ModLogger.Trace($"{ModRegistry.GetTag(asm)}: 注册 Config {mac.DeclaringType.FullName}.{mac.Name}");
                }
            }
        }

        public Action<Assembly, IReadOnlyList<ReflectionAccessorBase>>? Unregister => null;
    }

    internal class UIButtonAttributeHandler : IAttributeHandler
    {
        public void Handle(Assembly asm, ReflectionAccessorBase accessor, Attribute attribute)
        {
            if (accessor is MethodAccessor mac && attribute is UIButtonAttribute btn)
            {
                var entry = new ButtonEntry(asm, mac, btn.Group, btn.Description, null);
                ConfigUIManager.RegisterEntry(entry, btn);
            }
        }

        public Action<Assembly, IReadOnlyList<ReflectionAccessorBase>>? Unregister => null; // UIManager 可能不需要撤销
    }

}
