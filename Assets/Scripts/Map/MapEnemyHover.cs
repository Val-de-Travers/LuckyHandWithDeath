using UnityEngine;
using UnityEngine.EventSystems;

public class MapEnemyHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    EnemyPanel panel;
    PalierConfig.EnemyInfo info;

    public void Bind(EnemyPanel p, PalierConfig.EnemyInfo i)
    {
        panel = p;
        info = i;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (panel) panel.Show(info, eventData.position);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (panel && panel.gameObject.activeSelf) panel.Follow(eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (panel) panel.Hide();
    }
}
