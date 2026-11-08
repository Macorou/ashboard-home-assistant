# Module « Home Assistant » pour Ashboard Vision, socle et carte flux d'énergie

Date : 2026-11-08. Statut : en attente d'approbation.
Sources du SDK : dépôt `Ashewos/ashboard-vision`, branche `Canary`, dossier `docs/sdk`, exemples dans `examples/`.
Maquette de référence : [`docs/design/maquette-energie.html`](../../design/maquette-energie.html). C'est la référence du rendu : dessins, couleurs, tracés des flux et emplacements.

Dans ce document, **[Décidé]** marque ce que l'utilisateur a dit ou validé explicitement. **[Choix]** marque une proposition de Claude que l'utilisateur a acceptée sans la formuler, ou un point tranché lors de la rédaction. Ce qui reste au constructeur est listé à la fin.

## 1. Intention

L'utilisateur veut voir sur son écran Thermalright Trofeo Vision 9.16 (1920 × 462) ce qui se passe dans son installation solaire, pas seulement des chiffres : des dessins vivants et des filaments de courant qui circulent entre les panneaux, la maison et le réseau. Il a déjà des cartes de flux dans Home Assistant et veut **un design neuf** pensé pour cet écran, pas une copie.

Le module doit être **utilisable par n'importe qui** : pas d'entité, d'adresse ou d'hypothèse propre à l'installation de l'auteur.

Il s'appelle « Home Assistant » parce qu'il offrira à terme plusieurs cartes. Celle-ci est la première.

## 2. Objectifs, non-objectifs, anti-objectifs

**Objectifs de cette version**
- Un socle réutilisable : connexion à Home Assistant, clé API, choix de la carte affichée.
- La carte « Flux d'énergie », avec batterie optionnelle.
- Module publiable : vérifié par `ashboard-module check`, empaqueté, mis à jour par les versions GitHub.

**Non-objectifs (plus tard)**
- Les autres cartes d'entités Home Assistant. Elles viendront une par une, chacune avec sa propre conception.
- Home Assistant installé sur le même PC que l'application (permission `ce-pc`).
- La lecture automatique de la configuration Énergie de Home Assistant : elle passe par l'API WebSocket, que le SDK ne documente pas.
- Courbe de la journée et anneau de répartition, écartés de cette carte **[Décidé]**.

**Anti-objectifs (ce serait un échec même si ça marche)**
- Reproduire à l'identique les cartes de flux existantes de Home Assistant **[Décidé]**.
- Un flux qui passe sous la maison pour rejoindre le réseau **[Décidé]**.
- Des dessins posés sur un sol **[Décidé]**, ou des flux épais **[Décidé]**.
- Une batterie affichée à quelqu'un qui n'en a pas **[Décidé]**.
- Le module qui écrit dans Home Assistant : il ne fait que lire.
- Une adresse, un capteur ou une clé de l'auteur laissés dans le code ou le manifeste.

## 3. Contraintes du SDK à respecter

- SDK 1.0, .NET 10, SkiaSharp. Une seule classe publique dérivant de `CommunityModule`, constructeur sans paramètre.
- Le module ne fait qu'une sorte de carte par manifeste, mais chaque case posée a sa propre instance et ses propres réglages. D'où un réglage « Affichage » par case.
- Une case se recrée à chaque changement de réglage.
- Toute la vie du module suit `contrat.md` : `StartAsync` rapide, pas d'exception qui s'échappe des minuteurs, `DisposeAsync` en moins de 5 s.
- Messages de statut : français, tutoiement, disent quoi faire, 200 caractères au plus, 4 changements par seconde au plus.
- Champ requis vide : `NotConfigured`, sans tentative de connexion.
- Dessin : `ctx.Bounds`, `ctx.TextScale`, `ctx.Theme`, `ctx.Now` pour animer, jamais de taille codée en dur, jamais le compte des images.
- Coût : rester sous le seuil « Consommation élevée » (2 % de processeur et 150 Mio en moyenne).
- `LookupAsync` tourne sur une instance à part où `Services.Settings` est vide : seuls les secrets y sont lisibles.

## 4. Connexion à Home Assistant

