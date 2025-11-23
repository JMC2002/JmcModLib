using System;
using System.Reflection;

namespace JmcModLib.Config
{
    /// <summary>
    /// 配置存储接口
    /// </summary>
    public interface IConfigStorage
    {
        /// <summary>
        /// 获取文件名
        /// </summary>
        public string GetFileName(Assembly? asm = null);

        /// <summary>
        /// 根据 key 和 group 保存配置值
        /// </summary>
        void Save(string key, string group, object? value, Assembly? asm);

        /// <summary>
        /// 保存配置值
        /// </summary>
        /// <param name="key">配置项的key</param>
        /// <param name="group">配置项的组</param>
        /// <param name="type">值的类型</param>
        /// <param name="value">输出的值</param>
        /// <param name="asm">相关的程序集</param>
        /// <returns>是否成功加载配置</returns>
        bool TryLoad(string key, string group, Type type, out object? value, Assembly? asm);

        /// <summary>
        /// 是否存在assembly对应的配置文件
        /// </summary>
        bool Exists(Assembly? asm);

        /// <summary>
        /// 真实地将缓存写入存储介质
        /// </summary>
        void Flush(Assembly? asm);
    }
}