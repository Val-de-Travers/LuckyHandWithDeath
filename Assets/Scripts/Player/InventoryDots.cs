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

            bool filled = i < count;
            if (fullSprite && emptySprite) img.sprite = filled ? fullSprite : emptySprite;
            img.color = filled ? fullColor : emptyColor;
        }
    }
}
