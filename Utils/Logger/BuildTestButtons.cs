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

            private static void TestDebug() => ModLogger.Debug("测试Debug");
            private static void TestTrace() => ModLogger.Trace("测试Trace");
            private static void TestInfo() => ModLogger.Info("测试Info");
            private static void TestWarn() => ModLogger.Warn("测试Warn", new InvalidOperationException("这是一个测试异常"));
            private static void TestError() => ModLogger.Error("测试Error", new InvalidOperationException("这是一个测试异常"));
            private static void TestFatal()
            {
                try
                {
                    ModLogger.Fatal(new InvalidOperationException("这是一个测试致命异常"), "测试Fatal");
                }
                catch (Exception ex)
                {
                    ModLogger.Error("捕获到 Fatal 抛出的异常", ex);
                }
            }

            internal static void BuildUI(Assembly? asm = null)
            {
                asm ??= Assembly.GetCallingAssembly();
                ConfigManager.RegisterButton("测试Trace输出", TestTrace, ButtonText, GroupName, asm);
                ConfigManager.RegisterButton("测试Debug输出", TestDebug, ButtonText, GroupName, asm);
                ConfigManager.RegisterButton("测试Info输出", TestInfo, ButtonText, GroupName, asm);
                ConfigManager.RegisterButton("测试Warn输出", TestWarn, ButtonText, GroupName, asm);
                ConfigManager.RegisterButton("测试Error输出", TestError, ButtonText, GroupName, asm);
                ConfigManager.RegisterButton("测试Fatal输出", TestFatal, ButtonText, GroupName, asm);
            }
        };
    }
}
