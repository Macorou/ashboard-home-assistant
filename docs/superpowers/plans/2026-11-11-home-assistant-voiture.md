# Voiture électrique sur la carte Flux d'énergie, plan de construction

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter une voiture électrique optionnelle à la carte Flux d'énergie (module 1.3.0) : puissance de charge, pourcentage, trois aspects, trois couleurs, flux depuis la maison.

**Architecture:** La lecture gagne une `CarReading` (puissance, branchée, niveau, aspect) calculée par `EnergyReader` ; `EnergyFlows` gagne un flux `HomeToCar` qui ne vaut la puissance qu'en charge. Le dessin est un nouveau `CarArt` isométrique (trois couleurs, préparé par `ArtCache`), posé par six nouvelles variantes de `EnergyLayouts`, et `EnergyRenderer` ajoute l'atténuation, le badge et les textes. Sans voiture, rien ne change.

**Tech Stack:** C# / .NET 10, SkiaSharp 3, AshboardVision.Sdk 1.0.0, xUnit avec `AshboardVision.Sdk.Testing`.

**Spec:** [docs/superpowers/specs/2026-11-11-home-assistant-voiture-design.md](../specs/2026-11-11-home-assistant-voiture-design.md). Maquettes : [docs/design/voiture/](../../design/voiture/) (le code JavaScript de `maquette-voiture-modele.html` est la source du dessin).

## Global Constraints

- Sans voiture (`car_mode` absent ou `none`), les images, valeurs, lectures et tests existants ne changent pas.
- Le flux de la voiture part de la maison ; la consommation `H` ne change pas et n'ajoute jamais la voiture.
- « En charge » : puissance brute ≥ 50 W. Le flux `HomeToCar` vaut la puissance en charge, 0 sinon.
- Couleurs de caisse : bleu vif `#2a63d4` (défaut), jaune `#f0b90b`, blanc nacré `#e9edf3`. Couleur de la voiture sur la carte (flux, badge, chiffre en charge) : `#ff7eb6` ; chiffre en attente : `#c98aa8` ; chiffre débranchée : `ctx.Theme.TextMuted`.
- Libellés exacts : « Voiture · débranchée », « Voiture · branchée », « Voiture · en charge », suivis de « · 86 % » si le niveau est connu.
- Pas d'emblème ni de nom de marque dans le dessin, le code, les tests, le manifeste ou le README. Aucune entité de l'auteur dans le dépôt : les tests utilisent des noms comme `sensor.car_power`, `binary_sensor.car_plug`, `sensor.car_soc`.
- Messages en français, au tutoiement. Commentaires et noms de tests dans le style du projet (commentaires en français).
- Commits sur la branche de travail ; aucun envoi ni publication sans demande de l'utilisateur.

## Review Focus

1. Capteur « branchée » à `on` et puissance `unavailable` : aspect « branchée », valeur « — », carte en ligne (test en tâche 1).
2. Réglages de voiture restés dans la case alors que `car_mode` est `none` : aucune voiture, aucun capteur de voiture relu (test en tâche 1).
3. Puissance en kW (`7.4 kW`) : lue 7 400 W, en charge (test en tâche 1).
4. Consommation calculée (sans capteur maison) pendant une charge : `Home` identique avec et sans voiture (test en tâche 1).
5. Grande puissance (`11 000 W`) en 5 × 3 et en texte agrandi : le chiffre de la voiture reste dans la carte (test en tâche 5).

---

## Fichiers

