# Cambios

Todos los cambios importantes de Zarpazo se anotan aquí. El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y las versiones siguen [SemVer](https://semver.org/lang/es/).

El flujo de publicación lee la sección de la versión que se etiqueta (`## [0.1.0]`) y la usa como notas de la release.

## [Sin publicar]

### Cambiado
- Usa mucha menos memoria RAM: el ejecutable ya no va comprimido (la compresión se descomprimía en memoria) y pasa de ~118 MB a ~49 MB recién abierto, y de ~190 MB a ~119 MB tras varias capturas.
- Al grabar video los cuadros se reutilizan en vez de crear uno nuevo cada vez, y la memoria se devuelve al terminar la grabación o cerrar una captura.

### Corregido
- La memoria se disparaba al grabar video y se quedaba alta después de cerrarlo.
- Al pulsar Impr Pant con una ventana maximizada debajo, la pantalla no se oscurecía porque se resaltaba la ventana entera; ahora queda oscura. Tampoco se resalta ya una ventana escondida detrás de la maximizada.

## [0.2.0] - 2026-11-07

### Añadido
- El panel de grabación muestra el nivel del micrófono y del sonido de Windows en vivo, para comprobar que se oyen antes de empezar a grabar.

### Cambiado
- Los medidores de la barra de grabación son más grandes y cambian de verde a ámbar y rojo según el volumen.

## [0.1.0] - 2026-11-07

Primera versión.

### Añadido
- Captura de área y de ventana con atajo global (Impr Pant), icono en la bandeja y una sola instancia.
- Captura de pantalla completa, captura con retardo (3, 5 o 10 segundos) y repetir la última área, desde el menú de la bandeja; la tecla F elige el monitor en el selector.
- Barra del editor ordenada: herramientas agrupadas en menús (formas, dibujo, medir), color y grosor solo cuando aplican, y OCR, fijar y embellecer bajo el menú «Más».
- Lupa con zoom de píxeles y coordenadas, y soporte para varios monitores y escalado distinto (PerMonitorV2).
- Editor de anotaciones vectoriales: rectángulo, elipse, línea, flecha, lápiz, resaltador, texto, pasos numerados, regla, pixelado y cuentagotas; mover, borrar, deshacer y rehacer.
- Copiar al portapapeles y guardar como PNG o JPG, con guardado automático y plantilla de nombre.
- Embellecer (fondo, margen, esquinas y sombra), fijar la captura en pantalla y copiar texto con OCR.
- Grabación de GIF y video MP4 con micrófono y sonido de Windows, pausa, cuenta atrás, efectos de puntero y teclado, y recorte del video.
- Historial de capturas y grabaciones, ajustes de calidad e inicio con Windows.
- Instalador por usuario sin permisos de administrador, y ejecutables sueltos `Zarpazo.exe` y `Zarpazo-ligero.exe`.

[Sin publicar]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/emmanuelLugo/Zarpazo/releases/tag/v0.1.0
