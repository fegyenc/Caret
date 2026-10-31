# Novedades de Caret

Los cambios principales, de los más recientes a los más antiguos. Cada función tiene su propio tema en este menú; esta página solo te dice dónde mirar.

## Versión 2.10: escaneos, capturas de pantalla y una barra lateral más clara

- **Edición** > **Pegar texto de una imagen (OCR)** lee una captura de pantalla o una imagen del portapapeles (Win+Mayús+S hace una) con el OCR de Windows, en tu equipo, y pega su texto donde está el cursor.
- En la página **Documentos y escaneos**, activa **Leer también las imágenes de las carpetas (OCR)** y una carpeta que eliges o sueltas entrega al OCR sus imágenes (PNG, JPG, BMP, TIFF) además de sus documentos. Al principio está desactivado, y una carpeta con 30 imágenes o más pregunta antes de empezar. Las opciones de OCR ahora van primero en la página, y la línea bajo la zona para soltar nombra los escaneos y las imágenes.
- La barra lateral está en tres grupos: **Convertir** (**Documentos y escaneos**, que se llamaba *Convertir a Markdown*, **Correos de Outlook** y **Facturas en una sola tabla**), **Este documento** (**Revisión**, **Marcas de discurso** y, para un archivo XML, **Herramientas XML**) y **Biblioteca**. **Ocultar datos personales...** también está en el panel **Revisión**, y la tarjeta de abajo muestra un consejo.

## Versión 2.9: Brasil y Portugal, herramientas XML, escaneos

- Más documentos electrónicos con palabras claras: la NFS-e nacional de Brasil (factura de servicios), el CT-e (documento de transporte) y el MDF-e (manifiesto), con sus eventos, y el archivo de auditoría SAF-T (PT) de Portugal, con revisión de totales e impuestos. Una carpeta de facturas (*Facturas en una sola tabla*) puede mezclarlos: un CT-e sustituido por un CT-e de sustitución se lista, pero no se suma a los totales. Mira el tema *Convertir a Markdown*.
- Archivos XML: la estructura en la lista *Esquema* y en el mapa del documento; un trozo de XML con varios elementos seguidos se lee y se formatea como fragmento; **Edición** > **Pegar como XML** pone el portapapeles en la nota como bloque de código y lo abre como documento XML; un clic en un valor de la versión legible lo selecciona en el código.
- **Edición** > **Ocultar datos personales...** oculta direcciones de correo, teléfonos, IBAN, números de tarjeta, números de identificación y los nombres que pongas en la lista, en cualquier nota o archivo XML: en una copia o en la propia nota (un Deshacer la devuelve).
- PDF: convierte solo algunas páginas (el cuadro *Páginas del PDF* de la página Convertir); lee escaneos y fotos de páginas con el OCR de Windows, en tu equipo; y agrega marca de agua, encabezado, pie de página y números de página al exportar (**Archivo** > **Exportar** > **PDF...**).

## Versión 2.7: archivos XML, con palabras claras

- Lee un archivo XML como un documento. Ábrelo, suéltalo en la ventana o elígelo en la página Convertir. Caret conoce las facturas electrónicas de Colombia (DIAN), México (CFDI), Brasil (NF-e y NFC-e), Perú (SUNAT) y Chile (DTE) y otras facturas UBL, y explica sus códigos con palabras (en español, inglés o portugués). Cualquier otro XML se convierte en un esquema. Mira el tema *Convertir a Markdown*.
- Una carpeta de facturas como una sola tabla (página Convertir, *Facturas en una sola tabla*): Excel, CSV o Markdown, con las notas de crédito restadas y los duplicados contados una vez.
- Edita archivos XML. Se abren como código con la versión legible al lado. Una franja dice con palabras claras si el XML está bien formado y dónde no lo está; **Edición** > **Dar formato al XML** ordena solo los espacios en blanco; **Edición** > **Validar con un esquema...** compara el archivo con un XSD. El archivo se guarda como estaba escrito. Mira *Editar archivos XML* en el mismo tema.
- **Ir al archivo** (`Ctrl+K`) enumera los archivos XML, y Windows ofrece Caret en *Abrir con* para los archivos .xml, .xsd, .xsl y .xslt.
- Las imágenes viajan con la nota: **Archivo** > **Exportar** > **Markdown con imágenes (.zip)...**, reúne las imágenes junto a la nota, y **Párrafo** > **Gráfico desde la tabla...**

