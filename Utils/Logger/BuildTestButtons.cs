using JmcModLib.Config;
using System;
using System.Reflection;

namespace JmcModLib.Utils
{
    internal partial class BuildLoggerUI
    {
        private static class BuildTestButtons
        {
            private const string ButtonText = "点击输出";
            private const string GroupName  = "日志库测试";

            private static void TestDebug(Assembly asm) => ModLogger.Debug("测试Debug", asm);
            private static void TestTrace(Assembly asm) => ModLogger.Trace("测试Trace", asm);
            private static void TestInfo(Assembly asm) => ModLogger.Info("测试Info", asm);
            private static void TestWarn(Assembly asm) => ModLogger.Warn("测试Warn", new InvalidOperationException("这是一个测试异常"), asm);
            private static void TestError(Assembly asm) => ModLogger.Error("测试Error", new InvalidOperationException("这是一个测试异常"), asm);
            private static void TestFatal(Assembly asm)
            {
                try
                {
                    ModLogger.Fatal(new InvalidOperationException("这是一个测试致命异常"), "测试Fatal", asm);
                }
                catch (Exception ex)
                {
                    ModLogger.Error("捕获到 Fatal 抛出的异常", ex);
                }
            }

            internal static void BuildUI(Assembly asm)
            {
                ConfigManager.RegisterButton("测试Trace输出", () => TestTrace(asm), ButtonText, GroupName, asm);
                ConfigManager.RegisterButton("测试Debug输出", () => TestDebug(asm), ButtonText, GroupName, asm);
                ConfigManager.RegisterButton("测试Info输出",  () => TestInfo(asm) , ButtonText, GroupName, asm);
                ConfigManager.RegisterButton("测试Warn输出",  () => TestWarn(asm) , ButtonText, GroupName, asm);
                ConfigManager.RegisterButton("测试Error输出", () => TestError(asm), ButtonText, GroupName, asm);
                ConfigManager.RegisterButton("测试Fatal输出", () => TestFatal(asm), ButtonText, GroupName, asm);
            }
        };
    }
}