- La clé API est un jeton d'accès longue durée créé dans Home Assistant (profil, onglet Sécurité), saisi dans les réglages du module **[Décidé]**.
- **L'adresse et le jeton sont tous deux des champs `secret`** **[Choix]** : `ha_url` et `ha_token`, saisis une fois, partagés par toutes les cases. L'adresse doit être un secret parce que la recherche de capteurs ne peut lire que les secrets.
- L'adresse accepte `http://192.168.1.10:8123`, `https://maison.duckdns.org` ou `192.168.1.10:8123`. Sans schéma, on suppose `http://`. La barre finale est retirée.
- Bouton `action` « Tester la connexion » : `GET /api/` avec `Authorization: Bearer`, puis message clair (succès, adresse injoignable, jeton refusé, permission manquante).
- Permissions déclarées : `reseau-local` et `internet` **[Choix]**. L'utilisateur coche celle qui correspond à son cas. Si aucune des deux n'est accordée (`Services.Has`), statut `NotConfigured` qui dit d'en cocher une. Une adresse IP dit laquelle est utile ; un nom d'hôte ne le dit pas, et un refus se traite alors comme un échec réseau, avec un message qui rappelle de vérifier les permissions. Une adresse `localhost` ou `127.0.0.1` donne `NotConfigured` avec une phrase expliquant que ce cas n'est pas pris en charge.
- HTTPS **[Choix]** : validation normale par défaut, avec `Services.CreateHttpClient()`. Pour une adresse privée (plage privée, `.local`, `.lan`, nom sans point) en `https`, on utilise `Services.CreateHttpClient("ha")`, qui épingle le certificat auto-signé à la première connexion. On n'épingle pas les adresses publiques : leurs certificats Let's Encrypt changent tous les 60 à 90 jours, et l'épinglage les ferait refuser.
- Le jeton est relu dans le coffre à chaque besoin, jamais gardé dans un champ de log ou de message.

## 5. Réglages d'une case

Tous de type `choice`, `toggle` ou `lookup`, dans cet ordre. Les champs propres à l'énergie qui n'ont pas déjà une autre condition portent `visibleWhen` sur `display`, pour laisser la place aux futures cartes.

