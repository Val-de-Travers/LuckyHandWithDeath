# =====================================================================================
#  Reporte les shape keys de « head » sur les éléments qui la suivent par Surface Deform
#  (sourcils, moustache, barbe, rouflaquettes, cheveux), pour que le visage reste
#  solidaire une fois exporté vers Unity.
#
#  POURQUOI
#    Surface Deform est un MODIFICATEUR : il n'existe que dans Blender. Le FBX ne
#    transporte que le skinning et les blend shapes, et une blend shape appartient à UN
#    mesh — une clé posée sur « head » ne peut pas déplacer un vertex de « hair ».
#    Chaque élément doit donc porter SES PROPRES clés, portant les MÊMES NOMS.
#
#  CE QUE FAIT LE SCRIPT
#    Pour chaque élément et pour chacune des clés de « head » : met la clé à 1, laisse
#    Surface Deform faire son travail, et fige le résultat en une shape key du même nom
#    sur l'élément. Le modificateur est conservé pendant la boucle, retiré à la fin.
#
#  APRÈS LE SCRIPT
#    Les nouvelles clés ne sont pilotées par rien. C'est le JOIN qui leur donne vie :
#    en fusionnant tout dans « head », les clés de même nom se réunissent sous l'unique
#    datablock de « head » — celui qui porte déjà tes drivers. Voir le mode d'emploi
#    renvoyé par le script à la fin.
#
#  UTILISATION
#    Blender → onglet Scripting → Ouvrir ce fichier → Run Script (▶).
#    Regarde la console (Window → Toggle System Console) pour le détail.
# =====================================================================================

import bpy

# ---------------------------------- À ADAPTER ----------------------------------
HEAD = "head"

PARTS = [
    "barbe",
    "hair",
    "moustache",
    "rouflaquettes_L",
    "rouflaquettes_R",
    "sourcil_L",
    "sourcil_R",
]

# Nom du GROUPE DE SOMMETS auquel rattacher les éléments après la fusion, pour qu'ils
# suivent la rotation de la tête. Doit correspondre à ton os déformant de tête.
HEAD_BONE = "Head"

# Poser les poids sur l'os de tête pour les éléments qui n'en ont pas. Laisse à True :
# sans poids, la géométrie fusionnée resterait sur place quand la tête tourne.
ASSIGN_HEAD_WEIGHTS = True

# Aligner les noms des couches UV sur celle de head avant la fusion. Laisse à True :
# c'est ce qui empêche les éléments de devenir transparents au Ctrl+J (voir la
# docstring de unify_uv_names).
UNIFY_UV_NAMES = True
# -------------------------------------------------------------------------------


def fail(msg):
    print("\n[ÉCHEC] " + msg)
    raise RuntimeError(msg)


def relink_materials_to_data(obj):
    """Un slot de matériau peut être lié à l'OBJET ou à la MESH DATA. Ctrl+J ne garde que
    les données de mesh : tout matériau lié à l'objet est jeté au passage, et la géométrie
    retombe sur le slot 0 — celui de head. Si sa texture a de l'alpha, l'élément devient
    littéralement transparent. On rebranche donc tout sur la mesh data avant de fusionner."""
    fixed = 0
    for slot in obj.material_slots:
        if slot.link == "OBJECT":
            mat = slot.material      # lu tant que le slot pointe encore sur l'objet
            slot.link = "DATA"
            slot.material = mat      # réécrit du côté des données, où le join le trouvera
            fixed += 1
    return fixed


def unify_uv_names(head, parts):
    """Ctrl+J fusionne les couches UV **par nom**.

    Si « head » a sa couche nommée « UVMap » et « hair » la sienne « UVMap.001 », la
    fusion produit DEUX couches. Les faces des cheveux n'ont alors aucune coordonnée
    valable dans celle que lit le matériau : elles échantillonnent le texel (0, 0) de la
    texture. Sur une texture projetée, ce coin est presque toujours vide — et l'élément
    devient littéralement transparent, tout en étant bien présent dans le mesh.

    C'est la cause du « join qui rend transparent ». On aligne donc les noms avant."""
    if not head.data.uv_layers:
        print("[UV] head n'a aucune couche UV — rien à unifier.")
        return

    target = head.data.uv_layers[0].name
    print(f"[UV] couche de référence : « {target} »")

    for obj in [head] + parts:
        layers = obj.data.uv_layers
        if not layers:
            print(f"[UV] {obj.name} : AUCUNE couche UV — il sera invisible après fusion.")
            continue
        if len(layers) > 1:
            noms = ", ".join(l.name for l in layers)
            print(f"[UV] {obj.name} : {len(layers)} couches ({noms}). Seule la première est "
                  f"renommée — supprime les autres si elles ne servent pas.")

        old = layers[0].name
        if old == target:
            continue
        layers[0].name = target

        # Un nœud « UV Map » qui désignait l'ancien nom doit suivre, sinon le matériau
        # lirait une couche qui n'existe plus.
        for slot in obj.material_slots:
            mat = slot.material
            if not mat or not mat.use_nodes:
                continue
            for node in mat.node_tree.nodes:
                if node.type == "UVMAP" and node.uv_map == old:
                    node.uv_map = target
        print(f"[UV] {obj.name} : « {old} » → « {target} »")


