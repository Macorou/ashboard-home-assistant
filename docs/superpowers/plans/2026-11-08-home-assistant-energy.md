# Module « Home Assistant », plan de construction

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** construire le module communautaire Ashboard Vision « Home Assistant » : le socle de connexion et la carte animée des flux d'énergie, avec batterie optionnelle.

**Architecture :** une seule classe publique, `HomeAssistantModule`, lit la configuration et délègue à une carte (`ICard`). Le socle (`Core/`) ne connaît rien de l'énergie. La carte sépare la logique pure et testable (configuration, lecture, calcul des flux) du rendu SkiaSharp, qui repose sur des images préparées une fois et n'anime que les filaments et les lumières.

**Tech Stack :** .NET 10, C#, `AshboardVision.Sdk` 1.0.0, SkiaSharp (fourni par l'hôte), xunit, `AshboardVision.Sdk.Testing`.

**Spec :** [`docs/superpowers/specs/2026-11-08-home-assistant-energy-design.md`](../specs/2026-11-08-home-assistant-energy-design.md). À lire avant chaque tâche. Maquette : [`docs/design/maquette-energie.html`](../../design/maquette-energie.html). SDK : `docs/sdk` du dépôt `Ashewos/ashboard-vision`, branche `Canary`.

## Global Constraints

