# Convertir documentos y correos

Caret convierte documentos en Markdown: un texto limpio que conserva títulos, listas, tablas, vínculos y notas al pie, y deja fuera las fuentes y el diseño. El resultado suele ocupar una pequeña parte del original, es fácil de leer para las personas y los asistentes de IA lo procesan con muchos menos tokens. Todo se convierte en tu PC: no se sube nada.

## Documentos

Abre **Convertir a Markdown** en la barra lateral y arrastra archivos o una carpeta completa, o usa **Elegir archivos...**. Se admiten Word, Excel, PowerPoint, PDF, CSV y XML. Cada resultado muestra su tamaño antes y después y una estimación de sus tokens de IA. **Copiar todo para la IA** pone todo en el portapapeles como un solo texto.

También puedes hacer clic derecho en un archivo, varios archivos o una carpeta en el *Explorador de archivos* y elegir **Convertir a Markdown** (no disponible cuando tu organización lo desactiva).

Los archivos Markdown se guardan junto a los originales, o en una carpeta que elijas. Un archivo que ya existe nunca se sobrescribe.

## Correos de Outlook

Abre **Correos de Outlook** y elige correos guardados desde Outlook (`.msg`) o desde otros programas de correo (`.eml`). Caret convierte toda la conversación en un solo archivo: cada respuesta como un mensaje aparte, del más antiguo al más reciente, sin firmas, avisos legales ni banners de "remitente externo", y con los archivos adjuntos convertidos en su lugar.

**Ocultar datos personales** (activado de forma predeterminada) reemplaza nombres, direcciones de correo, números de teléfono, números de cuenta bancaria y números de identificación por marcadores como `[PERSON-1]`, el mismo marcador para la misma persona, para que la conversación siga siendo legible cuando la pegues en un asistente de IA. Funciona con reglas fijas y dígitos de control, no con IA. Las personas se encuentran por los remitentes y destinatarios del correo y por los saludos y despedidas, así que un nombre que solo aparece en medio de una oración no se detecta. Revisa el resultado antes de compartirlo.

Se entienden correos en inglés, francés, español, polaco y portugués, y las reglas son archivos simples que una empresa puede ampliar.

## Archivos XML

Los archivos XML que producen los programas y las autoridades tributarias (facturas electrónicas, declaraciones, exportaciones, configuraciones) también se convierten, para que una persona pueda leerlos. Donde Caret conoce el formato (la factura electrónica colombiana de la DIAN, el CFDI mexicano del SAT, la NF-e brasileña y la factura peruana de la SUNAT) el archivo empieza con un resumen corto; después, o para cualquier otro archivo XML, vienen todos los campos del archivo, en orden. Las partes que tienen un título propio (un `id` y un `name`) se vuelven títulos a los que puedes saltar, las listas de texto se vuelven listas numeradas, los registros repetidos se vuelven tablas, y un archivo grande empieza con un resumen de lo que contiene. Las firmas digitales se nombran, no se imprimen.

Caret no verifica ni juzga un archivo XML: muestra lo que dice el archivo. Un archivo mal formado dice dónde se rompe. El XML se lee solo en tu equipo, y un archivo que viene de fuera no puede hacer que Caret lea otros archivos ni se conecte a internet.

Para leer un archivo XML sin guardar nada, usa **Importar documento como Markdown...** en el menú Archivo: abre la versión legible como una nota nueva.

## Suelta archivos en cualquier parte

No hace falta abrir antes la página de conversión: arrastra archivos de Word, PDF, Excel, PowerPoint o correo (o una carpeta entera) sobre la ventana de Caret, en cualquier parte, y suéltalos cuando aparezca **Convertir a Markdown**. Una bandeja pequeña, abajo a la derecha, los enumera, cada uno con su barra de progreso y, al pasar el puntero o al llegar con el teclado, **Abrir**, **Mostrar en el Explorador de archivos** (el botón de carpeta) y **Comparar**. Un archivo que falla dice por qué, con **Reintentar**; **Borrar la lista** vacía la bandeja y Esc la cierra.

**Comparar** muestra el original como texto sin formato a la izquierda y el Markdown a la derecha, y los desplaza juntos. Encima están las opciones que se usaron: si se extrajeron las imágenes y, en un correo, si se ocultaron los datos personales y cuántos nombres, direcciones, números e identificaciones se encontraron (en el Markdown aparecen en negrita y subrayados). Esc lo cierra. Úsalo para ver qué conservó, descartó u ocultó la conversión antes de compartir el resultado.

## Cuando algo no se convierte

Un archivo protegido con contraseña, o dañado, aparece en la lista con el motivo. Un PDF escaneado sin texto no se puede leer: Caret no hace reconocimiento de texto.