| Fichier | Rôle |
|---|---|
| `HomeAssistant/Cards/Energy/CarState.cs` (créer) | `CarColor`, `CarAspect`, `CarReading`, règle de l'aspect |
| `HomeAssistant/Cards/Energy/EnergyConfig.cs` | réglages de la voiture |
| `HomeAssistant/Cards/Energy/EnergyReader.cs` | lecture des trois capteurs |
| `HomeAssistant/Cards/Energy/EnergyFlows.cs` | flux `HomeToCar` |
| `HomeAssistant/Cards/Energy/EnergyCard.cs` | avertissement d'unité hors puissances |
| `HomeAssistant/Core/EntityCatalog.cs` | recherche des `binary_sensor` |
| `HomeAssistant/manifest.json` | champs et version 1.3.0 |
| `HomeAssistant/Cards/Energy/Art/CarArt.cs` (créer), `ArtCache.cs` | dessin de la voiture |
| `HomeAssistant/Cards/Energy/EnergyLayouts.cs`, `FilamentPainter.cs` | variantes avec voiture, canal du flux |
| `HomeAssistant/Cards/Energy/EnergyRenderer.cs` | aspect, badge, textes |
| `HomeAssistant.Tests/*`, `README.md`, `docs/images/carte-voiture.png` | tests, galerie, documentation |

---

### Task 1: Réglages, lecture et flux de la voiture

**Files:**
- Create: `HomeAssistant/Cards/Energy/CarState.cs`
- Modify: `HomeAssistant/Cards/Energy/EnergyConfig.cs`, `EnergyReader.cs`, `EnergyFlows.cs`, `EnergyCard.cs`
- Test: `HomeAssistant.Tests/EnergyConfigTests.cs`, `EnergyReaderTests.cs`, `EnergyFlowsTests.cs`, `EnergyCardTests.cs`

**Interfaces:**
- Produces:
  - `internal enum CarColor { Blue, Yellow, White }`, `internal enum CarAspect { Unplugged, Waiting, Charging }`
  - `internal sealed record CarReading(double? Watts, bool? Plugged, double? SocPercent, CarAspect Aspect)`
  - `internal static class CarState { internal const double ChargingWatts = 50; internal static CarAspect AspectOf(double? watts, bool? plugged); }`
  - `EnergyConfig` : paramètres ajoutés **en fin de liste, avec valeur par défaut** : `bool HasCar = false, string CarPower = "", string CarPlug = "", string CarSoc = "", CarColor CarColor = CarColor.Blue`. `EntityIds` inclut `CarPower`, `CarPlug`, `CarSoc` non vides.
  - `EnergyInput` : `double Car = 0` en dernier paramètre. `EnergySnapshot` : `CarReading? Car = null` en dernier paramètre (null sans voiture).
  - `EnergyFlows` : propriété `double HomeToCar` (dernier paramètre positionnel, défaut 0).
  - `EnergyReader.OnOff(HaState? state) -> bool?` : `on` vrai, `off` faux (casse ignorée), sinon null.

- [ ] **Step 1: Write the failing tests**

`EnergyConfigTests` (réglages minimaux `solar` et `grid` en plus de ceux montrés) :
```csharp
[Fact] public void Car_ReadsAllFields()
// {"car_mode":"one","car_power":{"id":"sensor.car_power"},"car_plug":{"id":"binary_sensor.car_plug"},"car_soc":{"id":"sensor.car_soc"},"car_color":"yellow"}
// => HasCar, CarPower == "sensor.car_power", CarPlug == "binary_sensor.car_plug", CarSoc == "sensor.car_soc", CarColor == CarColor.Yellow,
//    EntityIds contient les trois.
[Fact] public void Car_WithoutPowerSensor_IsNotConfigured()
// {"car_mode":"one"} => false, problem == "Choisis le capteur de puissance de recharge de la voiture dans les réglages de la case."
[Fact] public void Car_ModeNone_IgnoresLeftoverFields()
// {"car_mode":"none","car_power":{"id":"sensor.car_power"}} => !HasCar, CarPower == "", EntityIds ne contient aucun "car".
[Fact] public void Car_DefaultColor_IsBlue()
```

