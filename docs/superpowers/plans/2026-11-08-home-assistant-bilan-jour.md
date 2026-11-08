# Carte « Bilan du jour » : plan de construction

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter au module Home Assistant une petite carte « Bilan du jour » (bande 4 × 1 ou liste 2 × 3) qui affiche les totaux du jour, le coût et le gain de revente, et livrer la version 1.1.0.

**Architecture:** La carte suit le découpage existant : `DailyConfig` lit les réglages, `DailyReader` transforme les états Home Assistant en lignes prêtes à afficher, `DailyCard` relit toutes les 30 s, `DailyRenderer` dessine. La boucle de lecture de `EnergyCard` est extraite dans `PollingCard` pour être partagée, sans changer le comportement de la carte d'énergie.

**Tech Stack:** C#, .NET 10, `AshboardVision.Sdk` 1.0.0, SkiaSharp 3, xUnit, `AshboardVision.Sdk.Testing`.

**Spec:** `docs/superpowers/specs/2026-11-08-home-assistant-bilan-jour-design.md`. Maquette : `docs/design/maquette-bilan-jour.html`.

## Global Constraints

- Le module reste en lecture seule : uniquement des `GET` vers Home Assistant.
- La clé API n'apparaît jamais dans un message, un journal ou un test écrit en clair ailleurs que dans le coffre.
- Tous les messages sont en français, et tutoient l'utilisateur.
- Les nombres s'écrivent avec une virgule, quelle que soit la langue du PC (`CultureInfo.InvariantCulture`, puis `Replace('.', ',')`).
- Une seule classe publique dans le module (`HomeAssistantModule`), tout le reste est `internal`.
- Commande de test, depuis la racine du dépôt : `dotnet test HomeAssistant.Tests`.
- Chaque commit se termine par la ligne `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. `day_values` modifié à la main (pas un tableau, nombre dans le tableau, doublon, valeur inconnue) : le module ne plante pas et garde ce qui est valable. Test en tâche 1.
2. Un capteur répond `nan`, `Infinity` ou une chaîne vide : la valeur affiche « — ». Test en tâche 2.
3. Le même capteur est choisi pour deux valeurs : une seule requête, les deux valeurs affichées. Test en tâche 5.
4. Un nombre très long, comme `123456,7`, ne dépasse pas de la carte. Test en tâche 6.
5. Une case réglée en 1.0.x (`grid_import` sans `grid`, ou `display` absent) continue de marcher. Tests en tâches 3 et 5.

---

### Task 1 : Lire les réglages du Bilan du jour

**Files:**
- Create: `HomeAssistant/Cards/Daily/DailyConfig.cs`
- Modify: `HomeAssistant/Core/SettingsReader.cs`
- Test: `HomeAssistant.Tests/DailyConfigTests.cs`

**Interfaces:**
- Produces :
  ```csharp
  internal enum DailyValue { Consumption, Production, Import, Export, Cost, Resale }
  internal enum DailyShape { Strip, List }
  internal enum ResalePrice { None, Typed, Sensor }
  internal sealed record DailyConfig(DailyShape Shape, IReadOnlyList<DailyValue> Values, string Consumption, string Production,
      string Import, string Export, string Cost, ResalePrice PriceMode, double? TypedPrice, string PriceSensor)
  {
      internal static bool TryRead(JsonElement settings, out DailyConfig? config, out string? problem);
      internal static double? ParsePrice(string text);
      internal IReadOnlyCollection<string> EntityIds { get; }
  }
  // SettingsReader
  public static IReadOnlyList<string> List(JsonElement settings, string key, IReadOnlyList<string> allowed, IReadOnlyList<string> fallback);
  ```

`Values` ne garde que les valeurs affichables, dans l'ordre de `day_values` : une valeur à capteur a son capteur choisi ; `Resale` a besoin de `day_export` et d'un mode de prix autre que `None` (en mode `Sensor`, d'un `day_price_sensor` choisi). `EntityIds` : les capteurs des valeurs de `Values`, sans doublon ni vide (`Resale` ajoute l'export et, en mode `Sensor`, le capteur de prix). Les valeurs JSON de `day_values` sont `consumption`, `production`, `import`, `export`, `cost`, `resale`.

- [ ] **Step 1 : écrire les tests**, avec `Entity(id)` qui rend `{"id":"<id>","name":"x"}` :

  | Test | Réglages | Attendu |
  |---|---|---|
  | `TryRead_Defaults_ToStripAndListedOrder` | les cinq capteurs | `Shape == Strip`, `Values` = Consumption, Production, Import, Export, Cost |
  | `TryRead_FollowsTheChosenOrder` | `day_values: ["cost","import"]`, capteurs présents | `Values` = Cost, Import |
  | `TryRead_ReadsListShape` | `day_shape: "list"` | `Shape == List` |
  | `TryRead_ValueWithoutSensor_IsDropped` | `day_cost` absent, `day_consumption` présent | `Cost` absent de `Values` |
  | `TryRead_NoDisplayableValue_IsNotConfigured` | `{}` | `false`, `problem == "Choisis au moins un capteur pour le Bilan du jour."` |
  | `TryRead_ResaleNeedsPriceMode` | export présent, `day_price_mode` absent | `Resale` absent |
  | `TryRead_TypedPrice_ShowsResale` | export, `day_price_mode: "typed"`, `day_price: "0,13"` | `Resale` présent, `TypedPrice == 0.13` |
  | `TryRead_TypedPriceEmpty_StillShowsResale` | export, mode `typed`, pas de prix | `Resale` présent, `TypedPrice == null` |
  | `TryRead_SensorPriceWithoutSensor_DropsResale` | export, mode `sensor` | `Resale` absent |
  | `EntityIds_AreDistinct_AndOnlyForShownValues` | conso et export sur `sensor.a`, `day_values: ["consumption","export"]`, coût choisi | `["sensor.a"]` |
  | `ParsePrice` (Theory) | `0,13`→0.13, `0.13`→0.13, `0`→0, `abc`→null, `-1`→null, `1,2,3`→null, `1.234,5`→null, `""`→null | |
  | `List_IgnoresBadEntries` | `day_values: ["cost",3,"cost","nope","import"]` | Cost, Import |
  | `List_NotAnArray_UsesFallback` | `day_values: "cost"` | l'ordre par défaut des six |
  | `List_EmptyArray_MeansNothing` | `day_values: []`, capteurs présents | `TryRead` rend `false` avec le message ci-dessus |

- [ ] **Step 2 : lancer** `dotnet test HomeAssistant.Tests --filter "FullyQualifiedName~DailyConfigTests"`. Attendu : échec de compilation, `DailyConfig` n'existe pas.

- [ ] **Step 3 : écrire** `SettingsReader.List` et `DailyConfig`. `ParsePrice` applique `^\d+([.,]\d+)?$`.

- [ ] **Step 4 : relancer** la même commande. Attendu : tout passe.

- [ ] **Step 5 : commit** `feat: lecture des réglages du Bilan du jour`.

### Task 2 : Transformer les états en valeurs affichables

**Files:**
- Create: `HomeAssistant/Cards/Daily/DailyReader.cs`
- Modify: `HomeAssistant/Cards/Energy/EnergyReader.cs` (rendre le test de nombre accessible)
- Test: `HomeAssistant.Tests/DailyReaderTests.cs`

**Interfaces:**
- Consumes : `DailyConfig`, `DailyValue`, `HaState`.
- Produces :
  ```csharp
  internal sealed record DailyItem(DailyValue Value, string Number, string Unit);
  internal static class DailyReader
  {
      internal const string Missing = "—";
      internal static IReadOnlyList<DailyItem> Read(DailyConfig config, IReadOnlyDictionary<string, HaState> states);
      internal static string LabelOf(DailyValue value);   // Conso, Prod., Import, Export, Coût, Revente
      internal static bool IsUnknownEnergyUnit(string? unit);
  }
  // EnergyReader
  internal static double? Number(HaState? state);   // enveloppe l'actuel TryNumber
  ```

Une valeur manquante a `Number == "—"` et `Unit == ""`. Une énergie a `Unit == "kWh"`. `IsUnknownEnergyUnit` est vrai pour une unité non nulle autre que Wh, kWh, MWh, sans tenir compte de la casse.

- [ ] **Step 1 : écrire les tests** (construire un `DailyConfig` directement) :

  | Test | Entrée | Attendu |
  |---|---|---|
  | `Energy_IsOneDecimal_WithComma` | `24.82` kWh | `("24,8", "kWh")` |
  | `Energy_ConvertsWhAndMWh` (Theory) | `7800` Wh ; `0.0078` MWh ; `7.8` KWH | `("7,8", "kWh")` |
  | `Energy_NoUnit_IsKwh` | `7.8`, unité null | `("7,8", "kWh")` |
  | `Cost_IsTwoDecimals` | `2.914` `€` ; `2.914` `EUR` | `("2,91", "€")` |
  | `Cost_OtherCurrency_IsKept` | `2.9` `USD` | `("2,90", "USD")` |
  | `Cost_NoUnit_IsEuro` | `2.9`, unité null | `("2,90", "€")` |
  | `Unusable_IsMissing` (Theory) | `unavailable`, `unknown`, `""`, `abc`, `nan`, `Infinity` | `("—", "")` |
  | `Resale_TypedPrice` | export `1.6` kWh, prix tapé 0.13 | `("0,21", "€")` |
  | `Resale_ConvertsExportUnit` | export `1600` Wh, prix tapé 0.13 | `("0,21", "€")` |
  | `Resale_ZeroPrice_IsZero` | prix tapé 0 | `("0,00", "€")` |
  | `Resale_TypedPriceInvalid_IsMissing` | `TypedPrice == null` | `("—", "")` |
  | `Resale_SensorPrice` (Theory) | `0.13` `€/kWh` ; `0.13` `EUR/kWh` ; `13` `c€/kWh` ; `0.13` sans unité | `("0,21", "€")` |
  | `Resale_SensorOtherCurrency` | `0.13` `USD/kWh` | `("0,21", "USD")` |
  | `Resale_ExportUnavailable_IsMissing` | export `unavailable` | `("—", "")` |
  | `Resale_PriceUnavailable_IsMissing` | prix `unknown` | `("—", "")` |
  | `Read_KeepsTheOrderOfValues` | `Values` = Cost, Consumption | items dans cet ordre |
  | `LabelOf` (Theory) | les six | les six libellés |
  | `IsUnknownEnergyUnit` (Theory) | `Wh`,`kwh`,`MWh`,null → false ; `W`,`€` → true | |

- [ ] **Step 2 : lancer** `dotnet test HomeAssistant.Tests --filter "FullyQualifiedName~DailyReaderTests"`. Attendu : échec de compilation.

- [ ] **Step 3 : écrire** `DailyReader` et exposer `EnergyReader.Number`. Un prix `c€/...` est divisé par 100 et sa devise est `€`. La devise d'un prix est le texte avant `/kWh`.

- [ ] **Step 4 : relancer**. Attendu : tout passe.

- [ ] **Step 5 : commit** `feat: valeurs du Bilan du jour`.

### Task 3 : Manifeste, recherche de capteurs et réglages du réseau

**Files:**
- Modify: `HomeAssistant/manifest.json`, `HomeAssistant/Core/EntityCatalog.cs`, `HomeAssistant/Cards/Energy/EnergyConfig.cs`
- Test: `HomeAssistant.Tests/ModuleStartupTests.cs`, `HomeAssistant.Tests/EntityCatalogTests.cs`, `HomeAssistant.Tests/EnergyConfigTests.cs`

**Interfaces:**
- Produces : `EntityKind` gagne `Energy`, `Money`, `Price`. `KindFor` : `day_consumption`, `day_production`, `day_import`, `day_export` → `Energy` ; `day_cost` → `Money` ; `day_price_sensor` → `Price`.

Manifeste : appliquer les § 4 et 5 de la spec. Les champs `day_*` se placent juste après `display`. Les `lookup` reprennent `labelPath`, `placeholder` et `emptyLabel` du champ `solar`. `day_price` a `maxLength` 12 et `placeholder` `0,13`. `minSize` passe à 2 × 1, `presets` inchangés. `grid_import` est retiré.

- [ ] **Step 1 : écrire ou modifier les tests**
  - `Manifest_DeclaresPresetsAndPermissions` : `minSize` attendu `(2, 1)`.
  - `Manifest_DailyFields_OnlyShowForDaily` : tout champ dont la clé commence par `day_`, sauf `day_price` et `day_price_sensor`, a `visibleWhen` `{ key: display, value: daily }` ; `day_price` dépend de `day_price_mode` = `typed`, `day_price_sensor` de `day_price_mode` = `sensor`.
  - `Manifest_DisplayOffersDaily` : `display` a les options `energy` et `daily`, défaut `energy`.
  - `Manifest_DayValues_IsAnOrderedListOfSix` : genre `list`, `default` égal à `consumption, production, import, export, cost, resale`.
  - `Manifest_GridFields_FollowDisplay` : `grid` et `grid_inverted` ont `visibleWhen` `display` = `energy` ; `grid_export` garde `grid_mode` = `two` ; `grid_import` n'existe plus.
  - `KindFor_MapsFields` : ajouter les six nouveaux champs.
  - `Search_Energy_KeepsOnlyEnergyUnits` : `Wh`, `kWh`, `MWh` gardés, `W` et `€` écartés.
  - `Search_Money_KeepsMonetary` : `device_class` `monetary` ou unité `€`/`EUR` gardés, `EUR/kWh` écarté.
  - `Search_Price_KeepsPerKwh` : `€/kWh`, `EUR/kWh`, `c€/kWh` gardés, `€` écarté.
  - `Search_Energy_RanksDailyFirst` : à requête vide, `Production du jour`, `sensor.pv_today` et `sensor.daily_x` passent avant `Compteur total`, même si ce dernier vient avant dans l'ordre alphabétique.
  - `EnergyConfigTests` : le test actuel qui attend `sensor.ignored` pour `grid` en mode `two` devient `TryRead_TwoSensors_GridIsThePurchaseSensor` (`GridImport == "sensor.ignored"`, `Grid == ""`). Ajouter `TryRead_TwoSensors_FallsBackOnLegacyGridImport` (seul `grid_import` est rempli : `GridImport == "sensor.imp"`).

- [ ] **Step 2 : lancer** `dotnet test HomeAssistant.Tests`. Attendu : ces tests échouent, les autres passent.

- [ ] **Step 3 : modifier** le manifeste, `EntityCatalog` et `EnergyConfig`. Le classement met en premier, pour `Energy`, `Money` et `Price`, les capteurs dont le nom ou l'identifiant normalisé contient `jour`, `journ`, `today` ou `daily`. Les comparaisons d'unité ignorent la casse.

- [ ] **Step 4 : relancer** `dotnet test HomeAssistant.Tests`. Attendu : tout passe.

- [ ] **Step 5 : commit** `feat: réglages et recherche du Bilan du jour`.

### Task 4 : Extraire la boucle de lecture

**Files:**
- Create: `HomeAssistant/Cards/PollingCard.cs`
- Modify: `HomeAssistant/Cards/Energy/EnergyCard.cs`

**Interfaces:**
- Produces :
  ```csharp
  internal abstract class PollingCard(CardContext context, TimeSpan every) : ICard
  {
      internal const int FailuresBeforeOffline = 3;
      internal static readonly TimeSpan RetryAfterUnauthorized = TimeSpan.FromSeconds(60);
      protected CardContext Context { get; }
      protected bool IsClosed { get; }
      protected abstract IReadOnlyCollection<string> EntityIds { get; }
      protected abstract void OnRead(IReadOnlyDictionary<string, HaState> states);   // appelé après la remise à zéro des échecs
      protected abstract void MarkStale();
      protected void Report(ModuleHealth health, string? message);
      protected virtual void DisposeRendering() { }
      public abstract bool IsAnimating { get; }
      public abstract void Render(SKCanvas canvas, ModuleRenderContext ctx);
      public void Start();
      public ValueTask DisposeAsync();
  }
  ```

`EnergyCard` hérite de `PollingCard` et garde `RefreshEvery`, `Current`, `IsUnknownUnit`, ses avertissements d'unité et son cas « réseau indisponible ». Aucun test n'est modifié dans cette tâche : ils sont le garde-fou.

- [ ] **Step 1 : déplacer** la boucle, `Fail`, `Report` et la fermeture dans `PollingCard`.
- [ ] **Step 2 : lancer** `dotnet test HomeAssistant.Tests`. Attendu : tout passe.
- [ ] **Step 3 : vérifier** `git diff --stat -- HomeAssistant.Tests`. Attendu : aucune sortie.
- [ ] **Step 4 : commit** `refactor: boucle de lecture commune aux cartes`.

### Task 5 : La carte et son choix dans le module

**Files:**
- Create: `HomeAssistant/Cards/Daily/DailyCard.cs`, `HomeAssistant/Cards/Daily/DailyRenderer.cs`
- Modify: `HomeAssistant/HomeAssistantModule.cs`
- Test: `HomeAssistant.Tests/DailyCardTests.cs`, `HomeAssistant.Tests/ModuleStartupTests.cs`

**Interfaces:**
- Consumes : `PollingCard`, `DailyConfig`, `DailyReader`.
- Produces :
  ```csharp
  internal sealed record DailyState(IReadOnlyList<DailyItem> Items, bool Stale);
  internal sealed class DailyCard(CardContext context, DailyConfig config) : PollingCard(context, RefreshEvery)
  {
      internal static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(30);
      internal volatile DailyState? Current;
  }
  internal sealed class DailyRenderer(DailyConfig config) : IDisposable
  {
      internal void Render(SKCanvas canvas, ModuleRenderContext ctx, DailyState? state);   // vide dans cette tâche, remplie en tâche 6
  }
  ```

`IsAnimating` vaut `false`. `OnRead` journalise une seule fois par capteur une unité d'énergie inconnue, publie `Current` et annonce `Online`. Dans `StartAsync`, `display` se lit avec `SettingsReader.Choice(Services.Settings, "display", "energy", "energy", "daily")`, et le client est libéré si la configuration échoue, comme pour l'énergie.

- [ ] **Step 1 : écrire les tests**, sur le modèle de `EnergyCardTests` :
  - `Poll_PublishesItems_InTheChosenOrder`.
  - `Poll_EveryThirtySeconds` : aucune nouvelle lecture après `Advance(29 s)`, une de plus après 1 s de plus.
  - `UnavailableSensor_ShowsMissing_AndStaysOnline`.
  - `SameSensorForTwoValues_IsReadOnce` : conso et import sur `sensor.a`, une requête par lecture, deux lignes affichées.
  - `Unauthorized_GoesOfflineImmediately`, `SensorNotFound_GoesOffline_AndKeepsItemsStale`.
  - `ThreeFailures_GoOffline_ThenRecover` : en ligne de nouveau dès que le serveur répond, `Stale == false`.
  - `IsNeverAnimating`.
  - `ModuleStartupTests.Start_DailyDisplay_CreatesDailyCard` (`Card` est un `DailyCard`).
  - `ModuleStartupTests.Start_DailyWithoutSensor_IsNotConfigured` : `NotConfigured`, message de la tâche 1, aucune requête.
  - `ModuleStartupTests.Start_NoDisplaySetting_StaysEnergy` (`Card` est un `EnergyCard`).

- [ ] **Step 2 : lancer** `dotnet test HomeAssistant.Tests --filter "FullyQualifiedName~DailyCardTests|FullyQualifiedName~ModuleStartupTests"`. Attendu : échec de compilation.

- [ ] **Step 3 : écrire** `DailyCard`, le `DailyRenderer` vide et le choix dans `StartAsync`.

- [ ] **Step 4 : lancer** `dotnet test HomeAssistant.Tests`. Attendu : tout passe.

- [ ] **Step 5 : commit** `feat: carte Bilan du jour branchée au module`.

### Task 6 : Le dessin et les cases trop petites

**Files:**
- Create: `HomeAssistant/Cards/Daily/DailyLayout.cs`, `HomeAssistant/Cards/Daily/DailyIcons.cs`
- Modify: `HomeAssistant/Cards/Daily/DailyRenderer.cs`, `HomeAssistant/Cards/Energy/EnergyRenderer.cs`, `HomeAssistant/Cards/Energy/EnergyCard.cs`
- Test: `HomeAssistant.Tests/DailyRendererTests.cs`, `HomeAssistant.Tests/GalleryTests.cs`

**Interfaces:**
- Produces :
  ```csharp
  internal static class DailyLayout
  {
      internal const float StripWidth = 464, StripHeight = 100, ListWidth = 226, ListHeight = 322;
      internal static string? TooSmallMessage(DailyShape shape, GridSize size);
  }
  // EnergyRenderer
  internal static string? TooSmallMessage(GridSize size);
  ```

Tout est dessiné dans l'espace de la forme, mis à l'échelle uniformément et centré dans `ctx.Bounds`, comme `EnergyRenderer`. Les mesures viennent de la maquette :

| | Bande (464 × 100) | Liste (226 × 322) |
|---|---|---|
| Marge intérieure | 4 à gauche et à droite | 13 à gauche et à droite, 6 en haut et en bas |
| Séparateurs | traits verticaux, de y = 14 à y = 86 | traits horizontaux sous chaque ligne sauf la dernière |
| Icône | 16, centrée, y de 18 à 34 | 18, centrée sur la ligne, à gauche |
| Nombre | taille 23, ou 21 à six valeurs ; centré ; ligne de base 62 | taille 24, à droite, unité à 0,58 fois la taille et 68 % d'opacité |
| Libellé | taille 12, centré, ligne de base 80 | taille 14, 8 après l'icône |

Dans la bande, un kWh n'affiche pas son unité, et un montant s'écrit `2,91 €`. Le nombre et le libellé passent par `EnergyRenderer.FitSize` avec la largeur disponible et sont multipliés par `ctx.TextScale`. Le nombre utilise `ctx.Theme.Text`, le libellé `ctx.Theme.TextMuted`, le séparateur `ctx.Theme.TextMuted` à l'alpha 46. Hors ligne (`Stale`), tout est à 50 % d'opacité. Les icônes reprennent les tracés `w-home`, `w-sun`, `w-imp`, `w-exp`, `w-cost`, `w-gain` de la maquette, avec `SKPath.ParseSvgPathData` (les cercles par `AddCircle`), en trait de 1,9 dans un carré de 24, aux couleurs du § 6 de la spec.

La vérification du message passe avant celle de `state` : une case trop petite affiche son message même sans données, avec `ModuleDrawing.DrawActionMessage`. `EnergyCard.IsAnimating` vaut `false` si son dernier dessin a montré ce message.

- [ ] **Step 1 : écrire les tests**
  - `TooSmallMessage` (Theory) : `(Strip, 3×1)` → `Agrandis la case : 4 × 1 minimum` ; `(Strip, 4×1)` et `(Strip, 6×2)` → null ; `(List, 2×2)` et `(List, 1×3)` → `Agrandis la case : 2 × 3 minimum` ; `(List, 2×3)` → null.
  - `Energy_TooSmallMessage` (Theory) : `4×3` et `5×2` → `Agrandis la case : 5 × 3 minimum` ; `5×3` → null.
  - `Render_Strip_And_List_DrawSomething`, à leurs tailles minimales, dans les trois thèmes.
  - `Render_TooSmall_DrawsTheMessage_EvenWithoutData` : capteurs jamais lus (`Delay` infini sur le faux serveur), case `3×1`, l'image n'est pas vide.
  - `Render_HugeNumbers_StayInsideTheCard` : six valeurs à `123456.7`, case 4 × 1, aucun pixel opaque (`Alpha > 0`) sur les 2 colonnes de pixels de gauche et de droite.
  - `Energy_TooSmall_StopsAnimating` : après un dessin en `4×2`, `IsAnimating == false`.
  - `GalleryTests.WriteAll` écrit aussi `daily_<forme>_<colonnes>x<lignes>_<thème>.png`, avec six valeurs, pour la bande en 4×1, 6×1, 4×2 et la liste en 2×3, 3×4, dans les trois thèmes (15 images, à compter dans l'assertion).

- [ ] **Step 2 : lancer** `dotnet test HomeAssistant.Tests --filter "FullyQualifiedName~DailyRendererTests"`. Attendu : échec.

- [ ] **Step 3 : écrire** le dessin et les messages.

- [ ] **Step 4 : lancer** `dotnet test HomeAssistant.Tests`. Attendu : tout passe. Regarder `gallery/daily_strip_4x1_classique.png` et `gallery/daily_list_2x3_classique.png` et les comparer à la maquette.

- [ ] **Step 5 : commit** `feat: dessin du Bilan du jour et cases trop petites`.

### Task 7 : Documentation, version et paquet

**Files:**
- Modify: `README.md`, `HomeAssistant/manifest.json`
- Create: `docs/images/carte-bilan-bande.png`, `docs/images/carte-bilan-liste.png` (copies de la galerie)

- [ ] **Step 1 : mettre à jour** `version` à `1.1.0`, et le README : la section « Ce qu'il affiche » présente les deux cartes avec les deux images ; « Ce qu'il ne fait pas » ne dit plus qu'il n'y a qu'une carte ; une section « Bilan du jour » décrit la forme, les tailles minimales, l'ordre des valeurs, les capteurs et le prix de revente ; en mode deux capteurs, « Capteur réseau » est celui de l'achat.

- [ ] **Step 2 : vérifier**, depuis la racine :
  ```
  dotnet test HomeAssistant.Tests -c Release
  dotnet build -c Release
  ashboard-module check HomeAssistant/bin/Release/net10.0 --out check-out
  ashboard-module pack HomeAssistant/bin/Release/net10.0 --out pack-out
  ```
  Attendu : tests réussis, `Compatible avec Ashboard Vision (SDK 1.0).`, et `pack-out/macorou.home-assistant-1.1.0.avmod` avec son `.sha256`. Le paquet ne contient pas de `.pdb`.

- [ ] **Step 3 : commit** `chore: version 1.1.0 avec le Bilan du jour`. Ne pas pousser ni publier : la publication demande l'accord explicite de l'utilisateur.
