using UnityEngine;
using UnityEngine.EventSystems;

// Relais de survol posé (à l'exécution) sur chaque image de trait du Palier Panel pour
// afficher le tooltip. Un MonoBehaviour DOIT vivre dans un fichier portant son nom, sinon
// l'Inspector Unity peut échouer à créer son SerializedObject (SerializedObjectNotCreatableException).
public class PalierTraitHover : MonoBehaviour, IPointerEnterHandler, IPointerMoveHandler, IPointerExitHandler
{
    PalierPanelView _panel;
    int _segIndex, _traitIndex;

    public void Init(PalierPanelView panel, int segIndex, int traitIndex)
    {
        _panel = panel; _segIndex = segIndex; _traitIndex = traitIndex;
    }

    public void OnPointerEnter(PointerEventData e) { if (_panel) _panel.ShowTraitTooltip(_segIndex, _traitIndex, e.position); }
    public void OnPointerMove(PointerEventData e)  { if (_panel) _panel.MoveTraitTooltip(e.position); }
    public void OnPointerExit(PointerEventData e)  { if (_panel) _panel.HideTraitTooltip(); }
}
