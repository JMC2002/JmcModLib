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
        /// <summary>
        /// 在模组设置完成后调用
        /// </summary>
        protected override void OnAfterSetup()
        {
            VersionInfo.modInfo = info;
            ModRegistry.Init();
            ModRegistry.Register(true, VersionInfo.modInfo, VersionInfo.Name, VersionInfo.Version)?
                       .RegisterLogger(uIFlags: LogConfigUIFlags.All)
                       .RegisterL10n()
                       .Done();
            ModLogger.Info("模组已启用");
        }

        private void OnDisable()
        {
            ModRegistry.Dispose();
            ModLogger.Info("Mod 已禁用，配置已保存");
        }

        private void OnEnable()
        {
            ModLogger.Info("模组已启用");
        }
    }
}