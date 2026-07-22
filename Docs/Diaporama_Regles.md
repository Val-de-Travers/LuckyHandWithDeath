# Diaporama des règles — script de contenu (Figma)

Cible : **3 diapos**, format **1920 × 1080** (PNG, exportées dans `Assets/Images/rules/`,
nommées `RulesSlide1/2/3.png`).
Le texte est **cuit dans l'image** (`RulesSlideshow.slides` = `Sprite[]`, `preserveAspect = true`).
Le champ `captions[]` du composant peut porter le titre affiché sous la diapo — voir §4.

Règle d'or de lisibilité : corps de texte **≥ 30 px**, titres **≥ 64 px**, jamais plus de
**7 blocs de texte** par diapo. Tout ce qui peut être une icône de dé doit être une icône de dé.

---

## Diapo 1 — « LES DÉS » (référence de score)

**Objectif** : le joueur doit ressortir en sachant *quelle face rapporte quoi*. Zéro verbe,
que des équivalences visuelles.

### Zones

```
┌──────────────────────────────────────────────────────────────┐
│  TITRE : LES DÉS                                             │
├───────────────────────┬──────────────────────────────────────┤
│  A. Les 5 dés         │  B. CE QUI MARQUE (dés seuls)        │
│  (visuel, 40%)        │  (tableau icône → valeur)            │
├───────────────────────┴──────────────────────────────────────┤
│  C. FLASH  (3 identiques)     │  D. FULL  (5 identiques)     │
└───────────────────────────────┴──────────────────────────────┘
```

### A — Les 5 dés
Visuel : 4 dés blancs + 1 dé mis en avant (couleur/halo) = le **dé SUN**.

> **5 dés.** Faces : 2 · 3 · 4 · 5 · 6 · 10
> **Le dé SUN** remplace le 3 par un ☀ :
> il vaut **10**, ou complète un FLASH comme n'importe quelle face.

### B — Ce qui marque
Tableau à 3 lignes, icône de face à gauche, valeur à droite :

| Face | Points |
|---|---|
| `5` | **5** |
| `10` | **10** |
| `☀` | **10** |

Sous le tableau, en petit :
> Les **2, 3, 4 et 6** ne marquent rien tout seuls.

