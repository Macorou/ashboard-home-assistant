using Trofeo.ModuleKit;

namespace HomeAssistant;

/// <summary>Les mesures du Bilan du jour (espace de la maquette) et la taille minimale de chaque forme.</summary>
internal static class DailyLayout
{
    internal const float StripWidth = 464, StripHeight = 100, ListWidth = 226, ListHeight = 322;

    /// <summary>Le message à afficher si la case est trop petite pour la forme, sinon null.</summary>
    internal static string? TooSmallMessage(DailyShape shape, GridSize size)
    {
        var (columns, rows) = shape == DailyShape.Strip ? (4, 1) : (2, 3);
        return size.Columns < columns || size.Rows < rows ? $"Agrandis la case : {columns} × {rows} minimum" : null;
    }
}
