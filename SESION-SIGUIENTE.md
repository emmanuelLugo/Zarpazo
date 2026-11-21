# Zarpazo: arranque de la siguiente sesión

Pega esto al empezar, o pide: «lee SESION-SIGUIENTE.md y ROADMAP.md y empieza».

## Dónde estamos
- Versión 0.10.0 publicada. Rama `main`. Hechos desde entonces, sin publicar versión nueva:
  - Log de errores rotativo y botón «Copiar información para reportar» (`e067e3b`).
  - Tamaño de salida 100/75/50/25% al copiar y guardar, en Más (`21f8d3a`).
  - Captura con scroll: Más > Captura con scroll o Ctrl+L (`23c765a`, `27fdf5e`). **Falta probarla a mano** en una página larga y un documento; si falla en una app, ajustar `Capture/ScrollStitcher.cs` y `Capture/ScrollCapture.cs`.
- Descartado por el usuario: idiomas es/en (punto 3) y ocultar datos sensibles por OCR (punto 5). No volver a proponerlos.
- El usuario no quiere pulido superficial (avisos, bienvenida, hoja de atajos, accesibilidad). Quiere **funciones nuevas con sustancia u optimizaciones** que hagan funcionar mejor la app.
- El v0.1 del plan original (bandeja, atajo global, overlay, CopyFromScreen, PNG, PerMonitorV2) ya está completo. DXGI en captura fija no hace falta. Riesgo abierto: multi-monitor con escalas distintas, nunca probado (un solo monitor al 100%).

## Qué hacer (el usuario elige; recomendado primero)
1. **Guardar video más rápido con decodificación por hardware (DXVA)** en el guardado del editor de video. El límite medido es el decodificador H.264 por software (~64 ms de CPU por cuadro 1080p; 20 s de video queman ~86 s de CPU). Ver memoria `zarpazo-save-speed` (usar Release para medir, comparar viejo y nuevo alternados). Variante menor: no redibujar cuadros cuando la cámara está quieta y el cuadro de origen no cambió.
2. **Buscar en el historial por el texto de las capturas** (reusa el OCR que ya existe).
3. Quitar silencios y limpiar el audio en el editor de video.
4. Subir y copiar enlace (hay que elegir servicio: servidor propio, S3/R2 o Imgur).
5. Globos (callouts), sellos y emojis en el editor (marcado Avanzado: confirmar antes).

## Reglas (de CLAUDE.md)
- Bloques pequeños, un commit por bloque, indicar cómo ejecutar y qué probar.
- Confirmar antes de cualquier función marcada «Después» o «Avanzado» en PLAN.md.
- Código y nombres en inglés; textos de interfaz en español. Una clase por archivo.
- Compilar con `C:\Program Files\dotnet\dotnet.exe` (64 bits). Si la app está abierta se bloquea el exe: pedir que la cierre, no matarla.
- Los tests que dejan cambios pendientes cuelgan con un MessageBox.
- En Bash, los heredocs con apóstrofes fallan: escribir el script con Write y ejecutarlo.
- Preguntar antes de simular ratón o teclado sobre la pantalla del usuario.
