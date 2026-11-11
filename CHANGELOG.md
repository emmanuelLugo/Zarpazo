# Cambios

Todos los cambios importantes de Zarpazo se anotan aquí. El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y las versiones siguen [SemVer](https://semver.org/lang/es/).

El flujo de publicación lee la sección de la versión que se etiqueta (`## [0.1.0]`) y la usa como notas de la release.

## [Sin publicar]

### Añadido
- Tamaño con la rueda del mouse, como en Lightshot: con el pincel, la flecha, la línea, el rectángulo, la elipse, el texto o los números, girar la rueda sobre la captura cambia el grosor (o el tamaño de la letra y de los números). También vale mientras dibujas, mientras escribes un texto y, con la herramienta Mover, sobre la anotación elegida (se puede deshacer, y varios giros seguidos cuentan como un solo paso). Un círculo translúcido junto al puntero muestra el tamaño que tendrá lo siguiente que dibujes.
- Configuración rediseñada: un menú lateral con seis secciones (General, Atajos, Captura, Grabación, Guardado y Acerca de) en vez de pestañas, con la ventana más ancha. Los tres atajos quedan juntos en «Atajos», la calidad del GIF y del video pasa a «Grabación», y todo lo de guardar (automático, a mano, nombre e historial) a «Guardado». Cada sección tiene un botón «Restablecer esta sección» que vuelve a los valores de fábrica solo ahí, sin guardar hasta pulsar Aceptar. Las actualizaciones pasan a «General».
- Configuración > Captura > «Elegir el área»: casilla para mostrar u ocultar la lupa (zoom con la posición y el color) que sigue al cursor al elegir el área. Viene activada; el cuentagotas de la barra de herramientas no cambia.

### Cambiado
- El botón de grosor con sus tres puntos (fino, medio y grueso) ya no está en la barra de herramientas: el tamaño se cambia con la rueda del mouse, en cualquier valor entre 1 y 30. El último tamaño se recuerda como antes.

### Corregido
- El nombre de archivo de la configuración (por ejemplo «Cap {fecha} {hora}») ahora también vale al copiar y pegar una captura o una grabación como archivo, no solo al guardar. En el archivo que se pega los espacios pasan a ser guiones bajos (Cap_2026-11-11_15-02-57.png) para que la ruta no necesite comillas.

## [0.5.1] - 2026-11-11

### Cambiado
- Atajos globales: Mayús sola ya vale junto con RePág, AvPág, Inicio o Fin (por ejemplo, Mayús+RePág). Antes todo atajo necesitaba Ctrl, Alt o Win. Con Mayús, las flechas y las letras siguen sin aceptarse.

## [0.5.0] - 2026-11-10

### Añadido
- Historial: filtro por tipo (Todo, Capturas, Grabaciones, Conservadas), menú con clic derecho y atajos: Supr elimina, Intro edita (o abre la grabación), Ctrl+C copia y Ctrl+F lleva a la búsqueda. El doble clic en una tarjeta ahora edita o abre, en vez de copiar.

### Cambiado
- Historial: las miniaturas se cargan en segundo plano, así que la ventana abre al instante aunque haya muchas capturas.

## [0.4.0] - 2026-11-10

### Añadido
- Menú de la bandeja: «Buscar actualizaciones» busca en el momento y siempre avisa del resultado (al día, hay una versión nueva o no se pudo buscar).
- Capturas agrupadas, para mandar varias pantallas de una vez (por ejemplo, un paso a paso al cliente): con el **modo grupo** terminar una captura con Enter la suma al grupo en vez de copiarla; Ctrl+C sigue copiando. El modo se enciende solo al añadir la primera captura (menú ··· del editor o Ctrl+D), se puede encender a mano en la bandeja o en la ventana «Capturas agrupadas…», y se apaga solo al vaciar el grupo. Mientras está encendido, la captura muestra arriba «Modo grupo · N en el grupo · Enter añade · Ctrl+C copia». El atajo **Ctrl+Alt+C** (se cambia en Configuración) copia todo el grupo y lo vacía. Se copia como archivos (Captura 1.png, Captura 2.png…), que al pegar se adjuntan todos de una vez en WhatsApp, Teams, Outlook o Gmail, o como una sola imagen con las capturas una debajo de otra, para programas que solo aceptan una. Cada captura lleva su número en la esquina (se puede desactivar). La ventana del grupo deja reordenar, quitar y arrastrar las capturas, una a una o todas, a un chat o un correo. El grupo se guarda en disco y sigue ahí al reabrir Zarpazo; el modo grupo, en cambio, empieza apagado cada vez.
- Atajos de letra para cada herramienta (R rectángulo, E elipse, L línea, A flecha, P lápiz, H resaltador, T texto, N pasos, B pixelar, M regla, I cuentagotas, V mover, S ajustar zona), que también salen en el tooltip de cada botón y en los menús de grupo.
- Historial: botón «Conservar» para que una captura o grabación no se borre sola, ni por el límite de cantidad ni por antigüedad (queda con una marca ★). Eliminarla a mano pide confirmación. Si editas de nuevo una captura conservada, la nueva también lo queda.
- Historial: «Borrar lo que tenga más de» (Nunca, 7, 30 o 90 días) en Configuración > General. Se aplica al abrir Zarpazo y al guardar una captura o grabación nueva; por defecto no borra nada.
- Reeditar un texto: doble clic sobre él con la herramienta Mover, o un clic con la herramienta Texto. Se abre el cuadro con lo escrito (con su color y tamaño), Enter confirma y Esc deja el texto como estaba. Vaciarlo lo borra. Deshacer y rehacer también cubren el cambio.
- Estirar anotaciones: con la herramienta Mover, la anotación elegida muestra 8 asas para cambiar su tamaño (rectángulo, elipse, línea, flecha, lápiz, resaltador y pixelado). Deshacer y rehacer también lo cubren. El texto y los números siguen el tamaño del grosor.
- Lupa al elegir el área: junto al cursor se ve el zoom de los píxeles con su posición en pantalla (X, Y) y el color debajo. Con las flechas del teclado el cursor se mueve de a 1 píxel para colocar el borde con precisión.
- Esc cancela por pasos: primero suelta la anotación elegida, luego vuelve a «ajustar zona» y solo después cierra sin guardar. El botón de cerrar dice «Cancelar y cerrar sin guardar (Esc)».
- Historial: cuadro «Buscar texto» para encontrar una captura por lo que dice. Al abrir el Historial se lee con el OCR de Windows el texto de las capturas que aún no lo tenían (una a una, en segundo plano) y se guarda junto a cada captura; la búsqueda ignora mayúsculas y acentos. Las grabaciones no entran en la búsqueda.

## [0.3.0] - 2026-11-10

### Añadido
- Tema claro y oscuro: en Configuración > General se elige entre «Igual que Windows» (cambia solo cuando cambias Windows), «Claro» y «Oscuro». Afecta a Configuración, Historial, Recortar video, la barra del título y el menú del icono de la bandeja.

- Actualización automática: una vez al día Zarpazo pregunta a GitHub si hay una versión nueva y avisa desde el icono de la bandeja (una sola vez por versión). En Configuración > Acerca de se puede buscar ahora, ver las novedades y pulsar «Descargar e instalar»: descarga el instalador solo del repositorio oficial, comprueba su tamaño y su SHA-256 contra lo publicado, lo ejecuta en silencio y vuelve a abrir Zarpazo. Se puede apagar con «Buscar actualizaciones automáticamente». Solo se actualiza sola la copia instalada con el instalador; un .exe suelto abre la página de descarga.

- Aviso cuando Windows tiene Impr Pant asignada al Recortes (opción «Usar el botón Impr Pant para abrir la captura de pantalla»): puede abrirlo en vez de Zarpazo. Sale un globo al iniciar y una nota en Configuración con un botón que abre los ajustes de teclado de Windows.

### Cambiado
- Aspecto renovado de las ventanas de Configuración, Historial y Recortar video: botones, listas, casillas, pestañas y barras de desplazamiento planos y redondeados, con el color de la marca como acento.

## [0.2.1] - 2026-11-07

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

[Sin publicar]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.5.1...HEAD
[0.5.1]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/emmanuelLugo/Zarpazo/releases/tag/v0.1.0
