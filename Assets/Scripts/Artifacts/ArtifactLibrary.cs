using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "CosmicWimpout/Artifact Library", fileName = "ArtifactLibrary")]
public class ArtifactLibrary : ScriptableObject
{
    public List<Artifact> artifacts = new List<Artifact>();

    public List<Artifact> GetRandomDistinct(int count, System.Random rng = null)
    {
        var pool = artifacts.FindAll(a => a != null);
        var result = new List<Artifact>();
        if (pool.Count == 0) return result;

        rng ??= new System.Random();
        int n = Mathf.Min(count, pool.Count);
        var used = new HashSet<int>();

        while (result.Count < n)
        {
            int i = rng.Next(0, pool.Count);
            if (used.Add(i)) result.Add(pool[i]);
        }
        return result;
    }
}
