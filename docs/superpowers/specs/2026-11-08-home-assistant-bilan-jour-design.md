# Carte « Bilan du jour » du module Home Assistant

Date : 2026-11-08. Module `macorou.home-assistant`, version publiée 1.0.1, cette carte vise la 1.1.0.

Les choix sont marqués **[Décidé]** quand l'utilisateur les a faits, **[Choix]** quand ils ont été faits pour lui et restent à relire.

Maquette validée : `docs/design/maquette-bilan-jour.html` (taille réelle en pixels).

## 1. Intention

Le module a déjà la carte « Flux d'énergie », qui montre ce qui se passe à l'instant. Il manque le bilan : combien la maison a consommé et produit aujourd'hui, ce qui a été acheté ou vendu au réseau, et **combien la journée a coûté**. L'utilisateur veut le voir d'un coup d'œil sur l'écran Trofeo, sans ouvrir Home Assistant.

La carte doit être **petite** **[Décidé]** : elle complète le flux d'énergie sans lui prendre la place. Le module reste utilisable par n'importe qui : chacun choisit ses propres capteurs.

## 2. Objectifs et limites

Objectifs :

- Afficher jusqu'à six valeurs du jour : consommation, production, import, export, coût, gain de revente **[Décidé]**.
- Laisser l'utilisateur choisir les valeurs affichées et leur ordre dans les paramètres **[Décidé]**.
- Proposer deux formes au choix : une bande sur une ligne et une liste en hauteur **[Décidé]**.

Hors sujet :

- Pas de graphique ni d'historique.
- Le module ne calcule pas les totaux du jour et ne gère pas le passage à minuit : il lit des capteurs de Home Assistant qui le font déjà.
- Il ne calcule pas le coût à partir d'un tarif. Le coût vient d'un capteur **[Décidé : « tous les chiffres viennent de HA »]**. Seul le gain de revente est calculé.
- Il ne lit pas la configuration Énergie de Home Assistant.
- Il reste en lecture seule.

Ce qui en ferait un échec même si ça marche : une carte qui prend beaucoup de place, un gain de revente affiché chez quelqu'un qui ne revend pas, ou des réglages encombrés de champs de l'autre carte.

## 3. Les valeurs

| Valeur (`value`) | Libellé court | Source | Format |
|---|---|---|---|
| `consumption` | Conso | capteur d'énergie | kWh, une décimale |
| `production` | Prod. | capteur d'énergie | kWh, une décimale |
| `import` | Import | capteur d'énergie | kWh, une décimale |
| `export` | Export | capteur d'énergie | kWh, une décimale |
| `cost` | Coût | capteur monétaire | deux décimales, avec la devise |
| `resale` | Revente | calcul : export du jour × prix de revente | deux décimales, avec la devise |

Le **gain de revente** vaut l'export du jour multiplié par le prix de revente **[Décidé]**. Il a besoin du capteur d'export et d'un prix. Sans prix (`Aucun`), il n'apparaît pas, même s'il est coché **[Décidé]**.

Règles de lecture **[Choix]** :

- Une valeur qui n'a pas de capteur choisi n'apparaît pas, même cochée.
- Une valeur dont le capteur répond `unavailable` ou `unknown`, ou n'est pas un nombre, s'affiche « — ». La carte ne passe pas hors ligne pour autant. Le gain de revente s'affiche aussi « — » si son export ou son prix est dans ce cas.
- Unités d'énergie : `Wh`, `kWh`, `MWh` (sans tenir compte de la casse), convertis en kWh. Une unité absente ou inconnue est traitée comme des kWh, avec un avertissement dans le journal une seule fois par capteur, comme pour la carte d'énergie.
- Le prix en capteur accepte `€/kWh`, `EUR/kWh` et `c€/kWh` (converti en euros). Sans unité, c'est des €/kWh.
- Devise affichée : `EUR` s'écrit `€`. Une autre unité monétaire est affichée telle quelle. Sans unité, on écrit `€`. Pour le gain, la devise est celle du prix (partie avant `/kWh`), et `€` pour un prix tapé.
- Les nombres s'écrivent à la française, avec une virgule, quelle que soit la langue du PC.

## 4. Réglages

