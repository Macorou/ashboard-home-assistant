# Home Assistant, module pour Ashboard Vision

Un module pour **Ashboard Vision**, le logiciel de l'écran Thermalright Trofeo Vision. Il lit ton installation dans **Home Assistant** et affiche ce qui se passe dans ton installation solaire : la production des panneaux, ta consommation, ce que tu achètes ou vends au réseau, et ta batterie si tu en as une. Les flux de courant sont dessinés et animés entre les panneaux, la maison et le réseau.

Le module **ne fait que lire** Home Assistant. Il n'envoie aucune commande et n'écrit rien dans ta configuration.

> Ce module n'est **pas affilié au projet Home Assistant**. C'est un module de la communauté pour Ashboard Vision.

## Ce qu'il affiche

- La carte **Flux d'énergie** : les panneaux, la maison, le réseau et, si tu en as une, la batterie.
- Trois dispositions : pointe du triangle en haut, maison à gauche, ou maison en bas.
- Deux styles de maison : contemporaine ou traditionnelle.
- Trois thèmes : classique, liquid-glass et pixel.
- Trois tailles : 5 × 3 (minimum et compacte), 6 × 4 (idéale) et 8 × 4 (grande).

## Ce qu'il ne fait pas

- Il ne pilote rien : pas de commande, pas d'écriture dans Home Assistant.
- Il ne lit pas la configuration Énergie de Home Assistant. Tu indiques toi-même les capteurs à utiliser.
- Il ne prend pas en charge Home Assistant installé **sur le même PC** que l'application. Pour ce cas, l'adresse `localhost` ou `127.0.0.1` est refusée, avec un message qui te l'explique.
- Pour l'instant, il n'y a qu'une seule carte. D'autres viendront plus tard.

## Installer le module

Télécharge le fichier `.avmod` de la dernière version, puis double-clique dessus. Ashboard Vision affiche alors ce que le module demande, et tu coches les permissions une à une (voir plus bas).

## Créer la clé API

Le module se connecte à Home Assistant avec une clé d'accès longue durée :

1. Dans Home Assistant, ouvre ton **profil** (en bas du menu latéral).
2. Va dans l'onglet **Sécurité**.
3. Dans la section **Jetons d'accès longue durée**, crée un jeton et donne-lui un nom, par exemple « Ashboard Vision ».
4. **Copie-le tout de suite** : Home Assistant ne l'affiche qu'une seule fois. Colle-le ensuite dans le champ « Clé API » des réglages du module.

Garde cette clé pour toi. Elle donne accès à ton Home Assistant.

## Réglages

Les réglages se trouvent dans la fenêtre de réglage de la case. L'adresse et la clé API sont partagées par toutes les cases du module : tu les saisis une seule fois.

### Connexion

- **Adresse de Home Assistant** : par exemple `192.168.1.10:8123` ou `https://maison.exemple.fr`. Sans schéma, le module suppose `http://`. La barre oblique finale est retirée toute seule.
- **Clé API** : le jeton créé plus haut.
- **Tester la connexion** : le bouton vérifie l'adresse et la clé, puis te dit ce qui marche ou ce qui coince (adresse injoignable, clé refusée, permission manquante).

Si tu te connectes en `https` à une adresse privée (réseau local, `.local`, `.lan` ou nom sans point), le module mémorise le certificat à la première connexion, car c'est souvent un certificat auto-signé. Si ce certificat change, le module te le signale : le bouton **Oublier le certificat** efface celui qui est mémorisé, et il sera appris de nouveau à la prochaine connexion.

### Affichage et disposition

- **Affichage** : « Flux d'énergie », pour l'instant le seul choix.
- **Disposition** : pointe en haut (par défaut), maison à gauche ou maison en bas.
- **Style de maison** : contemporaine (par défaut) ou traditionnelle.

### Capteurs

Tous les capteurs se choisissent dans une liste : tape un bout de leur nom, le module propose les capteurs correspondants (au plus 50), avec leur valeur actuelle et leur identifiant. Les capteurs de puissance doivent être en W ou en kW.

**Production (obligatoire)** : le capteur de puissance de tes panneaux. Sa valeur est positive.

**Réseau**, avec deux formats au choix :

- **Un seul capteur** (par défaut) : il est positif quand tu **achètes** du courant. Si le tien est positif quand tu **vends**, coche « Positif à la vente ».
- **Deux capteurs, achat et vente** : un capteur pour l'achat, un pour la vente. Leurs valeurs sont toujours positives.

