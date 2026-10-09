# Révision : commentaires et modifications

La fonction de révision fonctionne comme les outils de révision d'un traitement de texte, mais tout est écrit dans le document sous forme de texte brut, de sorte qu'un collègue, un autre éditeur et un assistant IA peuvent aussi le lire. Les marques suivent une convention publique appelée CriticMarkup :

| Ce que vous voyez | Écrit dans le fichier sous la forme |
|---|---|
| texte ajouté en vert | `{++ajouté++}` |
| texte supprimé en rouge | `{--supprimé--}` |
| un remplacement | `{~~ancien~>nouveau~~}` |
| un commentaire jaune | `{==le texte==}{>>@Nom 2026-10-07: la note<<}` |

Rien n'est conservé ailleurs, et la révision ne démarre jamais d'elle-même : saisir ou supprimer du texte ne crée pas de marques.

## Ajouter un commentaire

Sélectionnez du texte, faites un clic droit, **Ajouter un commentaire** (ou **Révision > Ajouter un commentaire**). Écrivez la note et vérifiez votre nom (au départ, c'est votre nom d'utilisateur Windows). Le texte est surligné en jaune, avec la note à côté. Elle est saisie dans le document, donc **Annuler** fonctionne.

## Comparer avec un autre fichier

**Révision > Comparer avec un autre fichier...** demande une version antérieure du document (par exemple la copie que vous avez envoyée) et le nom de l'auteur des modifications. Caret écrit les différences entre cette version et le document affiché à l'écran sous forme de révision *dans un nouvel onglet* ; votre document n'est pas modifié. Servez-vous-en pour voir ce qu'un collègue a changé dans le fichier qu'il vous a renvoyé. Si les onglets sont désactivés (**Paramètres > Onglets et fenêtres**), la révision remplace votre document dans la fenêtre : enregistrez donc votre document d'abord.

## Suivre les modifications pendant que vous éditez

**Révision > Suivre les modifications** (aussi un bouton dans le panneau Révision) retient le document tel qu’il est maintenant. Pendant que vous éditez, un instant après que vous avez cessé de taper, Caret montre ce que vous avez changé : le volet de droite de l’affichage **Fractionné** dessine le document avec les ajouts en vert et les suppressions en rouge, chacun avec votre nom et le jour, et le panneau Révision liste les modifications et les compte (aussi dans la barre d’état). Rien n’est écrit dans le fichier. **Arrêter le suivi** oublie la version de départ et laisse le document tel quel. La liste sert aussi à se déplacer dans le document : *cliquez sur une modification* et Caret y saute dans l’éditeur et la met en évidence dans le panneau de droite, quelle que soit la longueur du document. **Modification précédente** et **Modification suivante** passent de l’une à l’autre. La ✓ à côté d’une modification l’accepte (elle devient partie de la version de départ et quitte la liste) ; la ✗ la rejette (l’ancien texte revient dans le document, et **Annuler** fonctionne). **Annuler l’acceptation** reprend les dernières modifications acceptées. **Accepter toutes les modifications** et **Rejeter toutes les modifications** font de même pour toutes les modifications suivies d’un coup. Les différences dans les blocs de code ne peuvent être acceptées ou rejetées que toutes ensemble. Afficher les modifications dans l’affichage Visuel et les écrire dans le fichier sous forme de révision sont les étapes suivantes ; cette rubrique les décrira à mesure qu’elles arrivent.

## Accepter et rejeter

- *Clic droit sur une modification* (verte, rouge ou un remplacement) : **Accepter la modification** conserve ce qu'elle indique (les ajouts restent, les suppressions disparaissent) ; **Rejeter la modification** remet l'ancien texte.
- *Clic droit sur un commentaire :* **Supprimer le commentaire**.
- **Révision > Accepter toutes les modifications**, **Rejeter toutes les modifications** et **Supprimer tous les commentaires** s'appliquent à tout le document, en une seule étape d'annulation.

## Masquer les marques

**Paramètres > Éditeur > Afficher les marques de révision** désactive les couleurs ; les marques apparaissent alors comme le texte brut qu'elles sont.

## Bon à savoir

- Les marques sont du texte : un fichier révisé peut donc être enregistré, envoyé et ouvert dans n'importe quel éditeur.
- Un fichier renvoyé avec les marques d'un collègue se lit de la même façon : ses modifications apparaissent en couleur et vous les acceptez ou les rejetez.