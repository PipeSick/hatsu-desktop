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
si activaste «Conservar», cifrada). Ajustes › Isla › Portapapeles: límite, «No guardar contraseñas ni claves», «Conservar al reiniciar» (con su estado), «Ignorar las apps de la lista» y la
**lista editable** (nombres de proceso separados por comas; vacío = lista por defecto; se validan los caracteres), y **Borrar el historial** (fijados incluidos).
Una app se identifica por el nombre de su proceso: dos programas distintos con el mismo nombre de ejecutable cuentan como el mismo.
