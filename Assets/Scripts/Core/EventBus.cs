using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 类型安全的事件总线 — 解耦模块间通信
/// 用法：
///   EventBus.Subscribe<ProjectileHitEvent>(OnHit);
///   EventBus.Trigger(new ProjectileHitEvent(target, dmg, pos));
///   EventBus.Unsubscribe<ProjectileHitEvent>(OnHit);
/// </summary>
public static class EventBus
{
    // [ThreadStatic] 防止多线程竞争（Unity 主线程用，但安全起见保留）
    private static readonly Dictionary<Type, Delegate> events = new();

    /// <summary>订阅事件</summary>
    public static void Subscribe<T>(Action<T> handler)
    {
        Type type = typeof(T);
        if (events.TryGetValue(type, out Delegate existing))
            events[type] = Delegate.Combine(existing, handler);
        else
            events[type] = handler;
    }

    /// <summary>取消订阅</summary>
    public static void Unsubscribe<T>(Action<T> handler)
    {
        Type type = typeof(T);
        if (events.TryGetValue(type, out Delegate existing))
        {
            Delegate result = Delegate.Remove(existing, handler);
            if (result == null)
                events.Remove(type);
            else
                events[type] = result;
        }
    }

    /// <summary>触发事件 — 所有订阅者依次收到</summary>
    public static void Trigger<T>(T args)
    {
        Type type = typeof(T);
        if (events.TryGetValue(type, out Delegate handler))
        {
            (handler as Action<T>)?.Invoke(args);
        }
    }

    /// <summary>清空所有订阅（场景切换时调用）</summary>
    public static void Clear()
    {
        events.Clear();
    }
}