Le réglage existant `display` reçoit une nouvelle option, `daily`, « Bilan du jour ». Sa valeur par défaut reste `energy`. Tous les champs ci-dessous n'apparaissent que pour cette carte.

Ordre dans le formulaire, après les champs de connexion et `display` :

| Clé | Genre | Libellé | Détail |
|---|---|---|---|
| `day_shape` | choice | Forme | `strip` « Bande, une ligne » (défaut), `list` « Liste, en hauteur ». `visibleWhen` `display` = `daily` |
| `day_values` | list | Valeurs à afficher et ordre | Les six valeurs du § 3. Par défaut : les six, dans l'ordre du § 3. `visibleWhen` `display` = `daily` |
| `day_consumption` | lookup | Capteur de consommation du jour | genre énergie, `visibleWhen` `display` = `daily` |
| `day_production` | lookup | Capteur de production du jour | idem |
| `day_import` | lookup | Capteur d'import du jour | idem |
| `day_export` | lookup | Capteur d'export du jour | idem |
| `day_cost` | lookup | Capteur de coût du jour | genre monétaire, `visibleWhen` `display` = `daily` |
| `day_price_mode` | choice | Prix de revente | `none` « Aucun » (défaut), `typed` « Saisi », `sensor` « Capteur Home Assistant ». `visibleWhen` `display` = `daily` |
| `day_price` | text | Prix de revente, en €/kWh | `visibleWhen` `day_price_mode` = `typed`. Accepte `0,13` et `0.13`. Vide, invalide ou négatif : le gain s'affiche « — » |
| `day_price_sensor` | lookup | Capteur du prix de revente | genre prix, `visibleWhen` `day_price_mode` = `sensor` |

`day_values` utilise le genre `list` du SDK (`docs/sdk/reglages.md` du dépôt Ashboard Vision) : l'utilisateur coche des options et les range dans l'ordre voulu, et le réglage stocke un tableau JSON de `value`, dans cet ordre. Le manifeste déclare `options` (`value`, `label`) et `default` (tableau de `value`). À la lecture, une valeur inconnue ou en double est ignorée. Si le réglage est absent ou n'est pas un tableau, on prend l'ordre par défaut. Un tableau vide veut dire que rien n'est coché.

Le champ `number` de l'application n'accepte que des entiers : le prix tapé est donc un champ `text` **[Choix]**. Il accepte une seule virgule ou un seul point comme séparateur décimal, et toute autre écriture est invalide. Un prix de 0 est valide et donne un gain de 0,00 €.

Les recherches de capteurs s'appuient sur `EntityCatalog`, qui reçoit trois nouveaux genres **[Choix]** :

- énergie : unité `Wh`, `kWh` ou `MWh` ;
- monétaire : `device_class` `monetary`, ou unité `€` ou `EUR` ;
- prix : unité qui se termine par `/kWh`.

Le filtre se fait sur l'unité seulement, car rien de fiable ne distingue un total du jour d'un compteur à vie. Dans la liste, les capteurs dont le nom ou l'identifiant contient `jour`, `journ`, `today` ou `daily` passent en premier, et la valeur actuelle affichée dans chaque proposition permet de voir le bon **[Choix]**.

Si aucune valeur cochée n'a de source, la case est `NotConfigured` avec le message : « Choisis au moins un capteur pour le Bilan du jour. » **[Choix]**

### Ajustement des réglages du flux d'énergie

Une condition `visibleWhen` ne porte que sur un seul champ, et l'application ne l'enchaîne pas. Aujourd'hui `grid` et `grid_inverted` dépendent de `grid_mode`, dont la valeur par défaut est `one` : ils resteraient donc visibles dans une case « Bilan du jour ». **[Choix]** :

- `grid` et `grid_inverted` passent à `visibleWhen` `display` = `energy`.
- En mode « Deux capteurs », `grid` devient le capteur d'achat. Le champ `grid_import` disparaît du manifeste, et `grid_export` reste affiché seulement en mode `two`.
- `grid` prend le libellé « Capteur réseau » et l'aide « Avec deux capteurs, c'est celui de l'achat. » `grid_inverted` indique dans son aide qu'il ne sert qu'avec un seul capteur.
- Compatibilité : en mode `two`, le module lit `grid`, et à défaut l'ancienne valeur `grid_import`, pour que les cases déjà configurées continuent de marcher. Si les deux sont remplis, `grid` l'emporte, car c'est le seul des deux visible à l'écran.

