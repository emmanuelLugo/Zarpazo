# Cambios

Todos los cambios importantes de Zarpazo se anotan aquí. El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y las versiones siguen [SemVer](https://semver.org/lang/es/).

El flujo de publicación lee la sección de la versión que se etiqueta (`## [0.1.0]`) y la usa como notas de la release.

## [Sin publicar]

## [0.14.0] - 2026-11-26

### Añadido
- Aviso breve en la esquina al copiar o guardar una captura, sin sonido. Se apaga en Configuración > Captura. Reemplaza el globo de Windows del guardado automático.
- Captura: con la zona ya elegida, las flechas mueven 1 píxel el dibujo seleccionado o la zona; con Mayús, 10.
- Captura: con la herramienta Mover, al elegir un color se aplica al dibujo seleccionado (Ctrl+Z lo deshace).

### Cambiado
- Captura: al elegir otra zona (arrastrando fuera o con F) lo dibujado ya no se borra; se ve donde la zona nueva lo cubre, y Ctrl+Z sigue funcionando.

## [0.13.0] - 2026-11-26

### Añadido
- Editor de video, panel Cursor: color del efecto de clic (azul, rojo, amarillo, verde o blanco), «Ocultar si no se mueve» y tres formas de movimiento listas (Natural, Suave y Cinemático).
- El clic derecho se distingue en el video con un segundo anillo.
- El cursor dibujado cambia como en Windows: mano sobre los enlaces, barra sobre el texto y flechas al redimensionar o mover. Solo en videos grabados desde esta versión; con el estilo punto sigue siendo un punto.
- «Halo alrededor del cursor»: un círculo del color elegido acompaña al puntero.
- Al arrastrar (botón mantenido), un anillo sigue al puntero hasta soltarlo.
- Panel Zoom: «Desenfoque al moverse» (encendido de fábrica). La cámara y el cursor se difuminan cuando van rápido, en el video guardado.
- Panel Zoom: «Seguir al cursor» (encendido de fábrica). Con zoom, la cámara acompaña al puntero para que no salga de la vista.

### Corregido
- Editor de video: al maximizar o restaurar la ventana, la línea de tiempo vuelve a ocupar todo el ancho.
- Panel de grabación: la lista de micrófonos y altavoces muestra sus nombres.
- Zoom automático: con clics rápidos de un extremo a otro ya no hace zoom en cada uno (la cámara iba y venía); se ve la pantalla completa. Y ningún zoom empieza después de su clic.
- Al pasar de un zoom a otro hacia un lado alejado, la cámara se desliza de forma pareja, sin tirones.

### Cambiado
- Editor de video renovado, con un aspecto más limpio: los paneles Marco, Cursor, Zoom, Recorte, Capas y Exportar y el menú Presets usan grises, barras que se arrastran y botones planos; las opciones menos usadas quedan detrás de un botón.
- Línea de tiempo: las miniaturas se ven enteras con una etiqueta para arrastrar cada parte, y la línea roja es continua. «Mover antes» y «Mover después» pasaron al clic derecho (Alt+← y Alt+→ siguen igual), el tiempo está junto a reproducir y los avisos salen al lado de los botones.
- Panel de grabación (el círculo rojo de la barra) renovado: muestra solo lo que corresponde a video o a GIF, y los dispositivos de sonido solo cuando están encendidos.
- Historial: buscador y filtros planos, tarjetas sin caja, la elegida con borde claro, eliminar y «más» como iconos, y las capturas conservadas marcadas con una estrella.
- Al guardar un video con «Guardar…» ya no se crea además la copia del guardado automático: queda solo donde lo elegiste. Al copiarlo se sigue guardando solo.
- Zoom «Suave»: la cámara acerca y aleja de forma más pareja, sin el tirón del comienzo. El zoom automático empieza un poco antes de cada clic.
- Panel Cursor: «Tamaño» pasó a «Estilo», y el balanceo se desactiva con el estilo punto, donde no se nota.

## [0.12.0] - 2026-11-25

### Cambiado
- Configuración renovada: cada grupo de ajustes va en una tarjeta, con la explicación junto a su control, interruptores en lugar de casillas y un menú lateral con iconos.
- Los menús (el de la bandeja y los de clic derecho) tienen un aspecto propio: resalte redondeado con una barrita roja, en tema claro y oscuro.
- Barras del editor: la herramienta en uso se marca en rojo, los menús desplegables llevan borde y sombra, los avisos al pasar el mouse son oscuros y muestran la tecla aparte, y el tamaño al copiar y guardar es un solo control.
- La tecla de la herramienta en uso la suelta (por ejemplo, T y otra vez T), y vuelves a ajustar la zona.
- Historial renovado: buscador con lupa, filtro a la vista, tarjetas con la fecha y las medidas, y acciones rápidas (copiar, editar, conservar) al pasar el mouse. «Copiar» es el botón principal; «Abrir», «Conservar» y «Abrir carpeta» están en «Más».
- Editor de video: cada botón de la izquierda lleva su nombre, los ajustes con barra muestran nombre, barra y valor sin encimarse, los botones de edición van agrupados y el panel Marco está más ordenado.

## [0.11.0] - 2026-11-23

### Añadido
- Captura con scroll (Más > Captura con scroll, o Ctrl+L): eliges una zona y Zarpazo la va desplazando con la rueda del mouse y une las imágenes en una sola, para capturar una página o una lista más larga que la pantalla. Termina sola al llegar al final, con Esc o con el botón «Terminar» de la barra de estado, y la imagen terminada se abre para verla.
- Editor de capturas, menú Más: tamaño al copiar y guardar (100 %, 75 %, 50 % o 25 %), para mandar una captura más liviana.
- Ajustes: «Copiar información para reportar». Si algo falla, copia la versión, tu Windows, tus pantallas y los últimos errores para pegarlos en un reporte. No incluye tus capturas.

### Cambiado
- Los videos con marco se guardan con mucha más nitidez. Antes, para dejar lugar al marco, la grabación se achicaba (con tamaño «Alta», al 77 %) y el texto salía borroso. Ahora el video crece para que la grabación quede a su tamaño real, con el marco alrededor (hasta 4K). Por eso los videos con marco salen más grandes en píxeles y pesan algo más.
- Al guardar un video más chico (Alta, Media o Baja) o al grabar una pantalla más grande que Full HD, las letras finas ya no se pierden ni salen dentadas: cada punto de la imagen chica promedia todos los de la grande que cubre.
- Los videos se guardan con el perfil H.264 High, que deja más limpios los bordes del texto con el mismo peso.
- Guardar un video es más rápido: la tarjeta de video decodifica la grabación (en una prueba de 20 s a 1080p con marco, de 14,5 s a 11,7 s).

### Corregido
- En los videos grabados sin sonido, el cursor dibujado iba atrasado respecto de la pantalla (entre 0,1 y 0,7 s): se iluminaba un botón o se marcaba un clic antes de que el cursor llegara. Ahora van juntos. Las grabaciones hechas antes de esta versión conservan el desfase.

## [0.10.0] - 2026-11-20

### Añadido
- Girar capturas: una captura fijada sobre la pantalla gira un cuarto de vuelta a la derecha (Ctrl+R) o a la izquierda (Ctrl+Shift+R), también desde su menú. En el editor de capturas, el botón Girar (Ctrl+R) la fija ya girada a la derecha.
- Embellecer tiene barra de ventana, como el marco de los videos: ninguna, Mac (tres botones redondos) o Windows (minimizar, maximizar y cerrar), en tono oscuro o claro. La captura queda como una ventana sobre el fondo.

### Cambiado
- Las partes del video que aceleras o ralentizas ya no quedan mudas: su sonido se acelera o se ralentiza con ellas sin cambiar el tono (la voz no suena de ardilla ni de monstruo). Funciona de 0,5× a 4× (pasado 2× la voz se entiende cada vez menos, es normal). Silenciar una parte sigue funcionando en cualquier velocidad.

## [0.9.1] - 2026-11-16

### Corregido
- Dos monitores con distinta escala (por ejemplo, una notebook al 150 % y un monitor externo al 100 %): al grabar en el monitor que no es el principal, el marco rojo que señala la zona salía más chico que la zona, y el número de la cuenta regresiva, descentrado. Ahora miden lo que deben en cualquier monitor.
- El editor de video se abría más grande que el monitor secundario, con la barra de título (cerrar, minimizar) fuera de la pantalla. Ahora toma el tamaño y la escala del monitor donde se abre.

## [0.9.0] - 2026-11-16

### Añadido
- Capas sobre el video en el editor (icono nuevo a la izquierda, «Desenfoque, texto y flechas»): **Desenfocar** oculta una zona (una contraseña, un correo, datos de un cliente) con un desenfoque fuerte que no se puede leer; **Texto** escribe unas palabras, con su color y tamaño; **Flecha** señala algo. Se eligen con un clic o arrastrando sobre el video, se mueven y se estiran desde la misma imagen, y cada una aparece solo durante su bloque en una fila nueva de la línea de tiempo (3 segundos al ponerla; se arrastra el bloque o sus bordes). Siguen al zoom, al marco y al recorte, y un corte las deja en su momento. Se pueden deshacer y rehacer.
- Proyectos de video: el histórico guarda con cada grabación el video limpio y todo lo que hiciste en el editor (cortes, velocidad, zooms, capas, recorte y estilo). «Editar» sobre un video del histórico lo vuelve a abrir como lo dejaste, para retocar un zoom o un texto sin empezar de cero; al copiar o guardar de nuevo, el video nuevo reemplaza al anterior en el histórico. Si cierras el editor sin copiar ni guardar, igual se guardan tus cambios. Los videos de antes de esto siguen en el histórico para copiar y guardar, pero no se pueden editar. Solo funciona con el historial activado (Ajustes > Guardado), y la grabación limpia pesa más que el video final, así que cada video ocupa más espacio en el historial.
- Formato del video (16:9, 9:16 o 1:1) en la pestaña «Exportar» del editor: la grabación queda en el centro de un lienzo de esa forma, sobre el fondo del Marco (aunque no tengas el marco encendido, el fondo se ve; con el marco encendido se respeta su espacio, esquinas y sombra). Sirve para sacar un video vertical para historias, reels o shorts, o uno cuadrado para una publicación, desde la misma grabación. Los zooms, el cursor, los desenfoques, los textos y las flechas siguen a la grabación. Es de cada video, se puede deshacer y se guarda en el proyecto. Un 9:16 sale de 1080 × 1920 desde una grabación de 1920 × 1080.
- Marco del video, mucho más completo: fondo propio (un color, un degradado con ángulo, una imagen tuya o uno al azar) y looks de un clic; una línea alrededor de la grabación; una barra de ventana arriba, estilo Mac o Windows, clara u oscura; la sombra con su suavidad, distancia y color; y espacio propio en cada lado. Se ve igual en la vista previa y en el video guardado. El panel se pliega en secciones, cada una con su «Restablecer».
- Editor de video, pestaña «Exportar»: elige el tamaño del video guardado (Original, Alta 90 %, Media 75 % o Baja 50 %), su calidad (Ligera, Normal o Alta) y los cuadros por segundo (como la grabación, 24, 30 o 60). El panel dice a cuántos píxeles saldrá, también con un recorte. La elección se aplica al guardar o copiar, se recuerda para el siguiente video y no cuenta como un cambio que deshacer. No se inventan cuadros: con 60 sobre una grabación de 30, el video sigue a 30.

### Cambiado
- Editor de video: copiar o guardar ya no congela la ventana. La copia de la grabación y del video al histórico se hace en segundo plano, y cerrar el editor tampoco la espera. La app no se considera libre (ni se reinicia para actualizar) mientras la copia sigue.
- Guardar un video es bastante más rápido: donde la grabación no llega, el fondo se copia tal cual; el marco se compone en la mitad de tiempo, la conversión de color es unas 7 veces más rápida y el siguiente cuadro se lee mientras se dibuja el actual. En una prueba de 20 s a 1080p con marco, la versión de uso diario (Release) pasó de unos 21 s a 15-18 s. El resultado es idéntico píxel a píxel.
- Zooms en el editor de video: el panel de Zoom tiene ahora «Conectar zooms seguidos». Con él apagado, la cámara vuelve a pantalla completa entre dos zooms cercanos en vez de deslizarse de uno al otro. Forma parte del estilo: se recuerda para el siguiente video, entra en los presets y se deshace con Ctrl+Z.
- Ajustes > Video: «Poner zooms automáticamente al abrir una grabación». Apagado, el editor abre el video sin zooms (el botón «Zoom automático» sigue disponible).
- Ajustes > Actualizaciones: si abres una copia que no es la instalada (por ejemplo, una de desarrollo) y hay una versión nueva, el aviso ahora dice que la instalada sí se actualiza sola desde ahí, en vez de mandarte a la página de descarga.

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

[Sin publicar]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.14.0...HEAD
[0.14.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.13.0...v0.14.0
[0.13.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.12.0...v0.13.0
[0.12.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.11.0...v0.12.0
[0.11.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.10.0...v0.11.0
[0.10.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.9.1...v0.10.0
[0.9.1]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/emmanuelLugo/Zarpazo/compare/v0.8.0...v0.9.0
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
