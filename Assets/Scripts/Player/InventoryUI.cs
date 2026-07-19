using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    [Header("Refs")]
    public PlayerInventory inventory;
    [SerializeField] private RectTransform panel;   // Root Game Object (le conteneur de l'UI inventaire)
    public ArtifactCardView cardView;               // Carte (icone + nom + desc)
    public Button openButton;                       // bouton pour ouvrir/fermer
    public Button leftButton;
    public Button rightButton;
    public Button closeButton;                      // optionnel
    public Button useButton;
    public TMP_Text titleLabel;                     // "ArtifactTitle" — nom de l'artefact sélectionné

    [Header("Drag & Drop")]
    [Tooltip("Zone (table de jeu) sur laquelle glisser la carte pour activer l'artefact.")]
    public ArtifactDropZone dropZone;


    [Header("State")]
    [SerializeField] private int currentIndex = 0;

    [Header("Tooltip")]
    public ArtifactTooltip tooltip;                 // TooltipPanel partagé

    [Header("Game Manager")]
    [SerializeField] private GameManager gameManager;

    public int CurrentIndex => currentIndex;
    public bool IsOpen => panel != null && panel.gameObject.activeSelf;

    void Awake()
    {
        if (openButton) openButton.onClick.AddListener(Toggle);
        if (leftButton) leftButton.onClick.AddListener(Prev);
        if (rightButton) rightButton.onClick.AddListener(Next);
        if (closeButton) closeButton.onClick.AddListener(Hide);

        if (useButton)
        {
            useButton.onClick.AddListener(OnUseClicked);
            useButton.interactable = false; // désactivé par défaut
        }

        // Panel fermé par défaut
        if (panel) panel.gameObject.SetActive(false);

        // Flèches masquées tant qu'il n'y a pas au moins 2 objets (évite le flash à l'ouverture)
        SetArrowsVisible(false);

        // Config de la carte
        if (cardView)
        {
            cardView.UpdateMode(ArtifactCardMode.Inventory);
            cardView.SetTooltip(tooltip);
            cardView.onClicked = _ => gameManager?.TryUseArtifactFromInventory(currentIndex);
            // Drag & drop sur la table : active l'artefact courant (remplace le bouton USE)
            cardView.EnableDrag(dropZone, OnCardDroppedOnTable);
        }
    }

    // Appelé quand la carte d'inventaire est déposée sur la zone table.
    void OnCardDroppedOnTable()
    {
        if (inventory == null || inventory.Count == 0) return;
        gameManager?.TryUseArtifactFromInventory(currentIndex);
    }

    void OnEnable()
    {
        if (inventory != null) inventory.OnChanged += RefreshAfterChange;
        // Informe le GameManager de l'état d'ouverture (utile pour griser "Destroy")
        gameManager?.OnInventoryOpenChanged(IsOpen);
    }

    void OnDisable()
    {
        if (inventory != null) inventory.OnChanged -= RefreshAfterChange;
        // Informe le GameManager (au cas où on disable pendant la phase d'obtention)
        gameManager?.OnInventoryOpenChanged(IsOpen);
    }

    // --- Public API ---

    public void Show()
    {
        if (panel)
        {
            panel.gameObject.SetActive(true);
            panel.SetAsLastSibling();
        }
        ClampIndex();
        RefreshCard();
        gameManager?.OnInventoryOpenChanged(IsOpen); // met à jour le bouton Destroy

        RefreshUseButtonState();
    }

    public void Hide()
    {
        if (panel) panel.gameObject.SetActive(false);
        gameManager?.OnInventoryOpenChanged(IsOpen); // met à jour le bouton Destroy

        RefreshUseButtonState();
    }

    public void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    // À appeler quand le contenu doit être re-dessiné (ex: après pick / destroy)
    public void RefreshNow()
    {
        RefreshCard();
        gameManager?.OnInventoryOpenChanged(IsOpen); // re-évalue l’état du bouton Destroy
    }

    // Détruit l'artefact actuellement affiché
    public bool TryDestroyCurrent()
    {
        if (inventory == null || inventory.Count == 0) return false;

        int idx = currentIndex;

        // Si ton PlayerInventory n'a pas RemoveAt(int), remplace par:
        // var item = inventory.GetAt(idx); inventory.Remove(item);
        inventory.RemoveAt(idx);

        // Recalage de l'index
        if (currentIndex >= inventory.Count)
            currentIndex = Mathf.Max(0, inventory.Count - 1);

        RefreshNow(); // rafraîchit la carte + notifie GameManager
        return true;
    }

    // --- Navigation ---

    void Prev()
    {
        if (inventory == null || inventory.Count == 0) return;
        currentIndex = (currentIndex - 1 + inventory.Count) % inventory.Count;
        RefreshCard();
    }

    void Next()
    {
        if (inventory == null || inventory.Count == 0) return;
        currentIndex = (currentIndex + 1) % inventory.Count;
        RefreshCard();
    }

    // --- Internes ---

    void RefreshAfterChange()
    {
        if (inventory != null && inventory.Count == 0) currentIndex = 0;
        RefreshCard();
        gameManager?.OnInventoryOpenChanged(IsOpen);
    }

    void ClampIndex()
    {
        if (inventory == null || inventory.Count == 0) { currentIndex = 0; return; }
        currentIndex = ((currentIndex % inventory.Count) + inventory.Count) % inventory.Count;
    }

    void RefreshCard()
    {
        if (!cardView) return;

        if (inventory == null || inventory.Count == 0)
        {
            cardView.Bind(null);
            if (titleLabel) titleLabel.text = "";
            SetArrowsVisible(false);
            return;
        }

        var item = inventory.GetAt(currentIndex);
        cardView.Bind(item);
        if (titleLabel) titleLabel.text = item ? item.displayName : "";

        // Les flèches n'ont de sens qu'avec au moins 2 objets : sinon on les masque
        // complètement (et pas seulement grisées) pour éviter le clignotement à l'ouverture.
        SetArrowsVisible(inventory.Count > 1);
    }

    void SetArrowsVisible(bool visible)
    {
        if (leftButton && leftButton.gameObject.activeSelf != visible)
            leftButton.gameObject.SetActive(visible);
        if (rightButton && rightButton.gameObject.activeSelf != visible)
            rightButton.gameObject.SetActive(visible);
    }

    void RefreshUseButtonState()
    {
        if (!useButton) return;
        bool hasAny = (inventory != null && inventory.Count > 0);
        useButton.interactable = IsOpen && hasAny;
    }

    void OnUseClicked()
    {
        // Protège des usages hors conditions
        if (!IsOpen) return;
        if (inventory == null || inventory.Count == 0) return;

        // Déclenche l’usage de l’artefact courant
        gameManager?.TryUseArtifactFromInventory(currentIndex);
    }

    
}
