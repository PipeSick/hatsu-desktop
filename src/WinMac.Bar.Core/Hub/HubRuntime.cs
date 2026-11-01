namespace WinMac.Bar.Hub;

/// <summary>Lo que el panel multifunción y el Tiempo necesitan de la ejecución: datos vivos, prueba de velocidad y tiempo. Lo crea el anfitrión una vez.</summary>
public sealed record HubRuntime(HubMonitor Monitor, SpeedTestRunner Speed, WeatherService Weather);

/// <summary>Lo que el constructor de paneles lee (instantánea): datos vivos, estado de la prueba, del tiempo y el volumen elegido.</summary>
public sealed record HubData(HubMonitor Monitor, SpeedTestState Speed, WeatherState Weather, string? SelectedVolume, bool Use24h);

/// <summary>Una opción de un menú de selección (dispositivo de salida, micrófono…).</summary>
public sealed record Choice(string Id, string Text, bool Checked);
