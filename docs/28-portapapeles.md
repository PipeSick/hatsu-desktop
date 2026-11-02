# 28 · Portapapeles avanzado (Isla)

Texto e imágenes, búsqueda, fijados (favoritos), volver a copiar (como texto sin formato), eliminar, borrar.

Privacidad (Ajustes › Isla › Portapapeles):
- **No guarda** lo que parece contraseña, clave privada o token (detector conservador: claves PEM, AWS, GitHub, JWT, `password=…`, cadenas aleatorias de 12–128 caracteres
  con alta entropía). Un falso positivo solo significa que esa copia no queda en el historial; la Isla dice cuántas se omitieron (solo el número).
- Respeta las marcas de Windows que ponen los gestores de contraseñas (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory=0`,
  `Clipboard Viewer Ignore`) y una lista de **apps excluidas** (KeePass, KeePassXC, 1Password, Bitwarden… por defecto), por el proceso dueño del portapapeles.
- Límite configurable (5–500).
- **Conservar al reiniciar** (desactivado por defecto): archivo cifrado con DPAPI para tu usuario (`%LOCALAPPDATA%\NotchWin\clipboard.dat`); al desactivarlo se borra.
- Nunca se registra el contenido ni se sincroniza con nada externo.

Limitaciones: la detección es heurística; una app que no marque sus datos y no esté en la lista podría dejar una contraseña en el historial (en memoria, y en disco solo
si activaste «Conservar», cifrada). La lista de apps excluidas por ahora no se edita desde Ajustes (valor por defecto).
