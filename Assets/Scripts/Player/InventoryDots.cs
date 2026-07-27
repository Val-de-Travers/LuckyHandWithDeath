using UnityEngine;
using UnityEngine.UI;

public class InventoryDots : MonoBehaviour
{
    public PlayerInventory inventory;
    public Image[] dots = new Image[3];
    public Sprite emptySprite;
    public Sprite fullSprite;
    public Color emptyColor = new Color(1,1,1,0.25f);
    public Color fullColor = Color.white;

    [Tooltip("Remplit la pile du BAS vers le HAUT : le dernier élément du tableau 'dots' se " +
             "remplit en premier. Décocher pour revenir au remplissage haut → bas.")]
    public bool fillFromBottom = true;

    void OnEnable()
    {
        if (inventory) inventory.OnChanged += Refresh;
        Refresh();
    }
    void OnDisable()
    {
        if (inventory) inventory.OnChanged -= Refresh;
    }

    public void Refresh()
    {
        int count = inventory ? inventory.Count : 0;
        for (int i = 0; i < dots.Length; i++)
        {
            var img = dots[i];
            if (!img) continue;

            // Bas → haut : on remplit à partir de la FIN du tableau (le tableau étant ordonné
            // du haut vers le bas dans la hiérarchie).
            bool filled = fillFromBottom ? (i >= dots.Length - count) : (i < count);
            if (fullSprite && emptySprite) img.sprite = filled ? fullSprite : emptySprite;
            img.color = filled ? fullColor : emptyColor;
        }
    }
}
