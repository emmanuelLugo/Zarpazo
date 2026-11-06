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
- Captura inicial con Graphics.CopyFromScreen; migrar a Windows.Graphics.Capture solo cuando haga falta GIF o video.

## Convenciones
- Código y nombres en inglés; textos de la interfaz en español.
- Una clase por archivo, y separar la lógica de captura, el overlay y el editor en carpetas distintas.
