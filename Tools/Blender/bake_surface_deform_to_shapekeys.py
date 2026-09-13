# =====================================================================================
#  Surface Deform  ->  Shape Keys exportables en FBX
# =====================================================================================
#  POURQUOI
#    Le FBX ne sait transporter que deux déformations : le skinning par les os, et les
#    blend shapes. Surface Deform n'est ni l'un ni l'autre : il ne traversera jamais
#    l'export. Ce script transforme son résultat en shape keys, qui, eux, s'exportent.
#
#  CE QU'IL FAIT
#    Pour chaque shape key de la TÊTE, il met cette expression à 1, lit la géométrie
#    déformée de chaque élément, et la fige en shape key sur cet élément — sous le MÊME
#    NOM. Puis il pose sur chaque shape key d'élément un driver qui recopie la valeur de
#    celle de la tête : tu continues à n'animer que tes os de contrôle du visage, et tout
#    le reste suit.
#
#  IL NE TOUCHE À RIEN AVANT D'AVOIR TOUT VÉRIFIÉ
#    Une passe de contrôle liste les problèmes et s'arrête. Aucune modification n'est
#    faite tant que tout n'est pas en ordre.
#
#  PRÉREQUIS
#    1. Mirror, Subdivision et tout modificateur qui change le nombre de sommets doivent
#       être APPLIQUÉS, sur la tête comme sur les éléments. Blender refuse de les
#       appliquer une fois qu'un maillage a des shape keys : ça doit donc venir en
#       premier. Le script te dira lesquels bloquent.
#    2. Surface Deform REBINDÉ sur chaque élément (bouton « Bind »), cible = la tête.
#       Un bind fait avant un changement de topologie est invalide.
#    3. Object Mode, et enregistre ton fichier avant de lancer.
#
#  APRÈS
#    Supprime les modificateurs Surface Deform : leur rôle est terminé.
#    Puis, si tu veux : sélectionne tête + éléments et Ctrl+J. Blender fusionne les shape
#    keys par nom, et tu passes à un seul objet — donc un seul SkinnedMeshRenderer.
#
#  UTILISATION
#    Onglet Scripting -> New -> colle ce fichier -> renseigne les deux listes -> Run.
#
#  OÙ LIRE LE RAPPORT
#    Une fenêtre surgit avec le résumé, ET le rapport complet est écrit dans un bloc de
#    texte nommé « bake_report.txt » : ouvre-le dans le sélecteur de l'éditeur de texte
#    (le menu déroulant à côté du nom du script). Rien ne dépend donc de la console
#    système, invisible par défaut sous Windows.
# =====================================================================================

import bpy
import traceback

# --------------------------- RAPPORT ---------------------------
# Les print() d'un script lancé depuis l'éditeur de texte partent dans la console
# système, masquée par défaut sous Windows : un script qui « ne fait rien » a en réalité
# tout dit, sans témoin. On garde donc chaque ligne pour la réafficher DANS Blender.

REPORT = []


def say(line=""):
    REPORT.append(line)
    print(line)


def flush_report(title):
    """Écrit le rapport dans un bloc de texte et le résume dans une fenêtre surgissante."""
    try:
        txt = bpy.data.texts.get("bake_report.txt") or bpy.data.texts.new("bake_report.txt")
        txt.clear()
        txt.write("\n".join(REPORT))
    except Exception:
        pass

    try:
        lignes = [l for l in REPORT if l.strip() and not l.startswith("=")][:14]

        def draw(self, _context):
            for l in lignes:
                self.layout.label(text=l)
            self.layout.label(text="Rapport complet : bloc de texte « bake_report.txt »")

        bpy.context.window_manager.popup_menu(draw, title=title, icon='INFO')
    except Exception:
        pass


def resolve_object(name):
    """Retrouve un objet par son nom. Blender est SENSIBLE À LA CASSE : « head » et
    « Head » sont deux objets différents. On tolère l'écart, en le signalant."""
    ob = bpy.data.objects.get(name)
    if ob is not None:
        return ob, None

    bas = name.lower()
    for candidat in bpy.data.objects:
        if candidat.name.lower() == bas:
            return candidat, "%r trouvé sous le nom exact %r (casse différente)" % (name, candidat.name)
    return None, None


def mesh_objects_listing():
    noms = sorted(o.name for o in bpy.data.objects if o.type == 'MESH')
    return ", ".join(noms) if noms else "(aucun)"

