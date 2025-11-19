/// <summary>
/// JmcModLib - Common library for Duckov mods
/// </summary>
/// <remarks>
/// Copyright (C) 2025 mcjiang
///
/// This file is part of JmcModLib.
///
/// JmcModLib is free software: you can redistribute it and/or modify
/// it under the terms of the GNU Lesser General Public License as published
/// by the Free Software Foundation, either version 3 of the License, or
/// (at your option) any later version.
///
/// JmcModLib is distributed in the hope that it will be useful,
/// but WITHOUT ANY WARRANTY; without even the implied warranty of
/// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  
/// See the GNU Lesser General Public License for more details.
///
/// You should have received a copy of the GNU Lesser General Public License
/// along with JmcModLib.  
/// If not, see <see href="https://www.gnu.org/licenses/lgpl-3.0.html" />.
/// </remarks>

using JmcModLib.Core;
using JmcModLib.Utils;

namespace JmcModLib
{
    /// <summary>
    /// 入口类
    /// </summary>
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        void OnEnable()
        {
            Core.VersionInfo.modInfo = info;
            ModRegistry.Register(Core.VersionInfo.modInfo, VersionInfo.Name, VersionInfo.Version, LogLevel.Trace);
            ModLogger.Info("模组已启用");
        }

        void OnDisable()
        {
            ModLogger.Info("Mod 已禁用，配置已保存");
        }

        protected override void OnAfterSetup()
        {
            ModLogger.Info("模组已启用");
            ModConfig.Load();
            Core.VersionInfo.modInfo = info;
        }

        protected virtual void OnBeforeDeactivate()
        {
            ModConfig.Save();
            ModLogger.Info("Mod 已禁用，配置已保存");
        }

    }
}
