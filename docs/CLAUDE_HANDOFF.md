# Traspaso para Claude (léelo primero tras un /clear)

Actualizado: 2026-11-02. Complementa `docs/ESTADO-SESION.md` (contexto anterior: repos, PC del usuario, reglas de trabajo).

## Plan en curso (orden obligatorio, pedido por el usuario)
1. **NotchWin: volumen, brillo, notificaciones y prioridades** — implementado (docs/21). Ver «Estado por etapa».
2. Launchpad y tecla Windows (índice de apps reutilizable).
3. Spotlight (reutiliza el índice y el lanzador del Launchpad).
4. Global Menu, opción A (menús de la app activa en la barra, sin quitar los originales).
5. Validación general y este traspaso con el informe final.

Reglas: commits separados por etapa; validar en el CI de Windows; nada de hooks invasivos, inyección ni cambios permanentes de Windows;
nunca tocar resolución, monitores, red, audio ni reiniciar el PC del usuario; documentar lo que no se puede probar en CI.

## Cómo trabajar
- `apt-get install -y dotnet-sdk-10.0` si falta. Pruebas de lógica en Linux: `dotnet test src/WinMac.Bar.Core.Tests` (y Host.Core, Dock.Core, Apps.Core…), `dotnet test /home/user/notch-win/tests/NotchWin.Core.Tests`.
- NotchWin (WinUI) solo compila en Windows: lo valida su CI (`PipeSick/notch-win`, workflow `build`) y el job de integración de WinMac, que compila el commit de `tools/notch-pin.txt`.
  Al cambiar NotchWin: push a su rama `claude/relaxed-turing-cijzko`, luego actualizar `tools/notch-pin.txt` en WinMac.
- Logs del CI: `mcp__github__get_job_logs` con `tail_lines` pequeño (los registros con imágenes en base64 son enormes).

## Estado por etapa
| Etapa | Estado | Commits | CI |
|---|---|---|---|
| 0. Captura de la bandeja (sesión anterior) | cerrada a nivel de CI (run 75: 3 de 3 iconos en el desplegable real) | d309ed9 | verde |
| 1. Isla informativa | implementada + pruebas | NotchWin 14c374b, 58d14d9; WinMac (este commit) | ver último run |