# --- À RENSEIGNER --------------------------------------------------------------------

HEAD_NAME = "Head"          # l'objet qui porte les shape keys d'origine

ELEMENT_NAMES = [           # les objets qui doivent suivre le visage
    "Nez",
    "Bouche",
    "Sourcil_L",
    "Sourcil_R",
    # ... complète avec tes éléments
]

ADD_DRIVERS = True          # False : les shape keys sont créées, mais non pilotées

# -------------------------------------------------------------------------------------

# Modificateurs qui changent le nombre de sommets. Tant que l'un d'eux est actif, la
# géométrie évaluée n'a plus la même taille que le maillage de base, et aucune shape key
# ne peut en être tirée.
TOPOLOGY_TYPES = {
    'MIRROR', 'SUBSURF', 'MULTIRES', 'SOLIDIFY', 'ARRAY', 'SCREW', 'SKIN',
    'REMESH', 'DECIMATE', 'BEVEL', 'EDGE_SPLIT', 'WELD', 'WIREFRAME', 'MASK',
    'BOOLEAN', 'BUILD', 'TRIANGULATE', 'EXPLODE', 'OCEAN',
}


def surface_deform_of(ob):
    for m in ob.modifiers:
        if m.type == 'SURFACE_DEFORM':
            return m
    return None


def blocking_modifiers(ob):
    return [m for m in ob.modifiers if m.type in TOPOLOGY_TYPES and m.show_viewport]


def evaluated_coords(ob, only_modifier):
    """Coordonnées de l'objet déformé PAR CE SEUL modificateur.

    On éteint tous les autres le temps de la lecture : l'Armature ne doit pas cuire la
    pose courante dans la shape key, et un Mirror oublié ferait exploser le nombre de
    sommets. On les rallume dans tous les cas, y compris en cas d'erreur."""
    saved = [(m, m.show_viewport) for m in ob.modifiers]
    for m in ob.modifiers:
        m.show_viewport = (m is only_modifier)
    try:
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        ob_eval = ob.evaluated_get(dg)
        me = ob_eval.to_mesh()
        try:
            if len(me.vertices) != len(ob.data.vertices):
                return None
            return [v.co.copy() for v in me.vertices]
        finally:
            ob_eval.to_mesh_clear()
    finally:
        for m, state in saved:
            m.show_viewport = state


def bake_shape_key(ob, name, coords):
    """Écrit les coordonnées dans une shape key, en la RÉUTILISANT si elle existe.

    On ne supprime jamais une shape key : la retirer alors qu'un driver pointe encore
    dessus laisse une courbe orpheline, et c'est une source de plantage connue. La
    réutiliser rend aussi le script relançable sans créer de « Nez.001 »."""
    if ob.data.shape_keys is None:
        ob.shape_key_add(name="Basis", from_mix=False)

    kb = ob.data.shape_keys.key_blocks.get(name)
    if kb is None:
        kb = ob.shape_key_add(name=name, from_mix=False)

    for i, co in enumerate(coords):
        kb.data[i].co = co
    kb.value = 0.0
    return kb


def ensure_driver(element, head, name):
    """La shape key de l'élément recopie la valeur de celle de la tête.

    On ne recopie pas le driver d'os : en pointant sur la valeur finale, on reste juste
    quelle que soit la façon dont la tête est pilotée."""
    keys = element.data.shape_keys
    path = 'key_blocks["%s"].value' % name

    # On PARCOURT les drivers au lieu d'utiliser find() : find() rend None quand il ne
    # trouve rien, et passer None à remove() fait tomber Blender.
    ad = keys.animation_data
    if ad is not None:
        for fc in list(ad.drivers):
            if fc.data_path == path:
                ad.drivers.remove(fc)

    fc = keys.driver_add(path)
    drv = fc.driver
    drv.type = 'AVERAGE'

    var = drv.variables.new()
    var.name = 'v'
    var.type = 'SINGLE_PROP'
    tgt = var.targets[0]
    tgt.id_type = 'KEY'
    tgt.id = head.data.shape_keys
    tgt.data_path = path


# ============================== VÉRIFICATIONS ==============================

