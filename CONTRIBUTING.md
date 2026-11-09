# Contribuir a Hatsu Desktop

1. **Rama:** trabaja en una rama (`feat/…`, `fix/…`) y abre un PR; no se sube directo a la rama estable.
2. **Compilar y probar:** .NET 10. `dotnet test src/<Proyecto>.Tests`. En Linux, la lógica pura corre con `-p:EnableWindowsTargeting=true`; lo gráfico solo compila (se valida en Windows).
3. **Reglas del código:**
   - La lógica va en los proyectos `*.Core` (sin dependencias de Windows) con pruebas; lo específico de Windows va en `*.Win`.
   - Sin APIs privadas o inestables de Windows, sin dependencias de PowerToys, sin telemetría, sin drivers.
   - Los módulos nuevos usan `FeatureManager` (se apagan de verdad) y `HotkeyRegistry` (conflictos de atajos).
4. **Honestidad en los informes:** «compila» y «pasa pruebas» no equivalen a «validado». Cada documento marca qué se probó en un Windows real.
5. **CI:** cada push a una rama dispara `ci`; un push nuevo cancela el run en curso, así que agrupa los cambios. Los artifacts son temporales y no se publican.
6. **Isla (Hatsu Notch):** vive en `PipeSick/notch-win`; esta repo fija el commit en `tools/notch-pin.txt`. Cambia el pin solo en tu rama de trabajo.
7. **Secretos y datos:** no subas tokens, estado de ejecución, registros ni archivos `.bak-*`.