def main():
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")

    head = bpy.data.objects.get(HEAD)
    if head is None:
        fail(f"Objet « {HEAD} » introuvable.")
    if head.data.shape_keys is None:
        fail(f"« {HEAD} » n'a aucune shape key.")

    sk = head.data.shape_keys
    names = [kb.name for kb in sk.key_blocks[1:]]  # tout sauf Basis
    if not names:
        fail(f"« {HEAD} » n'a que sa Basis.")

    print(f"\n{len(names)} clés à reporter : {', '.join(names)}")

    # Matériaux : à rebrancher AVANT le join, sinon ils disparaissent avec l'objet.
    relinked = relink_materials_to_data(head)
    for pname in PARTS:
        o = bpy.data.objects.get(pname)
        if o and o.type == "MESH":
            relinked += relink_materials_to_data(o)
    if relinked:
        print(f"{relinked} matériau(x) rebranché(s) sur la mesh data (ils étaient liés à "
              f"l'objet et auraient été perdus au Ctrl+J).")

    # Couches UV : à aligner AVANT la fusion, sinon les éléments deviennent transparents.
    if UNIFY_UV_NAMES:
        meshes = [o for o in (bpy.data.objects.get(p) for p in PARTS)
                  if o is not None and o.type == "MESH"]
        unify_uv_names(head, meshes)

    # --- 1. Neutraliser tout ce qui pourrait polluer la mesure -------------------
    #
    # Les drivers : tes clés sont pilotées par des os. Tant qu'un driver est actif, lui
    # écrire une valeur ne sert à rien — il la réécrit à l'évaluation suivante.
    anim = sk.animation_data
    drivers = list(anim.drivers) if anim else []
    for d in drivers:
        d.mute = True

    # La pose : Surface Deform lit la surface ÉVALUÉE de head, donc y compris sa
    # déformation par l'armature. En pose courante, on figerait cette pose dans chaque
    # clé. On passe donc les armatures en position de repos le temps du travail.
    rest_restore = []
    for arm in [o for o in bpy.data.objects if o.type == "ARMATURE"]:
        rest_restore.append((arm, arm.data.pose_position))
        arm.data.pose_position = "REST"

    def reset_keys():
        for kb in sk.key_blocks[1:]:
            kb.value = 0.0

    made = 0
    skipped = []

    try:
        reset_keys()
        bpy.context.view_layer.update()

        for pname in PARTS:
            part = bpy.data.objects.get(pname)
            if part is None:
                skipped.append(f"{pname} (objet introuvable)")
                continue
            if part.type != "MESH":
                skipped.append(f"{pname} (pas un mesh)")
                continue

            sd = next((m for m in part.modifiers if m.type == "SURFACE_DEFORM"), None)
            if sd is None:
                skipped.append(f"{pname} (pas de Surface Deform)")
                continue

            if part.data.shape_keys is None:
                part.shape_key_add(name="Basis", from_mix=False)

            existing = {kb.name for kb in part.data.shape_keys.key_blocks}

            for n in names:
                if n in existing:
                    continue  # déjà reporté : on ne double pas

                reset_keys()
                sk.key_blocks[n].value = 1.0
                bpy.context.view_layer.update()

                bpy.context.view_layer.objects.active = part
                part.select_set(True)
                bpy.ops.object.modifier_apply_as_shapekey(
                    keep_modifier=True, modifier=sd.name
                )
                part.data.shape_keys.key_blocks[-1].name = n
                made += 1

            print(f"  {pname} : {len(names)} clés")

            # Le modificateur a fini son office. Le laisser déformerait une deuxième
            # fois par-dessus les clés qu'on vient de créer.
            part.modifiers.remove(sd)

            # Poids sur l'os de tête : sans eux, la géométrie fusionnée resterait
            # immobile quand la tête tourne.
            if ASSIGN_HEAD_WEIGHTS and HEAD_BONE not in part.vertex_groups:
                vg = part.vertex_groups.new(name=HEAD_BONE)
                vg.add(range(len(part.data.vertices)), 1.0, "REPLACE")

        reset_keys()

    finally:
        for arm, prev in rest_restore:
            arm.data.pose_position = prev
        for d in drivers:
            d.mute = False
        bpy.context.view_layer.update()

    print(f"\n{made} shape keys créées.")
    if skipped:
        print("Ignorés : " + " | ".join(skipped))

    print(
        "\n--- ÉTAPES SUIVANTES (à la main) ---\n"
        f"1. Sélectionne les éléments, puis « {HEAD} » EN DERNIER (il doit être l'actif),\n"
        "   et fais Ctrl+J.\n"
        "   → Les clés de même nom fusionnent sous le datablock de head, celui qui\n"
        "     porte déjà tes drivers. C'est ce qui redonne vie aux nouvelles clés.\n"
        "2. Vérifie : bouge un os curseur en Pose Mode. Sourcils, moustache et barbe\n"
        "   doivent suivre le visage.\n"
        "3. Vérifie la liste des shape keys : toujours les mêmes noms, aucun « .001 ».\n"
        "4. Vérifie les matériaux : le mesh fusionné doit avoir AUTANT de slots que tu\n"
        "   avais de matériaux distincts. S'il n'en reste qu'un, les éléments prendront\n"
        "   la texture de head et deviendront transparents là où elle n'est pas peinte.\n"
        "5. Ré-exporte le FBX avec tes réglages habituels.\n"
    )


main()
