# Cambios

Todos los cambios importantes de Zarpazo se anotan aquí. El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y las versiones siguen [SemVer](https://semver.org/lang/es/).

El flujo de publicación lee la sección de la versión que se etiqueta (`## [0.1.0]`) y la usa como notas de la release.

## [Sin publicar]

## [0.8.0] - 2026-11-13

### Añadido
- Velocidad por parte en el editor de video: el botón «1×» junto a Silenciar (o «Velocidad» en el menú del clic derecho sobre la línea de tiempo) acelera la parte elegida hasta 4× o la ralentiza hasta 0,5×. Sirve para pasar rápido lo aburrido (una carga, un formulario largo) sin cortarlo. La parte se ve más angosta en la línea de tiempo y su título dice la velocidad; la vista previa la reproduce así, y el zoom y el puntero la acompañan. A otra velocidad la parte va sin sonido.

### Cambiado
- El micrófono se nivela solo al grabar: antes entraba unos 20 dB más bajo que el sonido de Windows y los videos se oían muy despacio. Ahora la voz sube (o baja) a un volumen normal, el ruido de la sala entre frases no se levanta y, si el micrófono y el sonido de Windows suenan fuerte a la vez, el volumen baja un instante en lugar de recortarse y crujir. Además se quita el retumbe grave y se realzan un poco los agudos para que la voz se entienda mejor. El sonido de Windows no cambia, y la rueda de volumen de la barra sigue funcionando encima.

## [0.7.0] - 2026-11-13

### Añadido
- Editor de video renovado, al estilo de Recordly: barra superior con deshacer, rehacer, Copiar y Guardar…; a la izquierda, iconos que abren un panel a la vez (Marco, Cursor, Zoom y Recorte); el video grande en el centro y la línea de tiempo a todo lo ancho. Los ajustes son filas que se arrastran desde cualquier punto, con su nombre y valor escritos encima, y cada sección tiene «Restablecer». Diez fondos nuevos, también disponibles en Embellecer.
- Al detener la grabación de un video se abre directamente el editor, desde donde se copia o se guarda. La grabación igual queda en el historial aunque cierres el editor sin guardar.
- Recortar el video: el panel «Recorte» muestra la grabación entera con un rectángulo que se arrastra por sus lados, esquinas o centro (o se ajusta con las filas Superior, Inferior, Izquierda y Derecha). El zoom y el puntero siguen funcionando dentro del recorte, y se puede deshacer.
- Presets de video: el botón «Presets» de la barra superior guarda el aspecto actual (marco, puntero y estilo de zoom) con un nombre y lo aplica después con un clic. El panel Zoom suma el estilo: «Suave» (el de siempre) o «Enfocado» (más rápido).
- Puntero dibujado: los videos se graban sin el puntero y se dibuja al guardar, así que en el panel «Cursor» se elige su tamaño, cuánto se suaviza su recorrido y el efecto de los clics, con vista previa en vivo.
- Zoom automático en los videos, al estilo de Screen Studio y Recordly. Al grabar un video, Zarpazo guarda dónde estuvo el puntero y dónde hiciste clic; al abrir el editor («Editar…»), cada grupo de clics ya tiene su zoom: la imagen se acerca suavemente (1,5× por defecto, con un movimiento que arranca y se detiene sin tirones) a lo que tocaste, se queda ahí mientras sigues haciendo clic cerca y vuelve a alejarse. Si dos zooms quedan muy juntos, la cámara pasa de uno a otro sin alejarse.
- Fila de zooms en el editor de video, debajo de las partes: arrastra un zoom para moverlo o sus bordes para alargarlo; doble clic en la fila o `Z` añade uno donde está la línea roja (mirando adonde estaba el puntero); `Supr` o el clic derecho lo quitan. Al elegir uno, el video muestra la imagen entera con la zona que se verá marcada: haz clic o arrastra para elegir adónde mira, y elige cuánto se acerca (de 1,25× a 3×). «Zoom automático» los vuelve a poner a partir de los clics. Deshacer y rehacer también cubren los zooms, y un zoom sigue a su momento aunque cortes o muevas partes.
- Tamaño del puntero en las grabaciones (panel Grabar > Efectos): Normal, Grande (1,5×) o Muy grande (2×), como en Recordly. Los punteros de Windows (flecha, mano, texto…) se cargan en su versión grande, así que se ven nítidos; el halo y los anillos de los clics crecen con él. Vale para GIF y video, y se recuerda.
- Marco para los videos, como en Recordly: en el editor, el panel «Marco» pone la grabación sobre un fondo (los mismos degradados de Embellecer), con margen, esquinas redondeadas y sombra. Con zoom, el video crece y el fondo sale de la imagen. La vista previa lo muestra tal como se guardará y la elección se recuerda.
- Videos a 60 cuadros por segundo: Configuración > Grabación > Cuadros por segundo > «60 (muy fluido)».
- La vista previa del editor muestra el zoom al reproducir y al moverte por el video, y «Guardar video» lo aplica al video nuevo, con el mismo tamaño, calidad y sonido (un video de 10 s en 1080p tarda unos 8 s en guardarse). El video con zoom sale con un cuadro exacto cada 1/30 s aunque la grabación tenga cuadros que llegaron tarde, así que la cámara se mueve pareja, y con más calidad (al acercar cambian todos los píxeles y a la calidad de una grabación de pantalla se veía en bloques).

### Cambiado
- La barra de grabación es ahora una píldora flotante con iconos de línea y un único botón rojo para detener, y no aparece en los videos de pantalla completa.
- Al elegir el área, el puntero en cruz es blanco con borde negro y se ve sobre cualquier fondo. El de Windows invertía los colores y desaparecía sobre pantallas blancas oscurecidas.
- Con una selección grande, las barras de herramientas quedan en el mismo monitor que la selección y separadas del borde de la pantalla.
- Grabaciones más fluidas: la pantalla se copia con la tarjeta de video (DXGI), unos 8 ms por cuadro en 1080p en lugar de 33, así que el video ya no tiene cuadros que llegan tarde ni se ve a tirones. Si la zona ocupa dos monitores, o Windows no deja copiar la pantalla en ese momento, se usa el método de antes. Vale para GIF y video.
- El control que acerca la línea de tiempo del editor de video se llama ahora «Acercar línea», para no confundirlo con el zoom del video.
- Al cerrar el editor de video solo pregunta si hay cambios tuyos sin guardar; los zooms automáticos por sí solos no cuentan.

### Corregido
- Si Windows no deja grabar una fuente de sonido (por ejemplo, auriculares Bluetooth en modo manos libres), el video se graba igual con las demás fuentes y un aviso explica por qué; antes fallaba toda la grabación y quedaba un archivo vacío.
- Los controles deslizantes del editor de video siguen al mouse sin tirones.
- Al grabar una zona con un número impar de píxeles de ancho o de alto (por ejemplo 1919×1079), el video salía a unos 10 cuadros por segundo: cada cuadro se reescalaba entero para quitarle un píxel. Ahora ese píxel simplemente se deja fuera. Las zonas más grandes que 1920 (pantallas 2K y 4K) también se achican mucho más rápido.

## [0.6.0] - 2026-11-12

### Añadido
- Actualización automática más cómoda. La versión nueva se descarga sola en segundo plano y se comprueba que sea la publicada; cuando está lista, una tarjeta en la esquina de la pantalla dice «Zarpazo X está lista» con tres opciones: **Reiniciar ahora** (un clic: Zarpazo se cierra, se instala sin mostrar ninguna ventana y vuelve a abrirse con la versión nueva), **Al cerrar** (se instala al salir de Zarpazo desde la bandeja, o la próxima vez que se abra) y **Novedades**. El menú de la bandeja muestra «Reiniciar para actualizar» mientras haya una versión lista, y la descarga se recuerda aunque se cierre Zarpazo. En Configuración > General hay una casilla para apagar la descarga en segundo plano.
- Si se pide reiniciar mientras se elige un área, se edita una captura o se graba, Zarpazo espera a que termines y entonces se reinicia solo. La tarjeta de una versión nueva tampoco aparece mientras haces algo.
- Tras actualizar, la primera vez que se abre Zarpazo sale una tarjeta con la versión nueva y un botón para ver las novedades.
- Editor de video, en lugar del recorte que solo quitaba el principio y el final. Al terminar una grabación, el botón «Editar…» abre una línea de tiempo como la de un editor de video: se divide la grabación en partes (`S`), se eliminan las que sobran aunque estén en el medio (`Supr`; las siguientes se juntan), se cambia su orden arrastrándolas por el título o con Alt+←/→, se repiten (`Ctrl+D`), se les quita el sonido (`M`) y se ajusta dónde empieza y termina cada una arrastrando sus bordes (se puede volver a lo que se había quitado). La línea de tiempo muestra miniaturas del video, tiene zoom (Ctrl + rueda), deshacer y rehacer ilimitados (`Ctrl+Z`, `Ctrl+Y`), avance cuadro a cuadro con las flechas y reproducción de lo que quedará, saltando las partes quitadas. Se guarda como un video nuevo junto al original (`_editado.mp4`) y, si se cierra con cambios sin guardar, pregunta antes de descartarlos.
- Tamaño con la rueda del mouse, como en Lightshot: con el pincel, la flecha, la línea, el rectángulo, la elipse, el texto o los números, girar la rueda sobre la captura cambia el grosor (o el tamaño de la letra y de los números). También vale mientras dibujas, mientras escribes un texto y, con la herramienta Mover, sobre la anotación elegida (se puede deshacer, y varios giros seguidos cuentan como un solo paso). Un círculo translúcido con doble borde (claro y oscuro, para que se vea sobre cualquier fondo) sigue al puntero y muestra el tamaño que tendrá lo siguiente que dibujes; al girar la rueda aparece además el valor en píxeles, que se apaga solo. Las primeras veces, al elegir una herramienta de dibujo, un aviso recuerda que la rueda cambia el tamaño.
- Configuración rediseñada: un menú lateral con seis secciones (General, Atajos, Captura, Grabación, Guardado y Acerca de) en vez de pestañas, con la ventana más ancha. Los tres atajos quedan juntos en «Atajos», la calidad del GIF y del video pasa a «Grabación», y todo lo de guardar (automático, a mano, nombre e historial) a «Guardado». Cada sección tiene un botón «Restablecer esta sección» que vuelve a los valores de fábrica solo ahí, sin guardar hasta pulsar Aceptar. Las actualizaciones pasan a «General».
- Configuración > Captura > «Elegir el área»: casilla para mostrar u ocultar la lupa (zoom con la posición y el color) que sigue al cursor al elegir el área. Viene activada; el cuentagotas de la barra de herramientas no cambia.

### Cambiado
- El botón de grosor con sus tres puntos (fino, medio y grueso) ya no está en la barra de herramientas: el tamaño se cambia con la rueda del mouse, en cualquier valor entre 1 y 30. El último tamaño se recuerda como antes.

### Corregido
- Al actualizar desde Zarpazo, el reinicio podía cortar un video mientras se estaba creando («Creando el MP4…»), porque solo se comprobaba si había una grabación en curso. Ahora espera a que termine.
- Al recortar un video grabado se perdía hasta casi un segundo del final aunque no se hubiera tocado el final: el reproductor de Windows redondeaba la duración hacia abajo (13,6 s se leía como 13 s). El editor nuevo usa la duración exacta.
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

[Sin publicar]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.8.0...HEAD
[0.8.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.5.1...v0.6.0
[0.5.1]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/emmanuelLugo/Zarpazo/releases/tag/v0.1.0
