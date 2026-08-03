using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 泛型对象池 — 提供对象复用、自动扩容、上限裁剪
/// 不依赖任何接口/虚方法，通过 factory/onGet/onReturn 回调定制行为
/// </summary>
public class ObjectPool<T> where T : Component
{
    private readonly Queue<T> pool = new();
    private readonly Func<T> factory;
    private readonly Action<T> onGet;
    private readonly Action<T> onReturn;
    private readonly int maxSize;

    public int Count => pool.Count;

    public ObjectPool(Func<T> factory, Action<T> onGet = null, Action<T> onReturn = null, int maxSize = 100)
    {
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        this.onGet = onGet;
        this.onReturn = onReturn;
        this.maxSize = maxSize > 0 ? maxSize : 100;
    }

    /// <summary>从池中取出一个对象（池空则创建新实例）</summary>
    public T Get()
    {
        T item;
        if (pool.Count > 0)
        {
            item = pool.Dequeue();
            item.gameObject.SetActive(true);
        }
        else
        {
            item = factory();
        }
        onGet?.Invoke(item);
        return item;
    }

    /// <summary>将对象归还池中（超过上限则销毁）</summary>
    public void Return(T item)
    {
        if (item == null) return;

        onReturn?.Invoke(item);
        item.gameObject.SetActive(false);

        if (pool.Count < maxSize)
            pool.Enqueue(item);
        else
            GameObject.Destroy(item.gameObject);
    }

    /// <summary>预热：提前创建指定数量的实例并存入池</summary>
    public void PreWarm(int count)
    {
        for (int i = 0; i < count; i++)
        {
            T item = factory();
            item.gameObject.SetActive(false);
            pool.Enqueue(item);
        }
    }

    /// <summary>清空池（销毁所有实例）</summary>
    public void Clear()
    {
        while (pool.Count > 0)
        {
            T item = pool.Dequeue();
            if (item != null)
                GameObject.Destroy(item.gameObject);
        }
    }
}