| Clé | Genre | Libellé | Détail |
|---|---|---|---|
| `ha_url` | secret | Adresse de Home Assistant | Partagé par le module |
| `ha_token` | secret | Clé API (jeton longue durée) | Partagé par le module |
| `test` | action | Tester la connexion | |
| `display` | choice | Affichage | Une option aujourd'hui : `energy`, « Flux d'énergie » |
| `layout` | choice | Disposition | `top` Pointe en haut (défaut), `left` Maison à gauche, `bottom` Maison en bas **[Décidé]** |
| `house` | choice | Style de maison | `modern` Contemporaine (défaut), `classic` Traditionnelle **[Décidé]** |
| `solar` | lookup | Capteur de production | Requis |
| `grid_mode` | choice | Capteurs du réseau | `one` Un seul capteur (défaut) ; `two` Deux capteurs, achat et vente |
| `grid` | lookup | Capteur réseau | Si `one` |
| `grid_inverted` | toggle | Positif à la vente | Si `one`, faux par défaut (positif à l'achat) |
| `grid_import` | lookup | Capteur d'achat | Si `two` |
| `grid_export` | lookup | Capteur de vente | Si `two` |
| `home` | lookup | Capteur de consommation (facultatif) | Vide : calculée |
| `battery_mode` | choice | Batterie | `none` Pas de batterie (défaut) ; `one` Un seul capteur de puissance ; `two` Deux capteurs, charge et décharge. Tant que c'est `none`, aucun autre champ batterie n'apparaît **[Décidé : option réservée à ceux qui en ont une]** |
| `battery` | lookup | Capteur de puissance de la batterie | Si `one` |
| `battery_inverted` | toggle | Positif en charge | Si `one`, faux par défaut (positif en décharge) |
| `battery_charge` | lookup | Capteur de charge | Si `two` |
| `battery_discharge` | lookup | Capteur de décharge | Si `two` |
| `battery_soc` | lookup | Niveau de la batterie, en % (facultatif) | Si `one`. Sans lui, jauge éteinte et pas de pourcentage |
| `battery_soc2` | lookup | Niveau de la batterie, en % (facultatif) | Si `two`. Même rôle que `battery_soc` |

Note : `visibleWhen` ne connaît que l'égalité avec une valeur, et une seule condition. D'où ces listes à deux ou trois valeurs plutôt qu'une case et un format, et les deux champs de niveau.

**Recherche de capteurs (`LookupAsync`)**
- Appelle `GET /api/states`, filtre côté module, et rend au plus 50 résultats triés par pertinence sur le nom et l'identifiant.
- Pour les champs de puissance : capteurs `sensor.*` en W ou kW. Pour `battery_soc` : capteurs en %, de préférence `device_class: battery`.
- Libellé proposé : « Nom lisible · valeur actuelle unité (`sensor.xxx`) ». Valeur stockée : l'identifiant et le nom lisible.
- Adresse ou jeton manquant : un résultat unique qui dit de remplir la connexion d'abord.

Validation au démarrage : capteur requis vide, ou batterie activée sans capteur de puissance, donne `NotConfigured` avec le nom du champ à remplir.

## 6. Lecture des données

- Relecture toutes les **3 secondes** **[Choix]**, une seule à la fois, par `GET /api/states/<entity_id>` pour chaque capteur configuré, dans un champ `volatile` remplacé en entier.
- Unité lue dans `attributes.unit_of_measurement`, conversion kW vers W. Une unité inconnue est traitée comme W et signalée dans le journal, une fois.
- Valeurs `unavailable` ou `unknown` **[Choix]** :
  - production : compte pour 0, sans erreur (beaucoup d'onduleurs s'éteignent la nuit) ;
  - réseau : statut `Offline` qui nomme le capteur ;
  - consommation facultative : retour au calcul ;
  - batterie (puissance) : compte pour 0 et sa valeur affiche « — » ; niveau : jauge éteinte.
- Échec réseau **[Choix]** : après 3 lectures ratées de suite, statut `Offline` avec la cause et quoi faire, flux arrêtés, valeurs conservées et atténuées. Retour automatique dès que ça revient.
- Jeton refusé (401) : `Offline` qui dit de refaire une clé API dans le profil Home Assistant. Capteur introuvable (404) : `Offline` qui nomme le capteur.
- Certificat épinglé qui a changé : message clair, et bouton `action` « Oublier le certificat » (`forget_cert`, avec confirmation) qui efface l'empreinte `cert:ha` par `Secrets.Set("cert:ha", null)`.

## 7. Calcul des flux

Les grandeurs de base, en W, jamais négatives : `S` production ; `imp` et `exp` achat et vente ; `chg` et `dis` charge et décharge.

Consommation `H` : le capteur s'il est choisi et disponible, sinon `max(0, S + imp − exp + dis − chg)`. Sur l'installation de l'auteur, sans batterie, ce calcul retombe exactement sur son capteur de consommation (1 124 − 352,38 = 771,62 W).

Priorité **[Choix]** : le soleil sert la maison, puis la batterie, le reste part au réseau.

```
s2h = min(S, H)                       panneaux -> maison
b2h = min(dis, H - s2h)               batterie -> maison
g2h = min(imp, H - s2h - b2h)         réseau   -> maison
s2b = min(S - s2h, chg)               panneaux -> batterie
g2b = min(imp - g2h, chg - s2b)       réseau   -> batterie
s2e = min(exp, S - s2h - s2b)         panneaux -> réseau
b2g = min(exp - s2e, dis - b2h)       batterie -> réseau
```

`b2g` emprunte le même tracé que `g2b`, en sens inverse **[Choix]**.
Un flux de moins de 10 W est considéré nul **[Choix]**, pour ne pas animer le bruit de mesure.

Valeurs affichées : production, consommation, réseau (`imp` ou `exp`, le plus grand), batterie (`chg` ou `dis`, le plus grand). Part solaire : `s2h / H` en %.

## 8. Rendu

Espace de dessin 700 × 434, mis à l'échelle uniformément et centré dans `ctx.Bounds`. Fond transparent. Les textes respectent `ctx.TextScale`.

**[Décidé]** (validé sur les maquettes)
- Design moderne sombre, dessins de panneaux, de maison et de pylône sans sol. Trois dispositions en triangle, deux styles de maison, batterie murale optionnelle : le tout tel que la maquette.
- Sans batterie, la carte est identique à la maquette « Sans batterie » ; avec batterie, à la maquette « Avec batterie ».
- Filaments fins sur leur côté du triangle, jamais sous la maison, dessinés **avant** les dessins pour passer derrière eux.

**Comportements** (validés avec la maquette)
- Filament : par tracé, `n = max(1, round(L / 110))` têtes ; queue de 20 points espacés de 3,4 px, rayon `1,5 × (1 − t/20) + 0,35`, opacité `(1 − t/20)^1,5`, tête blanche, léger halo. Vitesse `55 + kW × 75` px/s. Ligne de fond épaisseur 1,1, opacité 0,32 si le flux est actif, 0,06 sinon. Un flux nul ne montre aucun filament.
- Couleurs : production `#ffc23d`, vente `#34e39a`, achat `#ff7a45`, batterie `#b58cff`. Un flux prend la couleur de sa source, sauf panneaux vers réseau qui est vert (vente).
- Halos de fond : suivent la puissance (or pour la production, vert pour la vente, orange pour l'achat, bleu pour la maison, violet pour la batterie), avec une lente dérive.
- Fenêtres : opacité `min(1, 0,28 + H / 2400)`. Cellules des panneaux : `min(0,95, S / 3600)`, avec une variation par cellule, et un reflet qui passe, d'autant plus visible que la production est forte.
- Pylône et valeur réseau : vert à la vente, orange à l'achat. Libellé « Vendu au réseau » ou « Acheté au réseau ».
- Batterie : 5 segments suivant le niveau, le dernier qui respire en charge ou en décharge. Libellé « En charge · 64 % », « En décharge · 41 % » ou « Batterie · 64 % ».
- Nombres fins, unité plus petite : moins de 1000 W en `640 W`, au-delà en `3,42 kW`.
- Le mouvement des valeurs affichées s'adoucit entre deux lectures (environ 1 s) **[Choix]**.

**Thèmes [Choix]** : `classique`, `liquid-glass`, `pixel`. Dessins et couleurs de flux identiques partout ; textes neutres dans la couleur de `ctx.Theme`, police du thème via `ModuleDrawing`.

**Tailles [Choix]** : `minSize` 5 × 3 ; `compact` 5 × 3 ; `ideal` 6 × 4 ; `large` 8 × 4.

**Performance [Choix]** : dessins préparés une fois par taille et par style (`SKPicture` ou image en cache), seuls les filaments et les couches de lumière sont recalculés. `IsAnimating` à faux quand rien ne bouge.

## 9. Publication

- Identifiant `macorou.home-assistant`, définitif après la première publication **[Choix]**. Nom affiché « Home Assistant », auteur Macorou. La description précise que le module n'est pas affilié au projet Home Assistant.
- Dépôt public `Macorou/ashboard-home-assistant`, licence GPL-3.0-or-later **[Choix]**.
- Version `1.0.0`, `sdk` à `1.0`, `repository` renseigné, publication par `ashboard-module pack` et version GitHub `v<version>`.
- Le projet vit à la racine de `H:\DEV\Claude\Module Ashboard Vision`, sous git **[Choix]**, avec `.superpowers/` ignoré. **La création du dépôt en ligne, les envois et la publication ne se font que sur demande explicite de l'utilisateur.**
- Le SDK, le banc d'essai et le modèle `dotnet new ashboard-module` sont déjà installés.

## 10. Critères de réussite

- Tests automatiques (`AshboardVision.Sdk.Testing`) : calcul des flux, lecture des unités, formats de réseau et de batterie, statuts, avec un client HTTP simulé.
- `ashboard-module check` conclut « Compatible ». Ce banc lance le module sans secrets, il reste donc `NotConfigured` et ne montre pas le dessin.
- Une galerie d'images produite par les tests montre les trois dispositions à chaque taille et dans chaque thème, avec et sans batterie, pour le contrôle visuel.
- Essai réel en mode développeur sur le Home Assistant de l'auteur, par l'utilisateur, avec `sensor.total_active_power` et `sensor.puissance_total_des_phases_meross`.
- Module non configuré : aucune erreur `Offline` au banc d'essai.
- Relecture finale du code par Opus 5.5 **[Décidé]**. Sous-agents de construction : Haiku 5.5 à Sonnet 5.5 selon la complexité **[Décidé]**.

## 11. Laissé au constructeur

- Le découpage du code, à condition de séparer un client Home Assistant réutilisable, le calcul des flux, et chaque carte, pour que les prochaines cartes s'ajoutent sans toucher au socle.
- La façon d'obtenir le halo des filaments et les dégradés en SkiaSharp, tant que le rendu reste celui de la maquette et que le coût reste sous le seuil.
- L'icône du module, dessinée pour l'occasion, sans reprendre le logo de Home Assistant.
- Les textes exacts d'aide et de statut.
