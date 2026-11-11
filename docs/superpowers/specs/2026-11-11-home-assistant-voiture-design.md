# Voiture électrique sur la carte Flux d'énergie

Date : 2026-11-11. Statut : en attente d'approbation.
Version visée : 1.3.0 du module `macorou.home-assistant`.
S'appuie sur la conception de la carte : [2026-11-08-home-assistant-energy-design.md](2026-11-08-home-assistant-energy-design.md). Tout ce qui n'est pas dit ici reste comme dans ce document.
Maquettes validées : [docs/design/voiture/maquette-voiture-modele.html](../../design/voiture/maquette-voiture-modele.html) (dessin, couleurs, place) et [docs/design/voiture/maquette-voiture-etats.html](../../design/voiture/maquette-voiture-etats.html) (trois aspects). Elles se lisent dans un navigateur, à côté de leurs deux images.

**[Décidé]** marque ce que l'utilisateur a dit ou validé. **[Choix]** marque un point tranché par Claude à la rédaction, que l'utilisateur peut revoir.

## 1. Intention

L'utilisateur recharge une voiture électrique chez lui. Il veut la voir sur la carte Flux d'énergie : combien elle tire en ce moment, son pourcentage de batterie, et si elle est branchée ou non, d'un coup d'œil **[Décidé]**.

La voiture se recharge sur le courant de la maison : **sa consommation fait partie de celle de la maison**. Le flux de la voiture part donc **de la maison** vers la voiture, et ne s'ajoute pas à la consommation affichée **[Décidé]**.

Comme tout le module, l'option doit servir à n'importe qui : pas de capteur, de marque ou de modèle de l'auteur dans le code ou le manifeste.

## 2. Objectifs, non-objectifs, anti-objectifs

**Objectifs**
- Une voiture optionnelle sur la carte Flux d'énergie, dans les trois dispositions, avec et sans batterie **[Décidé]**.
- Sa puissance de charge, toujours affichée, et son pourcentage s'il est connu **[Décidé]**.
- Trois aspects : débranchée, branchée en attente, en charge **[Décidé]**.
- Trois couleurs au choix dans les réglages : bleu vif, jaune, blanc nacré **[Décidé]**.

**Non-objectifs**
- Piloter la recharge (démarrer, arrêter, changer de mode). Le module ne fait que lire.
- Une voiture dans le Bilan du jour : la valeur « Voiture » y existe déjà depuis la 1.2.0.
- Plusieurs voitures.
- Une borne de recharge dessinée.

**Anti-objectifs**
- Une voiture affichée à quelqu'un qui n'en a pas : l'option est éteinte par défaut, et ses champs restent cachés tant qu'elle l'est.
- Un flux qui part du réseau ou des panneaux vers la voiture : il part toujours de la maison **[Décidé]**.
- Ajouter la puissance de la voiture à la consommation de la maison : elle y est déjà comptée **[Décidé]**.
- Un logo, un nom ou une forme qui reprend une marque. Le dessin s'inspire d'une petite citadine électrique, sans emblème.
- Un câble qui semble relier la voiture au pylône. Ce premier essai a été écarté pour cette raison **[Décidé]**.

## 3. Réglages

À la suite des champs de la batterie, dans cet ordre :

| Clé | Genre | Libellé | Détail |
|---|---|---|---|
| `car_mode` | choice | Voiture électrique | `none` Pas de voiture (défaut), `one` Une voiture. `visibleWhen` : `display` = `energy` |
| `car_power` | lookup | Capteur de puissance de recharge | Requis si `one`. Puissance, en W ou kW. `visibleWhen` : `car_mode` = `one` |
| `car_plug` | lookup | Capteur « voiture branchée » (facultatif) | Un capteur on/off. `visibleWhen` : `car_mode` = `one` |
| `car_soc` | lookup | Niveau de la voiture, en % (facultatif) | `visibleWhen` : `car_mode` = `one` |
| `car_color` | choice | Couleur de la voiture | `blue` Bleu vif (défaut), `yellow` Jaune, `white` Blanc nacré **[Décidé : les trois couleurs]**. `visibleWhen` : `car_mode` = `one` |

- Les libellés exacts et les textes d'aide sont laissés au constructeur, dans le ton du reste du module.
- `car_mode` est une liste à deux valeurs plutôt qu'une case à cocher, pour la même raison que la batterie : `visibleWhen` ne porte que sur l'égalité avec une valeur **[Choix]**.
- Validation : `car_mode` à `one` sans `car_power` donne `NotConfigured`, avec un message qui nomme le champ à remplir.

