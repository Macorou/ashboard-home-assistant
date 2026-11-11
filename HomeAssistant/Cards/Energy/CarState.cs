namespace HomeAssistant;

internal enum CarColor { Blue, Yellow, White }

/// <summary>L'état de la voiture à l'écran : débranchée, branchée sans charger, ou en charge.</summary>
internal enum CarAspect { Unplugged, Waiting, Charging }

/// <summary>Ce qu'on lit de la voiture ; un champ est null quand son capteur manque ou est indisponible.</summary>
internal sealed record CarReading(double? Watts, bool? Plugged, double? SocPercent, CarAspect Aspect);

internal static class CarState
{
    /// <summary>À partir de cette puissance, la voiture charge (en dessous, c'est le bruit de mesure ou une veille).</summary>
    internal const double ChargingWatts = 50;

    /// <summary>La puissance fait foi : elle suffit à dire que la voiture charge, même si le capteur de prise dit autre chose.</summary>
    internal static CarAspect AspectOf(double? watts, bool? plugged) =>
        watts >= ChargingWatts ? CarAspect.Charging
        : plugged == true ? CarAspect.Waiting
        : CarAspect.Unplugged;
}
