# Zarpazo

App de capturas de pantalla para Windows, estilo Lightshot/Snagit.
El plan completo de funciones está en PLAN.md.

## Stack
- C# .NET 8, WPF
- Atajo global con RegisterHotKey, icono en bandeja (NotifyIcon), una sola instancia (mutex)
- Manifiesto con PerMonitorV2 desde el inicio (DPI y multi-monitor)
- Publicación como self-contained single file

## Reglas de trabajo
- Trabajar por fases: primero todo lo marcado MVP en PLAN.md, en el orden de la sección "Orden de trabajo".
- Antes de añadir una función marcada "Después" o "Avanzado", confirmar conmigo.
- Avanzar en bloques pequeños y decirme cómo ejecutar y qué probar al terminar cada uno.
- Hacer un commit de git al cerrar cada bloque.

## Decisiones técnicas
- El overlay de captura se muestra solo cuando la captura ya está lista (evitar parpadeo).
- El overlay cubre el escritorio virtual completo, con soporte de coordenadas negativas y distinto escalado por monitor.
- Las anotaciones son objetos vectoriales (para deshacer, rehacer y mover), no píxeles pintados.
- Grabar copia la pantalla con DXGI Desktop Duplication (Vortice.Direct3D11) cuando la zona cabe en un solo monitor: unos 8 ms por cuadro en 1080p contra 33 ms de Graphics.CopyFromScreen, que dejaba cuadros tarde y el video a tirones. CopyFromScreen queda de respaldo (zona entre dos monitores, pantalla rotada, escritorio seguro de UAC). GIF a 12 fps y MP4 a 30 fps. No se usa Windows.Graphics.Capture porque dibuja un borde amarillo en Windows 10.
- El MP4 se codifica en vivo con Media Foundation (MediaStreamSource + MediaTranscoder); el audio (micrófono y sonido de Windows) se captura con NAudio (WASAPI) y se mezcla en trozos de 20 ms.

## Convenciones
- Código y nombres en inglés; textos de la interfaz en español.
- Una clase por archivo, y separar la lógica de captura, el overlay y el editor en carpetas distintas.
