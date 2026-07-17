using UnityEngine;
using UnityEngine.UI;

public class MapView : MonoBehaviour
{
    [Header("Marker (unique)")]
    public Image enemyMarker;            // Image UI qui affiche le portrait de l’adversaire courant

    [Header("Hover Panel")]
    public EnemyPanel enemyPanelPrefab;  // Ton prefab "Enemy Panel" (avec script EnemyPanel)
    EnemyPanel enemyPanelInstance;

    void EnsurePanelInstance()
    {
        if (!enemyPanelInstance && enemyPanelPrefab)
        {
            var canvas = GetComponentInParent<Canvas>();
            enemyPanelInstance = Instantiate(enemyPanelPrefab, canvas ? canvas.transform : transform);
            enemyPanelInstance.Hide();
        }
    }

    /// <summary>
    /// Affiche l'adversaire courant (portrait + hover).
    /// Passe null pour masquer.
    /// </summary>
    public void ShowEnemy(PalierConfig.EnemyInfo info)
    {
        if (!enemyMarker) return;

        // Sprite + visibilité
        var sprite = (info != null) ? info.icon : null;
        enemyMarker.sprite = sprite;

        bool visible = (sprite != null);
        enemyMarker.enabled = visible;
        enemyMarker.gameObject.SetActive(visible);
        if (visible) enemyMarker.SetNativeSize();

        // Hover panel
        var hover = enemyMarker.GetComponent<MapEnemyHover>();
        if (!hover) hover = enemyMarker.gameObject.AddComponent<MapEnemyHover>();

        EnsurePanelInstance();
        hover.enabled = visible;
        hover.Bind(enemyPanelInstance, info);

        if (!visible && enemyPanelInstance) enemyPanelInstance.Hide();
    }

    public void HideEnemy()
    {
        if (enemyMarker)
        {
            enemyMarker.sprite = null;
            enemyMarker.enabled = false;
            enemyMarker.gameObject.SetActive(false);
        }
        if (enemyPanelInstance) enemyPanelInstance.Hide();
    }
}