`EnergyReaderTests` :
```csharp
[Theory]
[InlineData(7400.0, null, CarAspect.Charging)]   // la puissance fait foi, même sans capteur branché
[InlineData(7400.0, false, CarAspect.Charging)]
[InlineData(49.0, true, CarAspect.Waiting)]
[InlineData(0.0, null, CarAspect.Unplugged)]
[InlineData(null, true, CarAspect.Waiting)]
[InlineData(null, null, CarAspect.Unplugged)]
public void AspectOf(double? watts, bool? plugged, CarAspect expected)
[Fact] public void Car_Kilowatts_AreConverted()        // "7.4" kW => Watts 7400, Aspect Charging
[Fact] public void Car_PowerUnavailable_PlugOn()        // power "unavailable", plug "on" => Watts null, Plugged true, Aspect Waiting
[Fact] public void Car_SocClamped_AndUnavailableIsNull() // "104" => 100 ; "unknown" => null
[Theory] [InlineData("on", true)] [InlineData("OFF", false)] [InlineData("unavailable", null)] public void OnOff(...)
[Fact] public void NoCar_SnapshotCarIsNull()
```

`EnergyFlowsTests` :
```csharp
[Fact] public void HomeToCar_OnlyWhenCharging()
// Compute(new EnergyInput(3000, 0, 0, 0, 0, null, Car: 7400)).HomeToCar == 7400 ;
// même entrée avec Car: 40 => HomeToCar == 0.
[Fact] public void Car_DoesNotChangeHome()
// Home calculée identique pour Car: 0 et Car: 7400 (S=3000, imp=5000, exp=0).
```
`EnergyFlows.Compute` garde sa signature : le flux suit `CarState.AspectOf(input.Car, null) == Charging`, donc la puissance seule (le capteur branché ne change rien au flux).

`EnergyCardTests` :
```csharp
[Fact] public async Task Car_IsRead_AndCardStaysOnline_WhenPowerUnavailable()
// solar, grid, car_mode one, car_power "unavailable", car_plug "on", car_soc "86" % =>
// Current.Snapshot.Car == (null, true, 86, Waiting), Health Online, aucun avertissement « Unité » pour car_soc ni car_plug.
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test HomeAssistant.Tests --filter "FullyQualifiedName~Energy"`
Expected: build errors (types `CarAspect`, `CarState`… absents).

- [ ] **Step 3: Implement**

- `CarState.cs` : les types de l'interface. `AspectOf` : `watts ≥ 50` → `Charging`, sinon `plugged == true` → `Waiting`, sinon `Unplugged`.
- `EnergyConfig.TryRead` : `car_mode` (`none`/`one`, défaut `none`), puis, seulement si `one`, `car_power` (requis, message ci-dessus), `car_plug`, `car_soc`, `car_color` (`blue`/`yellow`/`white`, défaut `blue`). Laisse les champs vides si `none`.
- `EnergyReader.Combine` : si `HasCar`, lit `Watts(car_power)` (jamais négatif), `OnOff(car_plug)`, `Percent(car_soc)`, et remplit `EnergyInput.Car` (0 si inconnu) et `EnergySnapshot.Car`.
- `EnergyFlows.Compute` : `HomeToCar = aspect Charging ? BelowNoise(Clamp(input.Car)) : 0`. Rien d'autre ne change.
- `EnergyCard.WarnUnknownUnits` : ignore aussi `config.CarSoc` et `config.CarPlug`.

- [ ] **Step 4: Run all tests**

Run: `dotnet test HomeAssistant.Tests`
Expected: tous verts (les anciens tests compilent grâce aux valeurs par défaut).

- [ ] **Step 5: Commit**

```bash
git add HomeAssistant/Cards/Energy HomeAssistant.Tests
git commit -m "feat: lecture de la voiture électrique et flux maison vers voiture"
```

---

### Task 2: Manifeste et recherche des capteurs « branchée »

**Files:**
- Modify: `HomeAssistant/manifest.json`, `HomeAssistant/Core/EntityCatalog.cs`
- Test: `HomeAssistant.Tests/ModuleStartupTests.cs`, `EntityCatalogTests.cs`

