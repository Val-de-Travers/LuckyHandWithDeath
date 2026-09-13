using UnityEngine;
using UnityEngine.EventSystems;

public class MapEnemyHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    EnemyPanel panel;
    PalierConfig.EnemyInfo info;

    // Fournisseur optionnel : quand il est posé, l'adversaire est relu AU MOMENT DU SURVOL au
    // lieu d'être figé à la liaison. Une liaison faite alors que l'adversaire n'était pas encore
    // connu (états de transition) ne condamne donc plus le panneau à rester vide.
    System.Func<PalierConfig.EnemyInfo> provider;

    PalierConfig.EnemyInfo Current => provider != null ? provider() : info;

    public void Bind(EnemyPanel p, PalierConfig.EnemyInfo i)
    {
        panel = p;
        info = i;
        provider = null;
    }

    public void Bind(EnemyPanel p, System.Func<PalierConfig.EnemyInfo> get)
    {
        panel = p;
        provider = get;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Le panneau s'affiche À CHAQUE survol, même sans donnée : EnemyPanel.Show gère le cas
        // nul (nom « — », description vide). Renoncer silencieusement donnait un survol qui
        // « parfois » ne répondait pas.
        if (panel) panel.Show(Current, eventData.position);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (panel && panel.gameObject.activeSelf) panel.Follow(eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (panel) panel.Hide();
    }

    // Le curseur peut quitter la zone alors que l'objet est désactivé (changement de phase,
    // panneau masqué) : OnPointerExit ne part alors jamais et l'encart resterait affiché.
    void OnDisable()
    {
        if (panel) panel.Hide();
    }
}
