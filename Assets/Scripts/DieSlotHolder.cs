using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Marqueur posé sur le « fantôme » qui réserve la place d'un dé pendant qu'il est saisi ou
// qu'il vole vers sa destination. Sans lui, la rangée (ou la zone) se refermerait d'un coup
// sous le curseur : le dé n'aurait plus de trou où atterrir et tout le reste sauterait.
//
// `order` reprend l'ordre d'origine du dé (DieView.homeOrder) pour que le tri des slots
// reste identique, que la place soit occupée par le dé lui-même ou par son fantôme.
public class DieSlotHolder : MonoBehaviour
{
    public int order;

    // Écart entre deux « groupes » de rangement. Les dés d'un FLASH utilisent le groupe 0
    // (donc un rang < GroupStride) et se retrouvent côte à côte en tête ; tous les autres dés
    // suivent dans le groupe 1. Voir DieView.SlotOrder.
    public const int GroupStride = 1000;

    // Crée un fantôme de la taille voulue, inséré au bon rang dans le conteneur.
    public static DieSlotHolder Create(Transform container, int order, Vector2 size, float scale)
    {
        var go = new GameObject("DieSlot", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(container, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.localScale = Vector3.one * scale;

        var h = go.AddComponent<DieSlotHolder>();
        h.order = order;
        rt.SetSiblingIndex(SlotIndexFor(container, order));
        return h;
    }

    // Déplace le fantôme dans un autre conteneur (ex. le dé lâché passe de la rangée à la zone).
    // `newOrder` rafraîchit son rang : le dé a pu rejoindre le Flash entre-temps.
    public void MoveTo(Transform container, Vector2 size, float scale, int newOrder)
    {
        order = newOrder;
        var rt = (RectTransform)transform;
        rt.SetParent(container, false);
        rt.sizeDelta = size;
        rt.localScale = Vector3.one * scale;
        rt.anchoredPosition = Vector2.zero;
        rt.SetSiblingIndex(SlotIndexFor(container, order));
    }

    // Libère la place : on détache AVANT de détruire, car Destroy() n'est effectif qu'en fin de
    // frame et laisserait une case fantôme le temps que le dé prenne sa position.
    public void Release()
    {
        transform.SetParent(null, false);
        Destroy(gameObject);
    }

    // Rang d'un enfant dans un conteneur de dés : le dé lui-même, ou le fantôme qui le remplace.
    // Les décors (cadre, voile, libellé) sont exclus de la mise en page (ignoreLayout).
    public static int OrderOf(Transform child)
    {
        var d = child.GetComponent<DieView>();
        if (d) return d.SlotOrder;
        var h = child.GetComponent<DieSlotHolder>();
        if (h) return h.order;
        return int.MaxValue;
    }

    // Un enfant occupe-t-il une case de dé (dé posé ou fantôme) ? Le décor mis hors mise en
    // page (ignoreLayout) n'en fait pas partie.
    static bool IsSlot(Transform child)
    {
        var le = child.GetComponent<LayoutElement>();
        if (le && le.ignoreLayout) return false;
        return child.GetComponent<DieView>() || child.GetComponent<DieSlotHolder>();
    }

    // Réordonne les dés déjà présents dans un conteneur selon leur rang courant.
    // Indispensable pour le regroupement du Flash : un dé DÉJÀ posé dans la zone n'est pas
    // réinséré quand il rejoint (ou quitte) le Flash — sans ce passage, un dé gardé pouvait
    // rester coincé entre deux dés du Flash.
    // Appelée à chaque synchro (donc chaque frame) : elle DOIT être sans effet quand l'ordre
    // est déjà bon, sinon SetAsLastSibling salirait la mise en page en continu.
    static readonly List<Transform> _resortBuf = new List<Transform>();

    public static void Resort(Transform container)
    {
        if (!container) return;

        _resortBuf.Clear();
        bool sorted = true;
        int prev = int.MinValue;
        for (int i = 0; i < container.childCount; i++)
        {
            var c = container.GetChild(i);
            if (!IsSlot(c)) continue;
            int o = OrderOf(c);
            if (o < prev) sorted = false;
            prev = o;
            _resortBuf.Add(c);
        }

        // Rien à faire : ordre déjà correct (cas de très loin le plus fréquent).
        if (sorted || _resortBuf.Count < 2) { _resortBuf.Clear(); return; }

        _resortBuf.Sort((a, b) => OrderOf(a).CompareTo(OrderOf(b)));

        // Les cases passent en fin de fratrie dans l'ordre voulu : le décor (ignoreLayout)
        // reste devant dans la hiérarchie, donc DERRIÈRE les dés à l'affichage.
        foreach (var s in _resortBuf) s.SetAsLastSibling();
        _resortBuf.Clear();
    }

    // Index d'insertion conservant l'ordre d'origine des dés (1..5 de gauche à droite),
    // que ce soit dans la rangée ou dans la zone de sélection.
    public static int SlotIndexFor(Transform container, int order)
    {
        for (int i = 0; i < container.childCount; i++)
        {
            var c = container.GetChild(i);
            var le = c.GetComponent<LayoutElement>();
            if (le && le.ignoreLayout) continue;      // décor : ne compte pas comme un slot
            if (OrderOf(c) > order) return i;
        }
        return container.childCount;
    }
}