**Recherche de capteurs** **[Choix]**
- `car_power` : comme les autres puissances, des `sensor.*` en W ou kW.
- `car_soc` : comme le niveau de la batterie, des `sensor.*` en %, avec `device_class: battery` en tête.
- `car_plug` : un nouveau genre de recherche, des **`binary_sensor.*`**. Ceux dont `device_class` vaut `plug` ou `battery_charging` passent en tête. C'est le premier champ qui cherche hors des `sensor.*`.

Chez l'auteur, les capteurs existent sous cette forme : un `binary_sensor` de type `plug` pour « branchée », une puissance en W et un pourcentage de type `battery`. Le module n'en dépend pas.

## 4. Lecture et état de la voiture

La lecture suit celle de la carte : toutes les 3 secondes, avec les autres capteurs.

**Grandeurs**
- `car` : la puissance de recharge en W, jamais négative, kW convertis comme ailleurs.
- `plugged` : vrai si `car_plug` vaut `on`, faux s'il vaut `off`, inconnu sinon (capteur absent, `unavailable`, `unknown`).
- `soc` : le pourcentage, borné de 0 à 100, ou inconnu.

**Aspect affiché** **[Choix sur les seuils]**
- L'aspect se décide sur la **lecture brute** de la puissance, pas sur la valeur adoucie de l'affichage : il change dès la lecture, pendant que le chiffre glisse vers sa nouvelle valeur comme les autres.
- **En charge** si `car` ≥ 50 W, quoi que dise `car_plug`. La puissance fait foi : une voiture qui tire du courant est forcément branchée.
- Sinon, **branchée, en attente** si `plugged` est vrai.
- Sinon, **débranchée**. C'est aussi l'aspect au repos quand il n'y a pas de capteur `car_plug` : sans lui, le module ne peut pas savoir qu'elle est branchée.

**Valeurs indisponibles** **[Choix]**, sur le modèle de la batterie
- `car_power` `unavailable` ou `unknown` : la valeur affiche « — », pas de flux, et l'aspect se décide avec `plugged` seul. La carte reste en ligne.
- `car_soc` indisponible : le pourcentage disparaît du libellé.
- Un capteur introuvable (404) ou une clé refusée se comportent comme pour les autres capteurs, y compris pour les deux capteurs facultatifs de la voiture : la carte passe hors ligne et nomme le capteur, comme elle le fait déjà pour le niveau de la batterie ou la consommation facultative **[Choix]**. Un capteur supprimé dans Home Assistant doit être corrigé dans les réglages, et le message le dit.

**Lien avec la maison**
- La consommation `H` ne change pas : capteur ou calcul, elle compte déjà la voiture **[Décidé]**. Les flux existants et leurs priorités ne changent pas.
- Le flux maison vers voiture vaut `car` **seulement quand l'aspect est « en charge »**, et 0 sinon **[Choix]**. Entre 10 et 50 W, la voiture n'est donc pas « en charge » et aucun filament ne court : l'aspect commande le flux, pour que le dessin ne dise jamais deux choses différentes.
- Si `car` dépasse `H` (capteurs décalés dans le temps), on affiche les deux valeurs telles quelles, sans corriger l'une par l'autre **[Choix]**.

## 5. Rendu