- Identifiant `macorou.home-assistant`, DLL `HomeAssistant.dll`, classe `HomeAssistantModule`, espace de noms `HomeAssistant`.
- Exactement une classe publique dérivée de `CommunityModule`, non scellée, constructeur sans paramètre. Tout le reste est `internal`.
- `InternalsVisibleTo Include="HomeAssistant.Tests"` dans le projet du module.
- Paquets en version exacte : `AshboardVision.Sdk` 1.0.0, `AshboardVision.Sdk.Testing` 1.0.0.
- Messages de statut : français, tutoiement, disent quoi faire, 200 caractères au plus.
- Champ requis vide : `NotConfigured`, sans requête. Jamais de secret dans le journal ou un message.
- Aucune exception ne sort d'une minuterie ou d'une tâche, surtout après `DisposeAsync`. Fermeture en moins de 5 s.
- Le module ne fait que lire Home Assistant : uniquement `GET`.
- Aucune adresse, entité ou clé de l'auteur dans le code ou le manifeste. Les noms d'entités de l'auteur n'apparaissent que dans les tests.
- Commits locaux à la fin de chaque tâche, terminés par la ligne `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. **Jamais de `git push`, de dépôt distant ou de version GitHub** sans demande explicite de l'utilisateur.
- Modèle du sous-agent indiqué par tâche. Relecture finale par Opus 5.5.

## Review Focus

1. Un capteur renvoie un état non numérique, vide ou `12,5` : la carte traite la valeur comme indisponible, sans exception. Test en tâche 4.
2. Valeurs incohérentes (production à -3 W la nuit, consommation mesurée inférieure à la production, achat et vente non nuls) : aucun flux négatif. Test en tâche 3.
3. Adresse ou jeton collés avec des espaces ou un retour à la ligne : ils sont nettoyés, l'en-tête HTTP ne lève pas d'exception. Test en tâche 5.
4. Home Assistant lent ou case fermée pendant une lecture : jamais deux lectures simultanées, aucune exception à la fermeture. Test en tâche 7.
5. Recherche de capteur sans accent (« energie » pour « Énergie ») ou en majuscules : le capteur est trouvé. Test en tâche 6.

---

## Structure des fichiers

```
HomeAssistant.slnx, README.md, LICENSE, .gitignore
HomeAssistant/
  HomeAssistant.csproj, manifest.json, icon.png
  HomeAssistantModule.cs            cycle de vie, recherche, boutons, choix de la carte
  Core/SettingsReader.cs            lecture prudente des réglages
  Core/HaAddress.cs                 normalisation et nature de l'adresse
  Core/HaClient.cs                  appels REST, HaState, HaProblem
  Core/EntityCatalog.cs             filtre et classement des capteurs
  Cards/ICard.cs                    contrat des cartes, CardContext
  Cards/Energy/EnergyFlows.cs       calcul pur
  Cards/Energy/EnergyConfig.cs      réglages de la carte
  Cards/Energy/EnergyReader.cs      états HA vers mesures
  Cards/Energy/EnergyCard.cs        lecture périodique, statuts
  Cards/Energy/Art/IsoPainter.cs    primitives isométriques
  Cards/Energy/Art/*Art.cs          maison x2, panneaux, pylône, batterie
  Cards/Energy/Art/ArtCache.cs      images préparées
  Cards/Energy/EnergyLayouts.cs     coordonnées des dispositions
  Cards/Energy/FilamentPainter.cs   filaments
  Cards/Energy/EnergyRenderer.cs    composition d'une image
HomeAssistant.Tests/
  Support/FakeHomeAssistant.cs      faux serveur (HttpMessageHandler)
  Support/TestableModule.cs         sous-classe avec le faux client
  *Tests.cs
```

---

### Task 1 : projet, manifeste et non-configuration — Sonnet

**Files :** Create : tout le squelette ci-dessus jusqu'à `HomeAssistantModule.cs`, `Core/SettingsReader.cs`, `Support/*`. Test : `HomeAssistant.Tests/ModuleStartupTests.cs`.

**Interfaces :**
- Produit : `public class HomeAssistantModule : CommunityModule` avec `internal virtual HttpClient CreateHttpClient(string? certificateKey) => Services.CreateHttpClient(certificateKey);`.
- Produit : `internal static class SettingsReader { string Text(JsonElement, string key); bool Bool(JsonElement, string key, bool fallback); string Choice(JsonElement, string key, string fallback, params string[] allowed); string EntityId(JsonElement, string key); }`. `EntityId` lit `{"id":"sensor.x","name":"…"}` et rend `""` si absent ou mal formé.
- Produit (tests) : `FakeHomeAssistant : HttpMessageHandler`, avec `Dictionary<string,(int Status,string Body)> Responses`, `List<HttpRequestMessage> Requests`, `Func<CancellationToken,Task>? Delay` ; `TestableModule : HomeAssistantModule`, avec `static AsyncLocal<HttpMessageHandler?> Handler` et `static AsyncLocal<Holder?> Probe`. Son constructeur écrit `Probe.Value!.Module = this` si la sonde est posée (`Holder { public TestableModule? Module; }`), pour appeler `LookupAsync` et `RunActionAsync` depuis les tests.

- [ ] **Step 1 : initialiser.** À la racine du projet : `git init -b main`, puis `dotnet new ashboard-module -n HomeAssistant --id macorou.home-assistant --param:author "Macorou" -o .`. Ajouter à `.gitignore` : `.superpowers/` et `gallery/`. Renommer la classe et le fichier en `HomeAssistantModule`, `public class`. Remplacer le test du modèle. Ajouter `InternalsVisibleTo`.

- [ ] **Step 2 : écrire le manifeste exact.** Champs : `name` « Home Assistant », `description` « Affiche ton installation solaire depuis Home Assistant, avec des flux de courant animés. Module non affilié au projet Home Assistant. », `author` « Macorou », `version` `1.0.0`, `sdk` `1.0`, `entry` `HomeAssistant.dll`, `permissions` `["reseau-local","internet"]`, `repository` `Macorou/ashboard-home-assistant`, `icon` `icon.png` (Task 10), `minSize` 5×3, `presets` compact 5×3, ideal 6×4, large 8×4. `settings` : la table du § 5 de la spec, dans cet ordre, avec `forget_cert` (action, `confirm`) juste après `test`.
  Les champs d'énergie portent `visibleWhen`. Comme une seule condition est permise, seuls `layout` et `house` portent `display=energy` ; les champs conditionnels gardent leur condition `grid_mode` ou `battery_mode`. Les `lookup` utilisent `labelPath: "name"`. Le champ d'aide de `ha_token` dit où créer la clé (profil Home Assistant, onglet Sécurité).

- [ ] **Step 3 : écrire les tests qui échouent.** `ModuleStartupTests` : `Manifest_DeclaresPresetsAndPermissions` (lit le JSON : `minSize` 5×3, `ideal` 6×4, `large` 8×4, permissions exactes, `id`) ; `Start_WithoutSecrets_IsNotConfiguredAndMakesNoRequest` (`Health == NotConfigured`, `Requests` vide, le message contient « adresse ») ; `Start_WithSecretsButNoSensor_IsNotConfigured` (le message contient « production ») ; `Start_LocalhostAddress_ExplainsItIsNotSupported` ; `Start_NoPermissionGranted_IsNotConfigured` (`granted: []`, le message contient « permission »).

- [ ] **Step 4 : vérifier l'échec.** `dotnet test` : ces tests échouent.

- [ ] **Step 5 : implémenter `StartAsync`** : lit `ha_url` et `ha_token` dans `Services.Secrets`, valide via `HaAddress` (Task 2 : pour cette tâche, un test de chaîne vide et de `localhost` suffit, remplacé en Task 2), vérifie qu'au moins une permission est accordée, vérifie le capteur `solar`. Ne lance aucune requête.

- [ ] **Step 6 : vérifier et valider.** `dotnet test` passe. Commit `feat: squelette du module et manifeste`.

### Task 2 : adresse Home Assistant — Haiku

**Files :** Create `Core/HaAddress.cs`. Test `HaAddressTests.cs`.

**Interfaces :** Produit `internal sealed record HaAddress(Uri Root, bool IsLocalhost, bool IsPrivate) { static bool TryParse(string? raw, out HaAddress? address); string? CertificateKey { get; } }`. `CertificateKey` vaut `"ha"` si `https` et privée, sinon `null`.

- [ ] **Step 1 : tests.** `[Theory]` `TryParse_Normalizes` : `192.168.1.10:8123` → `http://192.168.1.10:8123/` ; `http://ha.local:8123/` → même forme avec barre unique ; `  https://maison.duckdns.org \n` → `https://maison.duckdns.org/`. `TryParse_Rejects` : vide, `ftp://x`, `http://`. `IsPrivate` : vrai pour `10.x`, `172.16–31.x`, `192.168.x`, `.local`, `.lan`, `homeassistant` ; faux pour `maison.duckdns.org`, `8.8.8.8`, `172.32.0.1`. `IsLocalhost` : `localhost`, `127.0.0.1`, `[::1]`. `CertificateKey` : `https://192.168.1.10` → `"ha"` ; `http://192.168.1.10` → `null` ; `https://maison.duckdns.org` → `null`. Avec l'hôte, dans `ModuleStartupTests` : `Start_PrivateIpWithoutLocalNetwork_SaysToTickIt` (`granted: [Internet]`, le message contient « Réseau local ») ; `Start_PublicIpWithoutInternet_SaysToTickIt` (`granted: [LocalNetwork]`, le message contient « Internet ») ; `Start_HostnameWithOnePermission_TriesToConnect`.
- [ ] **Step 2 : vérifier l'échec.**
- [ ] **Step 3 : implémenter**, et brancher `StartAsync` dessus.
- [ ] **Step 4 : `dotnet test` passe. Commit** `feat: lecture de l'adresse Home Assistant`.

### Task 3 : calcul des flux — Haiku

**Files :** Create `Cards/Energy/EnergyFlows.cs`. Test `EnergyFlowsTests.cs`.

**Interfaces :** Produit
```csharp
internal readonly record struct EnergyInput(double Solar, double Import, double Export, double Charge, double Discharge, double? HomeSensor);
internal sealed record EnergyFlows(double SolarToHome, double BatteryToHome, double GridToHome, double SolarToBattery,
    double GridToBattery, double SolarToGrid, double BatteryToGrid, double Home, double SolarSharePercent)
{ internal const double NoiseFloorWatts = 10; internal static EnergyFlows Compute(EnergyInput input); }
```
Formules : § 7 de la spec. Toute entrée négative est ramenée à 0 d'abord. Un flux sous `NoiseFloorWatts` vaut 0. `Home` et `SolarSharePercent` sont calculés avant ce seuil.

- [ ] **Step 1 : tests** (précision 0,01).
  - `AuthorsCase_NoBattery` : S 1124, exp 352,38 → Home 771,62, SolarToHome 771,62, SolarToGrid 352,38.
  - `Sun_ChargesBatteryThenExports` : S 3420, exp 660, chg 900, HomeSensor 1860 → s2h 1860, s2b 900, s2e 660.
  - `Cloudy_BatteryAndGridFeedHome` : S 980, imp 440, dis 820 → Home 2240, s2h 980, b2h 820, g2h 440.
  - `OffPeak_GridChargesBattery` : imp 2520, chg 2000 → Home 520, g2h 520, g2b 2000.
  - `BatterySellsToGrid` : exp 700, dis 1000 → Home 300, b2h 300, b2g 700.
  - `SmallFlows_AreZero` : S 5, imp 200 → s2h 0, g2h ≥ 195.
  - `SolarShare_IsHundredWhenSelfSufficient`.
  - `Incoherent_NoNegativeFlow` (Review Focus 2) : `[Theory]` S -3 ; HomeSensor 100 avec S 2000 et exp 0 ; imp 300 et exp 300 ensemble : chaque flux ≥ 0 et aucun ne dépasse sa source.
- [ ] **Step 2 : vérifier l'échec.**
- [ ] **Step 3 : implémenter `Compute`.**
- [ ] **Step 4 : `dotnet test` passe. Commit** `feat: calcul des flux d'énergie`.

### Task 4 : configuration et lecture des capteurs — Sonnet

**Files :** Create `Cards/Energy/EnergyConfig.cs`, `Cards/Energy/EnergyReader.cs`, et `HaState` dans `Core/HaClient.cs`. Test `EnergyConfigTests.cs`, `EnergyReaderTests.cs`.

**Interfaces :**
- Produit `internal sealed record HaState(string EntityId, string State, string? Unit, string? DeviceClass, string? FriendlyName);`.
- Produit `internal enum LayoutKind { Top, Left, Bottom }`, `internal enum HouseStyle { Modern, Classic }`, `internal enum BatteryMode { None, One, Two }`.
- Produit `internal sealed record EnergyConfig(LayoutKind Layout, HouseStyle House, string Solar, bool GridTwo, string Grid, bool GridInverted, string GridImport, string GridExport, string Home, BatteryMode Battery, string BatteryPower, bool BatteryInverted, string BatteryCharge, string BatteryDischarge, string BatterySoc) { static bool TryRead(JsonElement settings, out EnergyConfig? config, out string? problem); IReadOnlyCollection<string> EntityIds { get; } }`. Avec `battery_mode` à `two`, `BatterySoc` vient de `battery_soc2`.
- Produit `internal sealed record EnergySnapshot(EnergyInput Input, double? BatterySocPercent, bool BatteryPowerKnown, string? UnavailableGridEntity)` et `internal static class EnergyReader { static double? Watts(HaState? state); static EnergySnapshot Combine(EnergyConfig config, IReadOnlyDictionary<string,HaState> states); static double? Percent(HaState? state); }`.

Règles : § 6 de la spec. `Watts` : culture invariante, `kW` × 1000, `W` tel quel, autre unité considérée W ; `unavailable`, `unknown`, vide, non numérique → `null`.

- [ ] **Step 1 : tests.**
  - `TryRead_MissingGridSensor_NamesTheField`, `TryRead_BatteryOneWithoutSensor_NamesTheField`, `TryRead_BatteryNone_IgnoresBatteryFields`, `TryRead_Defaults_TopModernOneNone`, `EntityIds_NoDuplicates_OnlyActiveMode`.
  - `Watts_ConvertsKilowatts` (`1.5 kW` → 1500), `Watts_NonNumeric_IsNull` (Review Focus 1 : `unavailable`, `unknown`, ``, `12,5`, `abc`).
  - `Combine_GridSigned_PositiveIsImport`, `Combine_GridInverted`, `Combine_GridTwoSensors`, `Combine_BatterySigned_PositiveIsDischarge`, `Combine_BatteryInverted`, `Combine_BatteryTwo`.
  - `Combine_SolarUnavailable_IsZeroWithoutProblem`, `Combine_GridUnavailable_NamesEntity`, `Combine_HomeSensorUnavailable_FallsBackToComputed` (`HomeSensor` null), `Combine_BatteryUnavailable_ZeroAndNotKnown`, `Combine_NoBattery_SocNull`.
- [ ] **Step 2 : vérifier l'échec.**
- [ ] **Step 3 : implémenter.**
- [ ] **Step 4 : `dotnet test` passe. Commit** `feat: configuration et lecture des capteurs d'énergie`.

### Task 5 : client Home Assistant — Sonnet

**Files :** Modify `Core/HaClient.cs`. Test `HaClientTests.cs`.

**Interfaces :** Produit
```csharp
internal enum HaProblemKind { Unauthorized, EntityNotFound, Unreachable, Certificate, BadResponse }
internal sealed class HaProblem(HaProblemKind kind, string userMessage, string? entityId = null, Exception? inner = null) : Exception(userMessage, inner) { HaProblemKind Kind; string? EntityId; }
internal sealed class HaClient(HttpClient http, Func<string?> readToken) : IDisposable
{ Task PingAsync(CancellationToken ct); Task<HaState> GetStateAsync(string entityId, CancellationToken ct); Task<IReadOnlyList<HaState>> GetAllStatesAsync(CancellationToken ct); }
```
`Consumes` : `HaAddress`, `HaState`. Routes : `api/`, `api/states/{Uri.EscapeDataString(id)}`, `api/states`. `http.Timeout` et `BaseAddress` sont posés par l'appelant (8 s). Le jeton est relu par `readToken` à chaque appel et nettoyé par `Trim()`. Une `HttpRequestException` dont une cause interne est une `AuthenticationException` donne `Certificate`.

- [ ] **Step 1 : tests avec `FakeHomeAssistant`.** `Get_SendsBearerAndParsesUnit` ; `Get_401_IsUnauthorized` ; `Get_404_IsEntityNotFoundWithEntityId` ; `Get_Html_IsBadResponse` ; `Get_NetworkFailure_IsUnreachable` (le message rappelle de vérifier les permissions) ; `Get_TlsFailure_IsCertificate` (`HttpRequestException` avec cause `AuthenticationException`, le message contient « Oublier le certificat ») ; `GetAll_ParsesFriendlyNameAndDeviceClass` ; `Token_WithWhitespaceAndNewline_IsTrimmed` (Review Focus 3 : jeton `" abc\r\n"`, en-tête `Bearer abc`, aucune exception) ; `OnlyGetIsEverSent` ; `Ping_Hits_ApiRoot`.
- [ ] **Step 2 : vérifier l'échec.**
- [ ] **Step 3 : implémenter.**
- [ ] **Step 4 : `dotnet test` passe. Commit** `feat: client REST Home Assistant`.

### Task 6 : recherche de capteurs et boutons — Sonnet

**Files :** Create `Core/EntityCatalog.cs`. Modify `HomeAssistantModule.cs`. Test `EntityCatalogTests.cs`, `ModuleActionsTests.cs`.

**Interfaces :**
- Produit `internal enum EntityKind { Power, Percent }` et `internal static class EntityCatalog { const int MaxResults = 50; static EntityKind KindFor(string fieldKey); static IReadOnlyList<LookupResult> Search(IEnumerable<HaState> states, EntityKind kind, string query); }`. `battery_soc` et `battery_soc2` sont `Percent`, tout le reste `Power`. Libellé `« Nom · 1124 W (sensor.x) »`. Valeur `{"id":"sensor.x","name":"Nom"}`.
- `LookupAsync(fieldKey, query, ct)` : lit uniquement les secrets, jamais `Services.Settings`. Sans adresse ou jeton : un seul résultat `LookupResult("Remplis d'abord l'adresse et la clé API", <JSON vide>)`, avec une valeur qui ne se lit pas comme un capteur. Erreur réseau : un seul résultat qui dit quoi faire ; jamais d'exception.
- `RunActionAsync("test", ct)` : `PingAsync`, puis `ActionResult(true, "Connexion réussie à Home Assistant.")` ou le message de `HaProblem`. `RunActionAsync("forget_cert", ct)` : `Secrets.Set("cert:ha", null)`, `ActionResult(true, …)`.

- [ ] **Step 1 : tests.** `Search_Power_KeepsOnlyWattSensors` (écarte `kWh`, `°C`, les `binary_sensor`) ; `Search_Percent_PrefersBatteryDeviceClass` ; `Search_IsAccentAndCaseInsensitive` (Review Focus 5 : `energie` trouve `Énergie maison`, `SOLAIRE` trouve `solaire`) ; `Search_MatchesEntityId` ; `Search_CapsAtFifty` ; `Search_EmptyQuery_ListsAll`. Avec `Probe` et un hôte **sans réglages** : `Lookup_ReadsSecretsOnly` ; `Lookup_NoSecrets_ReturnsGuidance` ; `Action_Test_Ok` ; `Action_Test_Unauthorized_SaysSo` ; `Action_ForgetCert_ClearsFingerprint`.
- [ ] **Step 2 : vérifier l'échec.**
- [ ] **Step 3 : implémenter.** Normalisation : `FormD` sans marques diacritiques, minuscules invariantes.
- [ ] **Step 4 : `dotnet test` passe. Commit** `feat: recherche de capteurs et test de connexion`.

### Task 7 : carte énergie, lecture périodique et statuts — Sonnet

**Files :** Create `Cards/ICard.cs`, `Cards/Energy/EnergyCard.cs`. Modify `HomeAssistantModule.cs`. Test `EnergyCardTests.cs`.

**Interfaces :**
- Produit `internal interface ICard : IAsyncDisposable { bool IsAnimating { get; } void Start(); void Render(SKCanvas canvas, ModuleRenderContext ctx); }`.
- Produit `internal sealed record CardContext(ModuleServices Services, HaClient Client, Action Invalidate, Action<ModuleHealth,string?> SetStatus);`.
- Produit `internal sealed record EnergyState(EnergySnapshot Snapshot, EnergyFlows Flows, bool Stale);`, publié par `internal EnergyState? Current` (`volatile`, remplacé en entier).
- `EnergyCard(CardContext context, EnergyConfig config)`. `Consumes` : `EnergyReader.Combine`, `EnergyFlows.Compute`, `HaClient.GetStateAsync`. `EnergyRenderer` arrive en Task 9 : ici, `Render` ne dessine rien.
- Constantes : `RefreshEvery = 3 s`, `FailuresBeforeOffline = 3`. Motif de fermeture du contrat (`_closed`, `_refresh`, `_stop`). Minuterie via `Services.Time`.

Comportement : § 6 de la spec. 401 et 404 passent `Offline` dès la première fois. Une panne réseau ne passe `Offline` qu'à la 3ᵉ, avec `Stale = true` et les dernières valeurs gardées. Le statut ne change que s'il diffère du précédent. Retour automatique en `Online`.

- [ ] **Step 1 : tests avec `host.Time.Advance` et `WaitUntilAsync`.** `Poll_PublishesFlows_AndGoesOnline` (Probe : valeurs de l'auteur, Home 771,62) ; `Poll_EveryThreeSeconds` (2 lectures après 3 s) ; `Unauthorized_GoesOfflineImmediately` ; `MissingEntity_NamesIt` ; `NetworkFailure_OfflineOnlyAfterThree_ThenRecovers` ; `Stale_KeepsLastValues` ; `GridUnavailable_GoesOffline_NamingEntity` ; `SolarUnavailable_StaysOnline` ; `SlowServer_NeverOverlapsReads` (Review Focus 4 : `Delay` bloquant, avancer de 9 s, pas plus d'une lecture par capteur en cours) ; `Dispose_DuringRead_DoesNotThrow` (Review Focus 4) ; `TwoCases_KeepSeparateStates` (deux hôtes, capteurs différents).
- [ ] **Step 2 : vérifier l'échec.**
- [ ] **Step 3 : implémenter**, et `HomeAssistantModule` crée le client avec `HaAddress.CertificateKey`, un `Timeout` de 8 s, choisit la carte selon `display`, délègue `Render`, `IsAnimating` et `DisposeAsync`.
- [ ] **Step 4 : `dotnet test` passe. Commit** `feat: lecture périodique et statuts de la carte énergie`.

### Task 8 : dessins en SkiaSharp — Sonnet

**Files :** Create `Cards/Energy/Art/IsoPainter.cs`, `HouseModernArt.cs`, `HouseClassicArt.cs`, `PanelsArt.cs`, `PylonArt.cs`, `BatteryArt.cs`, `ArtCache.cs`. Test `ArtTests.cs`.

**Interfaces :**
- Produit `internal sealed class IsoPainter(SKCanvas canvas)` : `Iso(float k, float ox, float oy)` rend une projection `(x,y,z) → SKPoint` ; `[x̂ = ox + (x−y)·0,866·k, ŷ = oy + (x+y)·0,5·k − z·k]` ; `Poly(SKPoint[], Fill, Stroke?)` ; `Lines(...)`. `Fill` couvre couleur unie et dégradé linéaire calé sur la boîte englobante de la forme, comme `objectBoundingBox` en SVG.
- Produit `internal enum ArtKind { HouseModern, HouseClassic, Panels, Pylon, Battery }` et `internal sealed class ArtCache : IDisposable { ArtImages Get(ArtKind kind, float scale); }`, avec `internal sealed record ArtImages(SKImage Dark, SKImage Lit, SKPoint Origin, float Scale);`.

Méthode : porter ligne à ligne les fonctions `houseModern`, `houseClassic`, `panels`, `pylon`, `battery` du `<script>` de la maquette, et les dégradés de ses `<defs>`. Couches à deux états : `Dark` (fenêtres et cellules éteintes) et `Lit` (allumées, avec la variation par cellule cuite dedans, valeurs tirées du même générateur que la maquette, graine 7). Le rendu fera un fondu de `Lit` sur `Dark`. Le lit en `#ffb445` avec le halo `winGlow` (flou 3) est cuit dans l'image. Pylône : `Lit` vide, isolateurs et segments de batterie dessinés à part en Task 9.

- [ ] **Step 1 : tests.** `Iso_MatchesMockupFormula` (2 points connus) ; `EveryArt_DrawsSomething_WithinBox` (`[Theory]` sur les 5, pixels non transparents, aucun au-delà de la boîte : 200 × 180 environ, 85 × 145 pour la batterie) ; `Panels_LitDiffersFromDark` ; `Pylon_LitIsEmpty` ; `Cache_ReusesSameImage_ForSameScale`.
- [ ] **Step 2 : vérifier l'échec.**
- [ ] **Step 3 : implémenter.**
- [ ] **Step 4 : contrôle visuel.** Test `Art_WriteSamples` qui écrit chaque dessin en PNG dans `gallery/art/`. Ouvrir les images et les comparer à la maquette : mêmes volumes, mêmes couleurs, aucun sol.
- [ ] **Step 5 : `dotnet test` passe. Commit** `feat: dessins de la maison, des panneaux, du pylône et de la batterie`.

### Task 9 : composition, filaments et galerie — Sonnet

**Files :** Create `Cards/Energy/EnergyLayouts.cs`, `FilamentPainter.cs`, `EnergyRenderer.cs`. Modify `EnergyCard.cs`. Test `EnergyRendererTests.cs`, `GalleryTests.cs`.

**Interfaces :**
- Produit `internal sealed record ArtPlacement(ArtKind Kind, float X, float Y, float Scale);`, `internal sealed record FlowPath(FlowKey Key, SKColor Color, SKPoint[] Bezier);`, `internal sealed record TextSlot(string Role, float X, float Y, bool Centered, float Size);`, `internal sealed record HaloSpot(string Role, float X, float Y, float Radius);`, `internal enum FlowKey { SolarToHome, SolarToGrid, GridToHome, SolarToBattery, BatteryToHome, GridToBattery }`, `internal sealed record EnergyLayout(IReadOnlyList<ArtPlacement> Arts, IReadOnlyList<FlowPath> Flows, IReadOnlyList<TextSlot> Texts, IReadOnlyList<HaloSpot> Halos)` et `internal static class EnergyLayouts { const float Width = 700, Height = 434; static EnergyLayout Get(LayoutKind kind, bool withBattery); }`. Les six jeux de valeurs se copient depuis les `<svg data-variant>` de la maquette.
- Produit `internal sealed class FilamentPainter { void Advance(double dtSeconds, EnergyFlows flows); void Draw(SKCanvas canvas, EnergyLayout layout, EnergyFlows flows); }`, constantes du § 8.
- Produit `internal sealed class EnergyRenderer : IDisposable { EnergyRenderer(EnergyConfig config); bool IsAnimating(EnergyState? state); void Render(SKCanvas canvas, ModuleRenderContext ctx, EnergyState? state); internal static string FormatPower(double watts); internal static float FitSize(string text, float wanted, float maxWidth, Func<string,float,float> measure); }`.

Règles : ordre de dessin halos, lignes de fond, filaments, dessins, lumières dynamiques, textes. Une seule mise à l'échelle uniforme, centrée, depuis `ctx.Bounds`. `dt` vient de l'écart entre `ctx.Now` successifs, plafonné à 0,5 s. Pas de flou par filament : la lueur se fait par un second disque plus large et peu opaque. Les grandeurs affichées s'approchent de la cible avec `1 − exp(−dt/0,35)`. `b2g` réutilise le tracé `GridToBattery`, parcouru à l'envers. Sans données (`state` nul) : rien n'est dessiné. Si `Stale` : filaments arrêtés, textes atténués. Avec batterie et `BatteryPowerKnown` faux : « — ». Sans niveau de batterie : jauge éteinte, libellé sans pourcentage. Textes neutres en `ctx.Theme.Text` et `TextMuted`, polices par `ModuleDrawing`, taille multipliée par `ctx.TextScale`, largeur limitée par `FitSize`. Unité à 0,42 fois la taille, opacité 0,65.

- [ ] **Step 1 : tests.** `FormatPower`, qui arrondit au watt avant de choisir l'unité : 640 → `640 W`, 999,6 → `1,00 kW`, 3420 → `3,42 kW`, 123456 → `123,46 kW`, 0 → `0 W`. `Layouts_Top_NoBattery_MatchesMockup` (positions et `s2h`) ; `Layouts_WithBattery_HasSixFlows` ; `Layouts_NoBattery_HasNoBatteryArtOrSlot` (`[Theory]` sur les 3, exigence **[Décidé]** de la spec). `Filament_ZeroFlow_DrawsNothing` ; `Filament_SpeedFollowsPower` (`55 + kW × 75`). `FitSize_ShrinksLongLabel_AtMinSizeAndLargeText` (`Consommation · 100 % solaire`, taille minimale, `TextScale` 1,2). `Render_Stale_StopsAnimating` ; `Render_NoData_DrawsNothing` ; `IsAnimating_FalseWhenNothingMoves` ; `Render_IsUniformlyScaledAndCentered_At8x4`.
- [ ] **Step 2 : vérifier l'échec.**
- [ ] **Step 3 : implémenter**, puis brancher `EnergyCard.Render` et `IsAnimating`.
- [ ] **Step 4 : galerie.** `GalleryTests.WriteAll` : 3 dispositions × 2 maisons × 3 tailles (5×3, 6×4, 8×4) × 3 thèmes × avec et sans batterie, plus les scénarios soleil, nuages, nuit, dans `gallery/`. Assertions : chaque image a des pixels, et les 3 thèmes rendent.
- [ ] **Step 5 : contrôle visuel.** Ouvrir un échantillon : les 3 dispositions avec et sans batterie, en 6×4 et 5×3, `pixel` et `classique`. Vérifier contre la maquette : aucun flux sous la maison, aucun recouvrement de texte, batterie absente sans batterie.
- [ ] **Step 6 : coût.** Test `Render_AverageCost` en `Release` : 300 rendus en 6×4 avec batterie, moyenne sous 15 ms. Sinon, réduire le travail par image.
- [ ] **Step 7 : `dotnet test` passe. Commit** `feat: rendu animé de la carte énergie`.

### Task 10 : banc d'essai, paquet et documentation — Haiku

**Files :** Create `README.md`, `LICENSE`, `HomeAssistant/icon.png`. Modify `HomeAssistant.csproj` (copie de `icon.png`).

- [ ] **Step 1 : icône.** PNG 256 × 256 produit à partir de `HouseModernArt` et d'un filament, par un test `WriteIcon`, sans le logo Home Assistant.
- [ ] **Step 2 : `LICENSE`** GPL-3.0 complète, texte officiel. **`README.md`** en français : à quoi sert le module, comment créer la clé API, comment choisir les capteurs avec les trois formats, permissions, limites (même PC non pris en charge), mention de non-affiliation.
- [ ] **Step 3 : banc d'essai.** `dotnet build -c Release`, puis `ashboard-module check HomeAssistant/bin/Release/net10.0 --out check-out`. Attendu : `Compatible avec Ashboard Vision (SDK 1.0).`, code de sortie 0, aucune ligne `✖`.
- [ ] **Step 4 : paquet.** `ashboard-module pack HomeAssistant/bin/Release/net10.0 --out pack-out`. Attendu : `pack-out/macorou.home-assistant-1.0.0.avmod` et son `.sha256`.
- [ ] **Step 5 : `dotnet test` passe. Commit** `chore: paquet, licence et documentation`.

### Task 11 : essai réel et relecture finale — Opus 5.5 pour la relecture

- [ ] **Step 1 : relecture finale** de toute la branche par Opus 5.5 contre la spec, ses anti-objectifs et les Global Constraints. Corriger ce qu'elle trouve, `dotnet test` passe.
- [ ] **Step 2 : essai par l'utilisateur.** Lui demander, dans Ashboard Vision, Paramètres, Modules complémentaires : Mode développeur, Ajouter un dossier (`HomeAssistant/bin/Release/net10.0`), cocher « Réseau local », poser la carte, saisir adresse et clé API, cliquer sur Tester la connexion, puis choisir ses capteurs de production et de réseau. Vérifier avec lui : valeurs correctes, flux animés, pas de « Consommation élevée » dans la fenêtre de réglage.
- [ ] **Step 3 : s'arrêter.** La création du dépôt `Macorou/ashboard-home-assistant`, l'envoi et la version GitHub attendent une demande explicite de l'utilisateur.