**Consommation (facultatif)** : si tu n'en choisis pas, le module la calcule à partir de la production, du réseau et de la batterie. Si tu en choisis un, c'est lui qui compte.

**Batterie** : par défaut, « Pas de batterie », et aucun autre champ batterie n'apparaît. Si tu en as une, deux formats :

- **Un seul capteur de puissance** : il est positif quand la batterie se **décharge** (par défaut). Si le tien est positif quand elle **charge**, coche « Positif en charge ».
- **Deux capteurs, charge et décharge** : un capteur pour la charge, un pour la décharge. Leurs valeurs sont toujours positives.

Dans les deux cas, tu peux ajouter le **niveau de la batterie, en %**. Sans lui, la jauge reste éteinte et le pourcentage n'est pas affiché.

### Ce qui se passe quand un capteur ne répond pas

- Une production « indisponible » ou « inconnue » compte pour 0, sans erreur : beaucoup d'onduleurs s'éteignent la nuit.
- Si le capteur réseau ne répond pas, la carte passe en « hors ligne » et te dit quel capteur pose problème.
- Après trois lectures ratées de suite, la carte passe en « hors ligne » avec la cause. Les valeurs restent affichées, atténuées, et la carte revient d'elle-même dès que Home Assistant répond de nouveau.
- Une clé refusée (401) te demande de refaire une clé API. Un capteur introuvable (404) est signalé par son nom.

Une case sans réglage, ou dont un capteur obligatoire est vide, reste « non configurée » : le module ne tente aucune connexion et te dit quel champ remplir.

## Permissions

Le module demande deux permissions, et tu coches celle qui correspond à ton cas :

- **`reseau-local`** : ton Home Assistant est sur ton réseau local, à une adresse IP (par exemple `192.168.1.10`).
- **`internet`** : ton Home Assistant est accessible par un nom de domaine public, par exemple en `https`.

Si aucune des deux n'est accordée, le module ne se connecte pas et te demande d'en cocher une. Le module ne reçoit jamais plus que ce qu'il déclare, ni plus que ce que tu as accordé.

Un nom d'hôte ne permet pas de savoir laquelle des deux est utile. Si la connexion échoue avec un nom d'hôte, vérifie d'abord les permissions.

## Dépannage

- **« Clé refusée »** : la clé a été révoquée ou n'est pas la bonne. Crée-en une nouvelle dans ton profil Home Assistant, onglet Sécurité, et colle-la dans les réglages.
- **« Adresse injoignable »** : vérifie l'adresse, le port (8123 par défaut), et la permission `reseau-local` ou `internet`.
- **Un capteur est introuvable** : son identifiant a changé dans Home Assistant. Choisis-le de nouveau dans la liste.
- **Le certificat a changé** : utilise le bouton **Oublier le certificat**, puis teste la connexion.

## Pour les développeurs

Il faut le SDK d'Ashboard Vision (`AshboardVision.Sdk`, version 1.0.0) et .NET 10.

```
dotnet build -c Release
dotnet test
ashboard-module check HomeAssistant/bin/Release/net10.0 --out check-out
ashboard-module pack HomeAssistant/bin/Release/net10.0 --out pack-out
```

- `ashboard-module check` lance le module comme le logiciel le fait, et écrit une image PNG par taille et par thème dans `check-out`. Il conclut à la fin si le module est compatible.
- `ashboard-module pack` fabrique `macorou.home-assistant-1.0.0.avmod` et son empreinte `.sha256`, à joindre à une version GitHub dont l'étiquette est `v1.0.0`.

L'icône `HomeAssistant/icon.png` est produite par le test `WriteIcon` : `WRITE_ICON=1 dotnet test --filter IconTests`. Sans cette variable, le test vérifie seulement que l'icône existe et fait 256 × 256 pixels.

L'identifiant du module (`id` du manifeste) est définitif après la première publication : c'est lui qui relie la disposition et les réglages de l'utilisateur à ton module. Si tu passes à un SDK plus récent, change le numéro dans `HomeAssistant/HomeAssistant.csproj` **et** le champ `sdk` du manifeste, ensemble.

## Licence

Ce module est distribué sous licence **GPL-3.0-or-later**. Le texte complet est dans le fichier [LICENSE](LICENSE).
