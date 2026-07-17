using UnityEngine;

public enum ArtifactRarity { Common, Rare, Epic }

public enum ArtifactType { Relance, Ajout, Transformation, Score, ContreJeu }

[CreateAssetMenu(menuName = "CosmicWimpout/Artifact", fileName = "Artifact")]
public class Artifact : ScriptableObject
{
    [Header("Identity")]
    public string id;
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;
    public ArtifactRarity rarity = ArtifactRarity.Common;

    public ArtifactType type ;

    [Header("Effect Stub (for later)")]
    public string effectKey; // branché plus tard
}