### C — FLASH
Visuel : trois dés identiques alignés, contour **orange** (c'est le feedback réel en jeu).

> **FLASH — 3 faces identiques dans le même jet.**
> 2→**20** · 3→**30** · 4→**40** · 5→**50** · 6→**60** · 10→**100**
> Une **paire + ☀** forme aussi un Flash.

### D — FULL
Visuel : cinq dés identiques.

> **FULL — les 5 dés identiques.**
> **100 × la face** (2 → 200 … 5 → 500), puis **relance obligatoire des 5 dés**.
> **Full de 6 → victoire immédiate.**
> **Full de 10 ou ☀ → SUPERNOVA : vous êtes éliminé.**

*Traiter la ligne Supernova en rouge, elle doit accrocher l'œil.*

---

## Diapo 2 — « LE TOUR » (la boucle de jeu)

**Objectif** : comprendre la tension pousse-ta-chance. À construire comme un **flowchart**,
pas comme un paragraphe. C'est la diapo la plus importante des trois.

### Zones

```
┌──────────────────────────────────────────────────────────────┐
│  TITRE : LE TOUR                                             │
├──────────────────────────────────────────────────────────────┤
│  A. Le cycle (schéma central, 55% de la surface)             │
├────────────────────────────┬─────────────────────────────────┤
│  B. LA CLAUSE (après Flash)│  C. LES 3 RÈGLES À RETENIR      │
└────────────────────────────┴─────────────────────────────────┘
```

### A — Le cycle
Boucle fléchée à 4 nœuds :

`ROLL` → `Garder les dés marquants` → `Relancer les autres ?` → **oui** retour à `ROLL` /
**non** → `BANK`

Deux sorties rouges branchées sur `ROLL` :

- **WIMPOUT** — « Aucun dé marquant et aucun Flash → **tout le score du tour est perdu**. »
- **DÉS CHAUDS** — « Les 5 dés marquent → **relance obligatoire**, impossible de banquer. »

Encadré `BANK` (vert) :
> **BANK** = vous mettez les points du tour à l'abri. Le tour passe à l'adversaire.

Encadré d'ouverture, à côté du premier `ROLL` :
> **Ouvrir : 35 points minimum.** Tant que vous n'avez pas banqué 35 points d'un coup,
> votre score reste à 0.

### B — La Clause
Visuel : les 3 dés du Flash en contour orange + 2 dés libres.

> **Un Flash doit être « dégagé ».** Deux options :
> ① **Sélectionner un 5, un 10 ou un ☀** parmi les dés libres → le Flash est validé,
>    vous pouvez banquer ou continuer.
> ② **ROLL — tenter la Clause** : relancez, et la face du Flash doit **retomber**.
>    Elle retombe → vous continuez. Elle ne retombe pas → **Wimpout**.

### C — Les 3 règles à retenir
Trois pastilles courtes :

- **Ouvrir à 35.**
- **Un Flash se dégage ou se paie.**
- **Banquer, c'est renoncer à la suite — et la garder.**

---

## Diapo 3 — « LA CAMPAGNE » (méta : paliers, artefacts, traits)

**Objectif** : montrer que la partie de dés n'est qu'un match, et qu'il y a une progression
au-dessus. Trois colonnes égales, une icône forte par colonne.

### Zones

```
┌──────────────────────────────────────────────────────────────┐
│  TITRE : LA CAMPAGNE                                         │
├──────────────────────────────────────────────────────────────┤
│  A. La carte des paliers (bandeau horizontal)                │
├──────────────┬───────────────────┬───────────────────────────┤
│ B. ARTEFACTS │  C. TRAITS        │  D. DÉFAITES              │
└──────────────┴───────────────────┴───────────────────────────┘
```

### A — Les paliers
Bandeau : 5 nœuds reliés, les 4 premiers avec 3 silhouettes d'ennemi, le 5ᵉ avec une
silhouette de boss plus grande.

> **5 paliers, 3 adversaires par palier, 1 Boss à la fin.**
> Le score à atteindre monte : **300 → 500 → 700 → 900 → 1200**.

### B — Artefacts
Visuel : 3 emplacements d'inventaire, dont un avec une carte en cours de glissement vers la table.

> **3 artefacts max.** On les **glisse sur la table** pour les déclencher.
> **Relance** · **Ajout** · **Transformation** · **Score** · **Contre-Jeu**
> Exemples : *Os du Tricheur* (relancer 1 dé) · *Dot de mariage* (×2 sur la bank)
> · *Corne de sommeil* (annuler le Flash adverse, pendant **son** tour).

Ligne en bas, mise en valeur :
> **Score en péril** : après un Wimpout, vos points clignotent au lieu de disparaître —
> un artefact glissé à temps peut encore sauver le tour.

### C — Traits
Visuel : trois portraits (ennemi / joueur / boss) avec leur icône de trait.

> **Des pouvoirs permanents, actifs toute la partie.**
> **Ennemis** — chacun porte son *Affliction* : *Bien né* (35 points offerts),
> *Happy Hour terminé* (bank forcée au-delà de 75 pts), *Pistage* (premier jet à deux 10)…
> **Joueur** — vos traits se cumulent au fil de la campagne.
> **Boss** — il en porte plusieurs à la fois : *Marée montante* (+10 pour lui à chaque
> relance), *Obole de Charon* (−10 % sur chacune de vos banks)…

### D — Défaites
Visuel : 3 crânes, un rempli.

> **3 défaites = campagne perdue.**
> Perdre contre le **Boss** met fin à la campagne immédiatement.
> Au passage d'un palier, vous pouvez **détruire un artefact pour effacer une défaite**.

---

## 4. Câblage Unity

Composant : `Assets/Scripts/RulesSlideshow.cs`

1. Exporter les 3 PNG → `Assets/Images/rules/` (`RulesSlide1.png`, `RulesSlide2.png`,
   `RulesSlide3.png`).
2. Réglages d'import, sur chaque PNG :
   - Texture Type = **Sprite (2D and UI)** ✔ déjà bon
   - Sprite Mode = **Single** ⚠ actuellement sur **Multiple** — à corriger, sinon le PNG
     n'est pas directement assignable dans `slides[]` (il faut déplier la texture pour
     attraper le sous-sprite `RulesSlideN_0`).
   - Max Size = **2048** ✔ (suffisant pour du 1920 de large)
3. Les glisser dans `slides[]` **dans l'ordre** (Dés / Tour / Campagne).
4. Optionnel — remplir `captions[]` avec exactement 3 entrées :
   `"Les dés"`, `"Le tour"`, `"La campagne"`.
5. `loop = false` (les flèches se grisent aux extrémités, c'est le comportement voulu ici).
6. `pauseGameWhileOpen = true` si le bouton Règles est accessible **pendant** un match.

Le compteur affiche automatiquement `1 / 3`, `2 / 3`, `3 / 3`.
