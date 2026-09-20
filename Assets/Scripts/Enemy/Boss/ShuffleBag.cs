using System.Collections.Generic;

/// <summary>
/// 袋装随机(纯 C#,无 Unity 依赖,方便离线自测与复用)。
/// 语义:袋内是全集,洗牌后逐个取出;抽空自动重装全集并重新洗牌(保证一轮内每项各出一次);
/// 相邻两次抽取保证不重复(袋内还有别的项时一定换一个,只有单项池才会连续相同);
/// 只有一个静态随机源,跨实例共享不额外分配。
/// 用法:var bag = new ShuffleBag(skillSlots.GetAvailableSkills()); int index = bag.Draw();
/// </summary>
public class ShuffleBag
{
    /// <summary>随机源(纯 C#,不依赖 UnityEngine.Random)</summary>
    private static readonly System.Random Rng = new System.Random();

    /// <summary>全集(跨轮不变,只有 Reset 会改)</summary>
    private readonly List<int> _items = new List<int>();

    /// <summary>当前袋内容(抽一项少一项,空了重装)</summary>
    private readonly List<int> _bag = new List<int>();

    /// <summary>上一次抽出的项(-1 = 还没抽过,不会与任何真实项冲突——项约定为非负)</summary>
    private int _lastDrawn = -1;

    /// <summary>按连续整数建库:全集 = 0,1,...,count-1(count ≤ 0 = 空池)</summary>
    public ShuffleBag(int count)
    {
        int n = count > 0 ? count : 0;
        var items = new int[n];
        for (int i = 0; i < n; i++) items[i] = i;
        Reset(items);
    }

    /// <summary>按显式项建库(池索引可能是稀疏的,如技能池里有空槽)</summary>
    public ShuffleBag(int[] items)
    {
        Reset(items);
    }

    /// <summary>全集项数(空池 = 0)</summary>
    public int Count => _items.Count;

    /// <summary>
    /// 重建全集并丢弃当前袋(下次 Draw 时重新洗牌)。items 为空 = 空池。
    /// 注意:不清洗牌状态也能用,重建后把「上次抽到的项」一并复位(_lastDrawn = -1)。
    /// </summary>
    public void Reset(int[] items)
    {
        _items.Clear();
        if (items != null && items.Length > 0) _items.AddRange(items);
        _bag.Clear();
        _lastDrawn = -1;
    }

    /// <summary>
    /// 抽一项:袋空 → 重装全集并洗牌;抽中上次抽过的项且袋内还有别的项 → 与袋内随机另一个位置交换后再取。
    /// 空池返回 -1(调用方自行跳过,不抛异常)。
    /// </summary>
    public int Draw()
    {
        if (_items.Count == 0) return -1;

        if (_bag.Count == 0) Refill();
        if (_bag.Count == 0) return -1;   // 理论不可达(有全集就装得进袋),防御性兜底

        int pick = Rng.Next(_bag.Count);

        // 相邻不重复:抽到上次的值且袋内还有别的项 → 换成袋内随机另一个位置的值
        if (_bag.Count > 1 && _bag[pick] == _lastDrawn)
        {
            int alt = Rng.Next(_bag.Count - 1);   // [0, count-2]:去掉 pick 自己后的第 alt 个
            if (alt >= pick) alt++;               // 映射回真实下标(跳过 pick)
            // 交换:把「上次抽过的值」挪到 alt 留在袋里,让 pick 位置变成别的值 —— 取 pick(不取 alt)
            int tmp = _bag[pick];
            _bag[pick] = _bag[alt];
            _bag[alt] = tmp;
        }

        int value = _bag[pick];
        _bag.RemoveAt(pick);
        _lastDrawn = value;
        return value;
    }

    /// <summary>重装全集并 Fisher-Yates 洗牌</summary>
    private void Refill()
    {
        _bag.Clear();
        _bag.AddRange(_items);
        for (int i = _bag.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            int tmp = _bag[i];
            _bag[i] = _bag[j];
            _bag[j] = tmp;
        }
    }
}