**Dessin de la voiture** **[Décidé, sur la maquette]**
- Une citadine électrique compacte à hayon, dessinée dans la même projection isométrique que la maison (`IsoPainter`, mêmes angles, même lumière), avec la face avant tournée comme la façade de la maison.
- Toit et montants noirs, vitres sombres avec reflet, phares fins et deux feux de jour verticaux qui descendent dans le pare-chocs, calandre sombre, jupes et passages de roue noirs, jantes claires à cinq branches, ombre douce au sol.
- La caisse prend la couleur choisie, ombrée selon chaque face : bleu vif `#2a63d4`, jaune `#f0b90b`, blanc nacré `#e9edf3`. Le reste du dessin est identique dans les trois couleurs.
- La maquette donne le modèle 3D (profils de la caisse et de l'habitacle, positions des roues et des feux) : le constructeur le reprend en C#. Le dessin peut être affiné, tant qu'il reste reconnaissable et qu'il ne prend aucun emblème de marque.

**Trois aspects** **[Décidé, sur la maquette]**
- **Débranchée** : voiture désaturée et à demi transparente, pas de badge, pas de flux, chiffre en gris.
- **Branchée, en attente** : voiture en couleur, petit badge rond « prise » blanc au-dessus du toit. Le tracé du flux est visible mais immobile et pâle. Chiffre en rose pâle.
- **En charge** : le badge devient un éclair rose qui pulse doucement. Le flux rose s'anime de la maison vers la voiture, avec les filaments habituels (vitesse selon la puissance). Chiffre en rose vif.
- Couleur de la voiture sur la carte : rose `#ff7eb6` pour le flux, le badge et le chiffre **[Choix]**. Il se distingue du violet de la batterie et de l'orange de l'achat.

**Textes** **[Décidé]**
- Libellé : « Voiture · débranchée · 86 % », « Voiture · branchée · 86 % » ou « Voiture · en charge · 86 % ». Sans pourcentage connu, la dernière partie disparaît.
- Valeur : la puissance de charge, **toujours affichée**, même à 0 W, au format habituel (`0 W`, `640 W`, `7,4 kW`), ou « — » si elle est indisponible.

**Place sur la carte** **[Décidé : place C, adaptée à chaque disposition]**
La voiture se pose **en bas de la carte, près de la maison**, avec un flux court depuis la maison. Les six dispositions existantes (trois pointes, avec ou sans batterie) reçoivent chacune une variante avec voiture. Repères, dans l'espace 700 × 434 :
- **Pointe en haut**, avec ou sans batterie : voiture en bas au centre, entre la maison et le pylône (largeur ≈ 110, centre ≈ 340, 388). Libellé et valeur juste à sa droite (≈ x 412, valeur plus petite, ≈ 28). C'est la maquette validée.
- **Maison à gauche, sans batterie** : voiture en bas, entre le libellé de la consommation et le pylône (largeur ≈ 128, centre ≈ 312, 372). Libellé et valeur dans la place libre du milieu de la colonne de droite (≈ x 516, y 190 et 230). C'est la maquette validée, sur la disposition de l'auteur.
- **Maison à gauche, avec batterie** : la colonne de droite passe à quatre lignes (production, batterie, voiture, réseau), avec une taille de valeur réduite si besoin pour tenir. La voiture reste à la même place.
- **Maison en bas**, avec ou sans batterie : voiture au sol, à côté de la maison, du côté où il reste de la place. Libellé dans une place libre d'une colonne ou à côté d'elle.
- Le flux maison vers voiture est une courbe courte qui part d'un coin de la maison et rejoint la voiture, sans passer sous la maison ni croiser un autre dessin.
- Sans voiture, chaque disposition reste exactement comme aujourd'hui.

Les places exactes des trois derniers cas sont au constructeur, avec la galerie comme juge (voir § 7).

**Tailles et performance**
- La carte Flux d'énergie demande toujours au moins 5 × 3 (message « Agrandis la case » en dessous ; le `minSize` du manifeste reste 2 × 1 pour le Bilan du jour). La voiture et ses textes suivent la même mise à l'échelle que le reste.
- La voiture est préparée en image une fois par taille, par couleur et par aspect, comme les autres dessins (cache existant). Seuls le badge, le flux et les halos s'animent.
- `IsAnimating` est vrai pendant la charge (filaments et badge), et suit la règle actuelle sinon.

## 6. Contraintes

- Rien ne change pour quelqu'un qui laisse `car_mode` sur « Pas de voiture » : mêmes images, mêmes valeurs, mêmes tests.
- Les réglages existants gardent leur sens. Aucune case déjà réglée ne doit perdre un réglage.
- Messages en français, au tutoiement, qui disent quoi faire.
- Aucune entité de l'auteur dans le code, les tests, le manifeste ou le README.

## 7. Critères de réussite

- Tests : lecture des trois capteurs (unités, indisponibles, `on`/`off`), choix de l'aspect (dont « en charge » sans capteur branché, et « débranchée » sans capteur), flux de la maison vers la voiture, consommation inchangée, validation `NotConfigured`, recherche des `binary_sensor`.
- La galerie produite par les tests montre, en 6 × 4 et dans le thème classique, chaque disposition avec et sans batterie, avec la voiture dans chacun des trois aspects, plus les trois couleurs, et quelques tailles 5 × 3 et 8 × 4. L'utilisateur la regarde avant la publication : c'est elle qui valide les places.
- Les images sans voiture de la galerie sont identiques à celles d'aujourd'hui.
- `ashboard-module check` conclut « Compatible ».
- README : nouvelle section sur la voiture, ses capteurs et ses trois aspects, et une image.
- Relecture finale du code par Opus 5.5 ; sous-agents de construction de Haiku 5.5 à Sonnet 5.5 selon la complexité **[Décidé]**.
- Le manifeste passe en 1.3.0 avec la construction, et le paquet est préparé. La publication se fait seulement sur demande explicite de l'utilisateur.

## 8. Laissé au constructeur

- Le découpage du code (par exemple un `CarArt` à côté de `BatteryArt`, et l'extension de `EnergyLayouts` pour les variantes avec voiture).
- Le portage du dessin de la maquette en `IsoPainter`, et ses petites retouches.
- Les places exactes de la voiture et de ses textes dans les dispositions « Maison à gauche avec batterie » et « Maison en bas », dans les limites du § 5.
- Les textes d'aide des réglages.