def collect(problems):
    """Rassemble tête et éléments, en notant tout ce qui cloche. Ne modifie rien."""
    head, note = resolve_object(HEAD_NAME)
    if note:
        say("  (i) %s" % note)
    if head is None:
        problems.append("Objet tête introuvable : %r" % HEAD_NAME)
        problems.append("Maillages présents dans le fichier : %s" % mesh_objects_listing())
        return None, []
    if head.type != 'MESH':
        problems.append("%s n'est pas un maillage." % head.name)
        return None, []

    keys = head.data.shape_keys
    if keys is None or len(keys.key_blocks) < 2:
        problems.append("%s n'a aucun shape key à convertir." % HEAD_NAME)

    for m in blocking_modifiers(head):
        problems.append("%s : le modificateur « %s » (%s) change le nombre de sommets. "
                        "Applique-le d'abord." % (HEAD_NAME, m.name, m.type))

    elements = []
    introuvables = 0
    for name in ELEMENT_NAMES:
        ob, note = resolve_object(name)
        if note:
            say("  (i) %s" % note)
        if ob is None:
            problems.append("Objet introuvable : %r" % name)
            introuvables += 1
            continue
        if ob.type != 'MESH':
            problems.append("%s n'est pas un maillage." % name)
            continue

        mod = surface_deform_of(ob)
        if mod is None:
            problems.append("%s : aucun modificateur Surface Deform." % name)
            continue
        if mod.target != head:
            problems.append("%s : le Surface Deform vise %r au lieu de %s."
                            % (name, mod.target.name if mod.target else None, HEAD_NAME))
        if not mod.is_bound:
            problems.append("%s : le Surface Deform n'est PAS bindé. Clique « Bind »." % name)

        for m in blocking_modifiers(ob):
            problems.append("%s : le modificateur « %s » (%s) change le nombre de sommets. "
                            "Applique-le d'abord." % (name, m.name, m.type))

        elements.append((ob, mod))

    if introuvables:
        problems.append("Maillages présents dans le fichier : %s" % mesh_objects_listing())
    if not elements:
        problems.append("Aucun élément exploitable.")

    return head, elements


def main():
    say("Surface Deform -> Shape Keys — Blender %s" % bpy.app.version_string)

    if bpy.context.mode != 'OBJECT':
        say("Passe en Object Mode avant de lancer le script (mode actuel : %s)."
            % bpy.context.mode)
        flush_report("Mauvais mode")
        return

    problems = []
    head, elements = collect(problems)

    if problems:
        say("")
        say("RIEN N'A ÉTÉ MODIFIÉ. %d point(s) à corriger :" % len(problems))
        for p in problems:
            say("  - %s" % p)
        flush_report("%d point(s) à corriger" % len(problems))
        return

    head_keys = head.data.shape_keys
    source_names = [kb.name for kb in head_keys.key_blocks[1:]]   # on saute « Basis »

    # État de départ propre : une expression restée à 1 se retrouverait cuite dans TOUTES
    # les shape keys produites.
    remembered = {kb.name: kb.value for kb in head_keys.key_blocks}
    for kb in head_keys.key_blocks:
        kb.value = 0.0

    say("")
    say("%d expression(s) x %d élément(s)" % (len(source_names), len(elements)))
    done, failed = 0, []

    try:
        for key_name in source_names:
            head_keys.key_blocks[key_name].value = 1.0
            bpy.context.view_layer.update()

            for ob, mod in elements:
                coords = evaluated_coords(ob, mod)
                if coords is None:
                    failed.append("%s / %s : nombre de sommets incohérent" % (ob.name, key_name))
                    continue

                bake_shape_key(ob, key_name, coords)
                if ADD_DRIVERS:
                    ensure_driver(ob, head, key_name)
                done += 1

            head_keys.key_blocks[key_name].value = 0.0
            say("  %-24s ok" % key_name)
    finally:
        # Les valeurs d'origine sont remises même si quelque chose a échoué en route.
        for name, value in remembered.items():
            if name in head_keys.key_blocks:
                head_keys.key_blocks[name].value = value
        bpy.context.view_layer.update()

    say("%d shape key(s) écrite(s)." % done)
    for f in failed:
        say("  ! %s" % f)
    say("Supprime maintenant les modificateurs Surface Deform des éléments.")
    flush_report("%d shape key(s) écrite(s)" % done)


# Filet de sécurité : une erreur Python remonterait dans la console système, elle aussi
# invisible. On la fait atterrir dans le rapport, comme le reste.
try:
    main()
except Exception:
    say("")
    say("ERREUR :")
    for l in traceback.format_exc().splitlines():
        say("  " + l)
    flush_report("Erreur — voir bake_report.txt")
    raise
