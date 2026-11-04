# SignPath: configuraciones de artefacto (borrador, sin enviar)
- `binaries.artifact-configuration.xml` — se **genera** con `tools/signing/New-ArtifactConfiguration.ps1 -Mode Binaries` a partir del paquete real y se versiona; el flujo (`docs/signing/release-signed.yml.draft`) falla si deja de coincidir con los binarios propios.
- `installer.artifact-configuration.xml` — firma del MSI (`-Mode Installer`).
Ninguno de los dos se ha subido a SignPath: se pegan en su editor (que valida la sintaxis) **cuando el proyecto esté aprobado**.

**Pendiente (a propósito):** `binaries.artifact-configuration.xml` aún no existe en el repositorio. Se genera una sola vez con `New-ArtifactConfiguration.ps1 -Mode Binaries` sobre un paquete real (hacen falta los 26 binarios propios) y se revisa a mano antes de versionarlo. El borrador de flujo lo exige antes de activarse.