Si l'on change `display` sur une case déjà réglée en deux capteurs ou avec une batterie, quelques champs du flux d'énergie restent visibles. Ce cas est rare et sans effet sur la carte : on l'accepte.

## 5. Tailles

L'écran fait 16 colonnes sur 4 lignes. La taille minimale est commune à tout le module **[contrainte de l'application]**.

- `minSize` du manifeste passe de 5 × 3 à **2 × 1**, le plus petit commun aux deux formes. Les `presets` ne changent pas.
- **Bande : au moins 4 × 1** (464 × 100 px) **[Décidé]**.
- **Liste : au moins 2 × 3** (226 × 322 px), 2 colonnes de large sur 3 lignes de haut **[Décidé]**.
- La taille se juge sur le damier (`ctx.Size.Columns` et `ctx.Size.Rows`), pas sur les pixels.
- Si la case est trop petite pour la forme choisie, elle affiche, avec `ModuleDrawing.DrawActionMessage`, un message court qui tient même en 2 × 1 : « Agrandis la case : 4 × 1 minimum » (ou « 2 × 3 minimum » pour la liste).
- Le **flux d'énergie** garde son besoin de 5 × 3. Posé dans une case de moins de 5 colonnes ou de moins de 3 lignes, il affiche « Agrandis la case : 5 × 3 minimum » **[Choix]**.
- Dans une case plus grande, le contenu s'agrandit proportionnellement et reste centré, sans dépasser. **[Choix]**

## 6. Dessin

La référence est la maquette. Fond et textes suivent le thème (`ctx.Theme`) comme pour la carte d'énergie, dans les trois thèmes classique, liquid-glass et pixel, et les tailles de texte tiennent compte de `ctx.TextScale`. Un nombre ou un libellé qui ne tiendrait pas est réduit pour tenir, jamais coupé.

Icônes et couleurs d'accent, fixes **[Choix, repris de la maquette]** :

| Valeur | Icône | Couleur |
|---|---|---|
| Conso | maison | `#8fb4ff` |
| Prod. | soleil | `#ffd166` |
| Import | flèche vers le bas sur une barre | `#ff7a45` |
| Export | flèche vers le haut sur une barre | `#34e39a` |
| Coût | symbole euro | `#ff9db0` |
| Revente | euro dans un cercle | `#5ff0c0` |

- **Bande** : une colonne par valeur, séparée par un trait fin, avec de haut en bas l'icône, le nombre, le libellé court. Pas d'unité pour les kWh, et la devise pour les montants.
- **Liste** : une ligne par valeur, séparée par un trait fin, avec l'icône, le libellé court, puis le nombre et son unité alignés à droite.
- L'espace se répartit entre les valeurs réellement affichées. L'ordre est celui de `day_values`.

La carte ne s'anime pas (`IsAnimating` vaut `false`).

## 7. Lecture et état

- La carte relit ses capteurs toutes les **30 secondes** **[Choix]** : ce sont des totaux, qui bougent lentement. Elle ne lit que les capteurs des valeurs affichées, plus celui du prix si le gain est affiché.
- Mêmes règles d'état que la carte d'énergie : une clé refusée, un capteur introuvable (404) ou un certificat refusé font passer la case hors ligne tout de suite, avec le message du module. Les autres échecs la font passer hors ligne après trois lectures ratées de suite.
- Hors ligne, les dernières valeurs restent affichées, atténuées, et la carte revient d'elle-même.
- Après un refus de clé, le module attend 60 secondes avant de réessayer, comme la carte d'énergie.

## 8. Ce qui reste au constructeur

- L'organisation du code, en suivant le découpage existant (`Cards/Daily/…`, interface `ICard`), et le partage éventuel de la boucle de lecture avec `EnergyCard`, à condition que le comportement de la carte d'énergie et ses tests ne changent pas, hormis ce que décrivent les § 4 et 5.
- Les marges et tailles exactes, tant que le rendu reste celui de la maquette.
- La documentation : le README présente la nouvelle carte, ses réglages et ses tailles, avec au moins une image rendue par le module.
- Les tests suivent l'existant, dont les images de contrôle de la galerie pour les deux formes et les trois thèmes.
