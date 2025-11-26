using System;

namespace JmcModLib.Config.Entry
{
    internal interface IConfigAccessor
    {
        Type UIType { get; }
        object? GetValue();
        void SetValue(object? value);
        bool Reset();

        event Action<object?>? OnChanged;
    }

    internal interface IConfigAccessor<T> : IConfigAccessor
    {
        new T GetValue();
        void SetValue(T value);

        event Action<T>? OnChangedTyped;
    }
}