**Interfaces:**
- Consumes: clés de réglage de la tâche 1.
- Produces: `EntityKind.Plug` ; `KindFor("car_plug") == Plug`, `KindFor("car_soc") == Percent`, `KindFor("car_power") == Power`.

- [ ] **Step 1: Write the failing tests**

```csharp
// ModuleStartupTests
[Fact] public void Manifest_CarFields_FollowBatteryFields_InOrder()
// juste après "battery_soc2" : car_mode, car_power, car_plug, car_soc, car_color
[Fact] public void Manifest_CarFields_Visibility()
// car_mode : visibleWhen display = energy, options none/one, défaut none ;
// les quatre autres : visibleWhen car_mode = one ; car_color : options blue/yellow/white, défaut blue
[Fact] public void Manifest_Version_Is_1_3_0()

// EntityCatalogTests
[Fact] public void Search_Plug_KeepsBinarySensors_PlugFirst()
// états : binary_sensor.door (device_class door), binary_sensor.car_plug (plug), binary_sensor.charging (battery_charging), sensor.x (W)
// => ["binary_sensor.car_plug", "binary_sensor.charging", "binary_sensor.door"] (plug et battery_charging en tête, puis par nom)
[Fact] public void Search_Plug_MatchesQuery_OnShortId()   // recherche "car" trouve binary_sensor.car_plug
[Theory] [InlineData("car_plug","Plug")] [InlineData("car_soc","Percent")] [InlineData("car_power","Power")] // dans KindFor_MapsFields
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test HomeAssistant.Tests --filter "FullyQualifiedName~ModuleStartup|FullyQualifiedName~EntityCatalog"`
Expected: FAIL.

- [ ] **Step 3: Implement**

- Manifeste : `version` 1.3.0 ; les cinq champs du § 3 de la spec, avec libellés et aides au ton du module (`car_plug` : « Un capteur on/off qui dit si la voiture est branchée. »). `car_mode` : options `none` « Pas de voiture », `one` « Une voiture ». `car_color` : `blue` « Bleu vif », `yellow` « Jaune », `white` « Blanc nacré ».
- `EntityCatalog.Search` : le préfixe accepté dépend du genre (`binary_sensor.` pour `Plug`, `sensor.` sinon), y compris pour l'identifiant court ; pour `Plug`, tout `binary_sensor` convient et `device_class` `plug` ou `battery_charging` passe en tête.

- [ ] **Step 4: Run all tests** — `dotnet test HomeAssistant.Tests`, tous verts.

- [ ] **Step 5: Commit** — `git commit -m "feat: réglages de la voiture et recherche des capteurs branchée"`

---

### Task 3: Dessin de la voiture

**Files:**
- Create: `HomeAssistant/Cards/Energy/Art/CarArt.cs`
- Modify: `HomeAssistant/Cards/Energy/Art/ArtCache.cs`
- Test: `HomeAssistant.Tests/ArtTests.cs`

**Interfaces:**
- Consumes: `CarColor` (tâche 1), `IsoPainter`, `Fill`, `Stroke`.
- Produces:
  - `ArtKind.CarBlue, ArtKind.CarYellow, ArtKind.CarWhite` ; `internal static ArtKind CarArt.KindFor(CarColor color)`.
  - `internal static void CarArt.Draw(IsoPainter p, string bodyHex)`.
  - `internal static readonly SKPoint CarArt.BadgeAnchor` : point local du centre du badge, au-dessus du toit.
  - Projection locale `Iso(10, 72, 24)` : la voiture tient dans une boîte locale d'environ 0..106 × 0..86 (à vérifier par `Ink`), son centre visuel près de (53, 43).

- [ ] **Step 1: Write the failing tests**

