using UnityEngine;

public static class GlobalData
{
    public static string IpAlvo = "192.168.4.1";
    public static int Porta = 8888;
    public static int PortaDescoberta = 8888;

    // Define se o visualizador iniciará com dados simulados (para testes sem o hardware pronto)
    public static bool IsSimulationMode = false;
    public static bool HasViewerModeSelection = false;
}