## Versión 2.6: la ventana viene a ti

- **Paleta de comandos** (`Ctrl+Mayús+P`, **Ver** > **Paleta de comandos**): escribe unas letras de lo que quieres hacer, un comando, un ajuste o una nota, y pulsa Entrar.
- **Panel del inspector** (`Ctrl+Mayús+I`, **Ver** > **Panel del inspector**): una columna junto a la nota con su esquema, sus marcas de revisión, el tiempo del discurso, todas las coincidencias de la búsqueda y el **Historial** de la nota, las versiones guardadas anteriores que puedes comparar y restaurar. Las copias de las notas guardadas se quedan en tu PC.
- **Mapa del documento** (**Ver** > **Mapa del documento**): una franja delgada en el borde derecho que muestra dónde estás en la nota y dónde están los títulos, los comentarios y los cambios.
- Una barra de estado que dice lo que importa: cuándo se guardó el archivo, palabras y tiempo de lectura, la selección, las marcas de revisión. Un segmento sin nada que decir no se muestra.
- Una barra pequeña sobre las palabras seleccionadas, con los formatos más usados y un botón que hace **Copiar como texto de WhatsApp**.
- Suelta archivos en cualquier parte de la ventana (Word, PDF, Excel, PowerPoint, CSV, correos o una carpeta) para convertirlos a Markdown. Una bandeja muestra los trabajos, con Abrir, Mostrar en la carpeta, Comparar y Reintentar.
- Tarjetas de apariencia (**Configuración** > **Apariencia**): mira el esquema de colores en una vista previa en vivo, encuentra lo que cambiaste y vuelve al esquema con un clic.
- Primero el teclado: pulsa `Alt` para ver qué tecla llega a cada botón, y `Ctrl+Mayús+/` para la lista de todos los atajos (**Métodos abreviados de teclado** en el menú Ayuda).
- Inicio como página de trabajo: los archivos con los que trabajabas, lo que puedes empezar y tus carpetas, en tres columnas.
- Correcciones: los colores de toda la ventana siguen de inmediato un cambio de tema, y las teclas de acceso desaparecen cuando la ventana se desplaza.

## Versión 2.5: envíalo como archivo de Word

**Archivo** > **Exportar** > **Documento de Word (.docx)** escribe un archivo de Word de verdad en tu PC, con títulos, listas, tablas, imágenes, notas al pie, tabla de contenido, cambios controlados y comentarios. No hace falta tener Word instalado. Mira el tema *Exportar a Word*.

## Versión 2.0: escríbelo, sigue los cambios, dilo

- Seguimiento de cambios mientras editas (**Revisión**), comentarios, aceptar o rechazar, y comparar un archivo con una versión anterior.
- **Marcas de discurso** y **teleprompter** para tus discursos, con reloj y ensayo.
- Corrector ortográfico, **Copiar como texto de WhatsApp**, un menú Ayuda, y portugués de Brasil, polaco y español para todos los países hispanohablantes.

## Versión 1.6: archivos desde el Explorador

Clic derecho Convertir a Markdown en el Explorador de archivos, una pestaña se puede soltar en otra ventana de Caret, un menú contextual en el editor y una conversión mucho mejor de archivos reales de PDF, Word, Excel, PowerPoint y correo.

## Versión 1.5

Una pestaña se puede arrastrar a su propia ventana, y la barra lateral se desplaza y se contrae. El enmascarado de datos personales en los correos también encuentra los nombres en los saludos y las despedidas.

## Versión 1.4: pestañas y un aspecto nuevo

Varios documentos en una ventana, una página de Configuración, esquemas de colores, diseños y mejor contraste.

## Versión 1.3: correos de Outlook

Los **Correos de Outlook** se convierten en Markdown limpio, un archivo para toda una conversación, con los datos personales ocultos si lo quieres.

## Versión 1.2: Convertir a Markdown

**Convertir a Markdown** lee archivos de Word, PDF, Excel y PowerPoint, se agregaron el francés y el español, y las letras con acento salen bien en archivos que no son UTF-8.

## Versión 1.0

El editor en sí: un editor de Markdown que muestra el texto como se verá, con tablas, vínculos e imágenes, favoritos y archivos recientes, y nada se sube a ninguna parte.
