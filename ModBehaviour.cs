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
using JmcModLib.Core.AttributeRouter;
using JmcModLib.Dependency;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace JmcModLib
{
    /// <summary>
    /// 入口类
    /// </summary>
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // 预加载的 DLL 列表
        private readonly string[] _preloadDlls =
        [
            "0Harmony.dll",
            // 以后如果有新的库，直接在这里加，例如：
            // "Newtonsoft.Json.dll",
            // "LiteDB.dll"
        ];

        private void OnEnable()
        {
            AttributeRouter.Init();
            ModRegistry.Init();
            ModLinker.Init();
            ModLogger.Info("模组已启用");
        }

        private void OnDisable()
        {
            ModLinker.Dispose();
            AttributeRouter.Dispose();
            ModRegistry.Dispose();
            ModLogger.Info("Mod 已禁用，配置已保存");
        }


        /// <summary>
        /// 在模组设置完成后调用
        /// </summary>
        protected override void OnAfterSetup()
        {
            PreloadDependencies();
            VersionInfo.modInfo = info;

            // 自注册
            ModRegistry.Register(true, VersionInfo.modInfo, VersionInfo.Name, VersionInfo.Version)?
                       .RegisterLogger(uIFlags: LogConfigUIFlags.All)
                       .RegisterL10n()
                       .Done();

            // 预加载依赖项
            ModLogger.Info("模组已启用");
        }

        /// <summary>
        /// 遍历列表，将依赖项强行载入内存
        /// </summary>
        private void PreloadDependencies()
        {
            if (_preloadDlls == null || _preloadDlls.Length == 0) return;

            string modFolder = Path.GetDirectoryName(this.info.dllPath);

            // 获取当前内存里已经加载的所有程序集
            var loadedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                loadedAssemblies.Add(asm.GetName().Name);
            }

            foreach (var dllName in _preloadDlls)
            {
                // 从文件名 "0Harmony.dll" 提取出程序集名 "0Harmony"
                string assemblyName = Path.GetFileNameWithoutExtension(dllName);

                // 查重：如果内存里已经有了，就跳过
                if (loadedAssemblies.Contains(assemblyName))
                {
                    ModLogger.Info($"依赖库已由其他 MOD 加载，跳过加载: {assemblyName}");
                    continue;
                }

                string dllPath = Path.Combine(modFolder, dllName);

                if (File.Exists(dllPath))
                {
                    try
                    {
                        // 载入 AppDomain
                        Assembly.LoadFrom(dllPath);
                        ModLogger.Info($"核心依赖已挂载: {dllName}");
                    }
                    catch (Exception ex)
                    {
                        // 捕获单个 DLL 的加载失败，不影响后续流程
                        ModLogger.Error($"加载依赖失败: {dllName}", ex);
                    }
                }
                else
                {
                    // 如果文件不存在，打印错误（因为这是写在列表里的，理应存在）
                    ModLogger.Error($"严重错误: 找不到核心依赖文件 {dllName}，路径: {dllPath}");
                }
            }
        }
    }
}