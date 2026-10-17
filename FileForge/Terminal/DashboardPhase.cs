namespace FileForge.Terminal;

/// <summary>
/// Définit les phases du cycle de vie du tableau de bord console.
/// </summary>
public enum DashboardPhase
{
    /// <summary>
    /// Phase d'analyse, d'inventaire et de comparaison des répertoires.
    /// </summary>
    Scanning,

    /// <summary>
    /// Phase active de transfert et de copie des fichiers.
    /// </summary>
    Copying,

    /// <summary>
    /// Phase où aucun fichier ne nécessite de transfert (disque à jour).
    /// </summary>
    UpToDate,

    /// <summary>
    /// Phase de finalisation et de synthèse une fois les opérations achevées.
    /// </summary>
    Completed
}
