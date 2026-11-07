# Zarpazo: plan de funciones de la app de capturas para Windows (estilo Lightshot/Snagit)

La app se construye en C# con .NET 8, usando WPF para el editor. Las funciones se reparten en tres niveles de prioridad:

- **MVP:** lo mínimo para igualar a Lightshot.
- **Después:** mejoras que se montan sobre el MVP.
- **Avanzado:** funciones propias de Snagit, las más costosas de construir.

## 1. Captura

El MVP cubre la captura de área con atajo global; las capturas de ventana, scroll y video quedan para más adelante.

| Función | Prioridad |
| --- | --- |
| Captura de área arrastrando el mouse (pantalla congelada y oscurecida) | MVP |
| Atajo global configurable (PrtScn por defecto) | MVP |
| Soporte multi-monitor y distinto escalado DPI | MVP |
| Captura de pantalla completa | Después |
| Captura de ventana (detecta la ventana bajo el cursor y la resalta) | Después |
| Captura con retardo (3, 5 o 10 segundos) | Después |
| Repetir la última región capturada | Después |
| Lupa con zoom de píxeles y coordenadas para afinar la selección | Después |
| Captura con scroll (páginas largas) | Avanzado |
| Captura de GIF y video de pantalla | Avanzado (hecho: botón Grabar, Ctrl+G; GIF y video MP4 con micrófono y sonido de Windows) |
| Captura de texto con OCR (copiar el texto de una imagen) | Avanzado (hecho: botón "Copiar texto", Ctrl+T) |

## 2. Editor de anotaciones

El MVP incluye las formas básicas, el texto y deshacer; el desenfoque, la numeración y mover objetos llegan después.

| Función | Prioridad |
| --- | --- |
| Rectángulo, línea, flecha y lápiz libre | MVP |
| Texto | MVP |
| Selector de color y grosor | MVP |
| Deshacer y rehacer | MVP |
| Resaltador semitransparente | Después |
| Elipse y círculo | Después |
| Numeración de pasos (1, 2, 3...) | Después |
| Desenfoque y pixelado para ocultar datos sensibles | Después |
| Mover, redimensionar y borrar objetos ya dibujados | Después |
| Cuentagotas de color | Después |
| Globos de texto (callouts), sellos y emojis | Avanzado |
| Sombras, bordes y efectos de imagen | Avanzado (hecho: botón Embellecer: fondo, margen, esquinas y sombra al copiar y guardar) |
| Recorte posterior y redimensionado del lienzo | Avanzado |

## 3. Salida

El MVP se limita a copiar al portapapeles y guardar como imagen; subir a la nube con un link es una función avanzada.

| Función | Prioridad |
| --- | --- |
| Copiar al portapapeles | MVP |
| Guardar como PNG o JPG | MVP |
| Guardar automáticamente en una carpeta elegida | Después (hecho: pestaña Guardado de Configuración) |
| Nombres de archivo con plantilla (fecha, hora, contador) | Después (hecho: {fecha}, {hora}, {n}) |
| Imprimir | Después |
| Abrir la imagen en otro programa | Después |
| Subir a la nube y copiar el link (servidor propio, Imgur, etc.) | Avanzado |
| Compartir por correo o mensajería | Avanzado |
| Fijar la captura sobre la pantalla (flotante, con opacidad) | Avanzado (hecho: botón Fijar, Ctrl+P) |

## 4. Historial y biblioteca

Nada del historial entra en el MVP; la búsqueda por texto depende de tener antes el OCR.

| Función | Prioridad |
| --- | --- |
| Historial de capturas con miniaturas | Después |
| Reabrir una captura anterior para seguir editándola | Después |
| Borrado automático de capturas antiguas | Después |
| Buscar dentro de las capturas por texto (usando OCR) | Avanzado |

## 5. Integración con Windows

El MVP necesita el icono en la bandeja y una sola instancia en ejecución; el resto es pulido posterior.

| Función | Prioridad |
| --- | --- |
| Icono en la bandeja del sistema con menú contextual | MVP |
| Una sola instancia en ejecución | MVP |
| Iniciar con Windows | Después |
| Notificaciones nativas de Windows | Después |
| Modo oscuro y claro según el sistema | Después |
| Actualización automática | Después |

## 6. Configuración

Todas las opciones de configuración quedan para después del MVP, que arranca con valores fijos.

| Función | Prioridad |
| --- | --- |
| Cambiar atajos de teclado | Después |
| Elegir formato, calidad y carpeta de guardado | Después |
| Herramienta y color por defecto | Después |
| Idiomas (español e inglés) | Después |
| Comportamiento tras capturar: copiar, guardar, abrir editor o subir | Después |

## Notas técnicas

- **Atajo global:** `RegisterHotKey` (user32.dll). En Windows 11 hay que desactivar "Usar el botón Impr Pant para abrir recorte de pantalla" para evitar el conflicto.
- **Bandeja:** `NotifyIcon` (WinForms) o la librería Hardcodet.NotifyIcon.Wpf.
- **Captura:** empezar con `Graphics.CopyFromScreen` o BitBlt; más adelante, Windows.Graphics.Capture o DXGI Desktop Duplication (GIF y video).
- **Overlay:** ventana `WindowStyle=None`, `Topmost`, `ShowInTaskbar=False`, que cubra el escritorio virtual completo.
- **Selección de ventana bajo el cursor:** `WindowFromPoint` más `DwmGetWindowAttribute` con `DWMWA_EXTENDED_FRAME_BOUNDS`.
- **Editor:** cada anotación se guarda como objeto vectorial para poder deshacer, mover y editar.
- **Distribución:** self-contained single file; instalador por usuario con Inno Setup (`tools/build-installer.ps1`, genera `publish/Zarpazo-Setup-<versión>.exe`); inicio con Windows mediante la clave `Run` del registro; una sola instancia con mutex.

## Puntos delicados

1. **DPI y multi-monitor:** declarar `PerMonitorV2` en el manifiesto desde el primer día; monitores con escalado distinto y coordenadas negativas.
2. **Contenido protegido** (DRM, algunos juegos) sale negro y no tiene solución limpia.
3. **Parpadeo del overlay:** mostrar la ventana solo cuando la captura ya está lista.
4. **Captura con scroll:** es compleja, dejarla para el final.

## Orden de trabajo

1. **Esqueleto:** bandeja, atajo global, captura de todos los monitores y overlay de selección.
2. **Editor básico:** formas, texto, color y grosor, deshacer y rehacer.
3. **Salida:** copiar al portapapeles y guardar PNG/JPG.
4. **Después:** funciones marcadas "Después", de una en una.
5. **Avanzado:** funciones estilo Snagit.