Ajoute les trois voitures au `[Theory]` `EveryArt_DrawsSomething_WithinBox` (boîte 110 × 90), puis :
```csharp
[Fact] public void Car_ThreeColors_DifferOnlyByBody()   // images Dark des trois couleurs différentes entre elles
[Fact] public void Car_HasNoLitImage()                  // Lit transparent, comme le pylône
[Fact] public void Car_BadgeAnchor_IsAboveTheRoof()     // BadgeAnchor.Y < plus petit Y de l'encre de la voiture + 4
```
Ajoute les trois voitures à `Art_WriteSamples`.

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test HomeAssistant.Tests --filter FullyQualifiedName~ArtTests`, FAIL.

- [ ] **Step 3: Implement**

Porte en C# la fonction `build(color)` de `docs/design/voiture/maquette-voiture-modele.html` : mêmes constantes (`W = 3.5`, `L = 8.1`, profils `body` et `cabin`, roues en y 1.4 et 6.55, rayon 0.78), même règle de faces visibles (`ny + nz > 0`), même lumière (`Ld = normalize(0.45, -0.25, 1)`, facteur `0.6 + 0.48 · max(0, n·Ld)`), mêmes détails et couleurs fixes. La maquette projette avec `(x−y)·8.66, (x+y)·5 − z·10` : c'est `Iso(10, …)`. Les dégradés `side`, `glass`, `rim`, `grille` deviennent des `Fill.Box`. L'ombre au sol : polygone noir à 0,5 avec `glow: true` (pas de flou séparé). Les feux de jour avec `glow: true`. `HasLit` reste faux pour la voiture.

- [ ] **Step 4: Run all tests, then look at the samples** — `dotnet test HomeAssistant.Tests`, tous verts ; ouvre les échantillons des trois voitures écrits par `Art_WriteSamples` et compare-les à la maquette.

- [ ] **Step 5: Commit** — `git commit -m "feat: dessin isométrique de la voiture en trois couleurs"`

---

### Task 4: Dispositions avec voiture et canal du flux

**Files:**
- Modify: `HomeAssistant/Cards/Energy/EnergyLayouts.cs`, `FilamentPainter.cs`
- Test: `HomeAssistant.Tests/EnergyRendererTests.cs`

**Interfaces:**
- Consumes: `ArtKind.CarBlue` (tâche 3, posé comme la maison : le moteur remplace la couleur), `EnergyFlows.HomeToCar` (tâche 1).
- Produces:
  - `FlowKey.HomeToCar` (valeur 6). Dans `FilamentPainter` : `Channels = 8`, `BatteryToGridChannel = 7` ; `PowerOf(flows, HomeToCar) == flows.HomeToCar`.
  - `EnergyLayouts.Get(LayoutKind kind, bool withBattery, bool withCar)` ; l'ancien `Get(kind, withBattery)` reste et vaut `withCar: false`.
  - Rôles de texte `carLabel`, `carValue`.
  - `FilamentPainter.Draw(SKCanvas canvas, EnergyLayout layout, EnergyFlows flows, float carLineOpacity = -1)` : `-1` garde la règle habituelle ; `0` ne trace pas le chemin de la voiture ; une autre valeur fixe l'opacité de sa ligne de fond quand le flux est nul.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory] // les six combinaisons
public void Layouts_WithCar_AddCarArt_Flow_AndSlots(string layout, bool battery)
// Get(kind, battery, true) : une seule ArtPlacement CarBlue, un FlowPath HomeToCar dont le premier point est près de la maison
// (dans sa boîte de placement agrandie de 40), un slot carLabel et un carValue, tous dans 0..700 × 0..434.
[Theory] public void Layouts_WithoutCar_AreUnchanged(string layout, bool battery)
// Get(kind, battery, false) a les mêmes Arts, Flows et Texts que Get(kind, battery)
[Fact] public void Filament_HomeToCar_HasItsOwnChannel()
// Advance(1, flows avec HomeToCar 7400 et BatteryToGrid 500) : PhaseOf(HomeToCar) == SpeedFor(7400), PhaseOf(GridToBattery, true) == SpeedFor(500)
```

