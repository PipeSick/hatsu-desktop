# 29 · Workspace Manager

- **Guardar** la distribución de las ventanas abiertas (proceso, clase, título, rectángulo normal, estado normal/maximizada/minimizada, monitor) en
  `%APPDATA%\WinMac\workspaces.json` (legible, versionado).
- **Restaurar**: empareja cada ventana guardada con una abierta (mismo proceso y clase; título exacto, luego parecido); si el monitor ya no existe va al principal;
  las posiciones se escalan al área de trabajo del monitor actual (de 4K a 1080p, etc.) y se mantienen dentro.
- **Lado a lado** y **cuadrícula** en el monitor del cursor.
- APIs públicas (`EnumWindows`, `Get/SetWindowPlacement`). Windows aplica los tamaños mínimos/máximos de cada app. Una app con más privilegios que WinMac (UIPI) o colgada
  se informa y no se toca. No se usan ganchos ni inyección.
- **Nunca inicia apps** salvo que el usuario escriba `launchPath` para esa ventana en el archivo.
- Desde Spotlight o `WinMac.Bar.App --workspace save|restore|side|grid`.
Validado en el CI con ventanas reales del Bloc de notas (un monitor); multimonitor y DPI distintos: geometría **simulada** en pruebas.
