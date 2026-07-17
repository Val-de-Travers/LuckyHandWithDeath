using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    public const int Capacity = 3;

    [SerializeField] private List<Artifact> items = new List<Artifact>();
    public IReadOnlyList<Artifact> Items => items;
    public int Count => items.Count;

    public event Action OnChanged;

    public bool TryAdd(Artifact a)
    {
        if (!a) return false;
        if (items.Count >= Capacity) return false;
        items.Add(a);
        OnChanged?.Invoke();
        return true;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= items.Count) return;
        items.RemoveAt(index);
        OnChanged?.Invoke();
    }

    public Artifact GetAt(int index)
    {
        if (items.Count == 0) return null;
        index = ((index % items.Count) + items.Count) % items.Count; // wrap
        return items[index];
    }

    public void ClearAll()
    {
        if (items.Count == 0) return;
        items.Clear();
        OnChanged?.Invoke();
    }
}