- [ ] **Step 2: Run tests to verify they fail** — FAIL.

- [ ] **Step 3: Implement**

Une fonction `WithCar(EnergyLayout base, LayoutKind kind, bool bat)` ajoute à la disposition de base ce qui suit (espace 700 × 434 ; échelle de la voiture = largeur voulue / 106) :
- **Top**, avec ou sans batterie : voiture de largeur ≈ 110 centrée en (340, 388) ; flux `HomeToCar` rose `#ff7eb6` (226, 330) → (250, 352) → (270, 362) → (290, 368) ; `carLabel` (412, 380, gauche, 14), `carValue` (412, 414, gauche, 28).
- **Left sans batterie** : voiture ≈ 128 centrée en (312, 372) ; flux (262, 262) → (278, 290) → (292, 312) → (300, 330) ; `carLabel` (515, 196), `carValue` (515, 244, 42).
- **Left avec batterie** : même voiture et même flux ; colonne de droite sur quatre lignes, dans cet ordre production, batterie, voiture, réseau : libellés en y 44, 152, 260, 368, valeurs 44 plus bas, taille 38.
- **Bottom**, avec ou sans batterie : point de départ : voiture ≈ 100 au sol à droite de la maison, centrée en (590, 395) ; libellés de la voiture dans la colonne de droite au-dessus d'elle ; colonnes resserrées (valeurs 34) si besoin. Ces deux variantes se règlent à l'œil sur la galerie (tâche 6) ; les tests ne figent que les règles du Step 1.

- [ ] **Step 4: Run all tests** — tous verts.

- [ ] **Step 5: Commit** — `git commit -m "feat: dispositions avec voiture et flux maison vers voiture"`

---

### Task 5: Rendu de la voiture

**Files:**
- Modify: `HomeAssistant/Cards/Energy/EnergyRenderer.cs`
- Test: `HomeAssistant.Tests/EnergyRendererTests.cs`

**Interfaces:**
- Consumes: tout ce qui précède.
- Produces: rien de nouveau pour les autres tâches.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory] [InlineData(CarAspect.Unplugged)] [InlineData(CarAspect.Waiting)] [InlineData(CarAspect.Charging)]
public void Render_Car_DrawsTheCar_InEachAspect(CarAspect aspect)
// disposition left sans batterie, 6 × 4 : de l'encre dans la boîte de la voiture
[Fact] public void Render_Unplugged_IsDimmerThanWaiting()
// somme des alpha des pixels de la boîte de la voiture : débranchée < 0,75 × en attente
[Fact] public void Render_Charging_ShowsPinkValue_AndAnimates()
// pixels proches de #ff7eb6 dans la zone carValue ; IsAnimating vrai
[Fact] public void Render_Waiting_DoesNotAnimate_WhenNothingElseMoves()
// solaire 0, réseau 0, voiture en attente (0 W, branchée) => IsAnimating faux une fois les valeurs posées
[Fact] public void Render_CarModeNone_SameAsBefore()
// rendu pixel à pixel identique entre une config sans champs voiture et la même avec car_mode none + champs restés
[Fact] public void Render_CarPowerUnknown_ShowsDash()
[Fact] public void Render_HugeCarPower_StaysInsideCard_At5x3() // 11 000 W, TextScale 1.3 : pas d'encre rose hors de la carte
[Fact] public void CarLabel_Texts()
// « Voiture · en charge · 86 % », « Voiture · branchée », « Voiture · débranchée · 12 % » (via une méthode internal static CarLabel(CarReading))
```

- [ ] **Step 2: Run tests to verify they fail** — FAIL.

- [ ] **Step 3: Implement**

- `_layout = EnergyLayouts.Get(config.Layout, battery, config.HasCar)`.
- `Quantities` gagne `Car` (puissance connue, sinon 0), adouci comme les autres ; `Settled` en tient compte.
- `DrawArt` : `ArtKind.CarBlue` remplacé par `CarArt.KindFor(_config.CarColor)`. Débranchée : image dessinée avec une peinture d'opacité 0,55 et un filtre de couleur qui désature (saturation 0,15) et assombrit (0,75).
- Badge (dans les coordonnées locales de la voiture, comme `DrawGauge`) : disque `rgba(20,24,32,.85)` de rayon 9 cerclé à 1,4 ; en attente, trait `#c9d1de` et dessin de prise (deux broches et corps) ; en charge, cercle et éclair `#ff7eb6`, opacité `0,55 + 0,45 · (0,5 + 0,5 · sin(clock · 3,9))`. Tracés : ceux de `maquette-voiture-etats.html`.
- Flux : `FilamentPainter.Draw(..., carLineOpacity)` avec `0` débranchée, `0.16f` en attente, `-1` en charge.
- Textes : `carLabel` → `CarLabel(reading)` ; `carValue` → `FormatPower(shown.Car)` ou « — » si la puissance est inconnue, dans la couleur de l'aspect (Global Constraints).
- `IsAnimating` : vrai aussi si `HomeToCar > 0`.

- [ ] **Step 4: Run all tests** — tous verts.

- [ ] **Step 5: Commit** — `git commit -m "feat: rendu de la voiture, de ses trois aspects et de ses textes"`

---

### Task 6: Galerie, réglage des places, README

**Files:**
- Modify: `HomeAssistant.Tests/GalleryTests.cs`, `HomeAssistant/Cards/Energy/EnergyLayouts.cs` (places « Bottom » et « Left avec batterie » si besoin), `README.md`
- Create: `docs/images/carte-voiture.png`

- [ ] **Step 1: Galerie**

Ajoute à `WriteAll` un jeu voiture, maison contemporaine, scénario « sun » : pour chaque disposition, avec et sans batterie, les trois aspects en 6 × 4 classique (`{layout}_{bat|nobat}_car-{unplugged|waiting|charging}_6x4_classique.png`), les trois couleurs en charge sur « left » sans batterie, et la disposition « left » sans batterie en charge en 5 × 3 et 8 × 4 dans les trois thèmes. Voiture en charge : `7400 W`, `on`, `86 %` ; en attente : `0 W`, `on`, `86 %` ; débranchée : `0 W`, `off`, `86 %`. Mets à jour le total attendu.

- [ ] **Step 2: Régler les places**

Lance `dotnet test HomeAssistant.Tests --filter GalleryTests`, regarde chaque image « car ». Règle dans `EnergyLayouts` les places des variantes « Bottom » et « Left avec batterie » jusqu'à ce que rien ne se chevauche, que la voiture reste au sol près de la maison et que le flux ne passe sous aucun dessin. Relance les tests de la tâche 4.

- [ ] **Step 3: README**

Nouvelle sous-section « Voiture électrique » dans « Réglages » (après la batterie) : les cinq réglages, les trois aspects, la règle des 50 W, l'absence de capteur « branchée ». Une ligne dans « Ce qu'il affiche ». Image `docs/images/carte-voiture.png` copiée depuis `gallery/left_nobat_car-charging_6x4_classique.png`, posée sur le fond du thème.

- [ ] **Step 4: Vérifier**

Run: `dotnet build -c Release`, `dotnet test -c Release`, puis `ashboard-module check HomeAssistant/bin/Release/net10.0 --out check-out` et `ashboard-module pack HomeAssistant/bin/Release/net10.0 --out pack-out`.
Expected: tous les tests verts, « Compatible avec Ashboard Vision (SDK 1.0). », paquet `macorou.home-assistant-1.3.0.avmod`.

- [ ] **Step 5: Commit** — `git commit -m "docs: voiture électrique dans la galerie et le README"`
