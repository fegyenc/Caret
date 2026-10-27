# Contenedor de documentos electrónicos SETP990000001

> Archivo: co-factura-adjunta.xml
> UBL 2.1 · Factura Electrónica de Venta

Este archivo reúne un documento electrónico y la respuesta de la autoridad tributaria en un solo archivo XML.

- **Referencia:** 990000001
- **Fecha de emisión:** 2026-03-04 10:16:00-05:00
- **Emisor:** Comercializadora Ejemplo S.A.S.
- **Adquirente:** Cliente de Prueba Ltda.

## Factura electrónica de venta SETP990000001

- **Fecha de emisión:** 2026-03-04 10:15:00-05:00
- **Fecha de vencimiento:** 2026-04-03
- **Moneda:** COP
- **Código único (CUFE / CUDE):** 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef (CUFE-SHA384)
- **Ambiente:** Pruebas

**Notas**

- Factura de ejemplo con datos ficticios.

### Emisor

- **Nombre:** Comercializadora Ejemplo S.A.S.
- **Identificación:** 900000001-1 (NIT)
- **Dirección:** Calle 1 # 2-3, Bogotá, D.C., Bogotá, Colombia
- **Correo electrónico:** facturacion@ejemplo.test
- **Teléfono:** 6010000000
- **Responsabilidades fiscales:** O-13;O-15

### Adquirente

- **Nombre:** Cliente de Prueba Ltda.
- **Identificación:** 900000002-2 (NIT)
- **Dirección:** Carrera 4 # 5-6, Medellín, Antioquia, Colombia
- **Correo electrónico:** compras@cliente.test
- **Responsabilidades fiscales:** R-99-PN

### Pago

- **Forma de pago:** Crédito
- **Medio de pago (código):** ZZZ

### Detalle

| # | Descripción | Cantidad | Unidad | Precio unitario | Total línea |
| --- | --- | --- | --- | --- | --- |
| 1 | Resma de papel carta (caja x 10) | 10.000000 | EA | 50000.00 COP | 500000.00 COP |
| 2 | Silla de oficina \| modelo "Ejemplo" \*ergonómica\* | 2.000000 | EA | 250000.00 COP | 500000.00 COP |

### Impuestos

| Impuesto | Base | Tarifa | Valor |
| --- | --- | --- | --- |
| 01 · IVA | 1000000.00 COP | 19.00 % | 190000.00 COP |

### Totales

- **Total antes de impuestos:** 1000000.00 COP
- **Base imponible:** 1000000.00 COP
- **Total con impuestos:** 1190000.00 COP
- **Total a pagar:** 1190000.00 COP

## Respuesta de la DIAN

- **Referencia:** SETP990000001
- **Código único (CUFE / CUDE):** 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
- **Resultado:** 02
- **Fecha de emisión:** 2026-03-04 10:15:30-05:00

- **Proveedor:** Dirección de Impuestos y Aduanas Nacionales
- **Resultado:** 02: Documento validado por la DIAN (texto de ejemplo)

### Respuestas

| Código | Mensaje |
| --- | --- |
| FAD00 | Mensaje de ejemplo de una regla de validación |

## Todos los campos del archivo

El resumen de arriba muestra lo que hace falta para leer el documento; esta lista tiene todos los campos del archivo XML, en orden, para que no se oculte nada.

- **AttachedDocument**
  - **UBLExtensions**
    - **UBLExtension**
      - **ExtensionContent**
        - **Signature**: firma digital (no se imprime)
  - **UBLVersionID**: UBL 2.1
  - **CustomizationID**: Documentos adjuntos
  - **ProfileID**: Factura Electrónica de Venta
  - **ProfileExecutionID**: 2
  - **ID**: 990000001
  - **IssueDate**: 2026-03-04
  - **IssueTime**: 10:16:00-05:00
  - **DocumentType**: Contenedor de Factura Electrónica
  - **ParentDocumentID**: SETP990000001
  - **SenderParty**
    - **PartyTaxScheme**
      - **RegistrationName**: Comercializadora Ejemplo S.A.S.
      - **CompanyID** (schemeID=1, schemeName=31): 900000001
      - **TaxScheme**
        - **ID**: 01
        - **Name**: IVA
  - **ReceiverParty**
    - **PartyTaxScheme**
      - **RegistrationName**: Cliente de Prueba Ltda.
      - **CompanyID** (schemeID=2, schemeName=31): 900000002
      - **TaxScheme**
        - **ID**: 01
        - **Name**: IVA
  - **Attachment**
    - **ExternalReference**
      - **MimeCode**: text/xml
      - **EncodingCode**: UTF-8
      - **Description**: un documento XML guardado como texto
        - **Invoice**
          - **UBLExtensions**
            - **UBLExtension**
              - **ExtensionContent**
                - **Signature**: firma digital (no se imprime)
          - **UBLVersionID**: UBL 2.1
          - **CustomizationID**: 10
          - **ProfileID**: DIAN 2.1: Factura Electrónica de Venta
          - **ProfileExecutionID**: 2
          - **ID**: SETP990000001
          - **UUID** (schemeID=2, schemeName=CUFE-SHA384): 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
          - **IssueDate**: 2026-03-04
          - **IssueTime**: 10:15:00-05:00
          - **DueDate**: 2026-04-03
          - **InvoiceTypeCode**: 01
          - **Note**: Factura de ejemplo con datos ficticios.
          - **DocumentCurrencyCode**: COP
          - **LineCountNumeric**: 2
          - **AccountingSupplierParty**
            - **AdditionalAccountID**: 1
            - **Party**
              - **PartyName**
                - **Name**: Comercializadora Ejemplo
              - **PartyTaxScheme**
                - **RegistrationName**: Comercializadora Ejemplo S.A.S.
                - **CompanyID** (schemeAgencyID=195, schemeID=1, schemeName=31): 900000001
                - **TaxLevelCode** (listName=48): O-13;O-15
                - **RegistrationAddress**
                  - **ID**: 11001
                  - **CityName**: Bogotá, D.C.
                  - **CountrySubentity**: Bogotá
                  - **AddressLine**
                    - **Line**: Calle 1 # 2-3
                  - **Country**
                    - **IdentificationCode**: CO
                    - **Name** (languageID=es): Colombia
                - **TaxScheme**
                  - **ID**: 01
                  - **Name**: IVA
              - **Contact**
                - **Telephone**: 6010000000
                - **ElectronicMail**: facturacion@ejemplo.test
          - **AccountingCustomerParty**
            - **AdditionalAccountID**: 1
            - **Party**
              - **PartyTaxScheme**
                - **RegistrationName**: Cliente de Prueba Ltda.
                - **CompanyID** (schemeAgencyID=195, schemeID=2, schemeName=31): 900000002
                - **TaxLevelCode** (listName=48): R-99-PN
                - **RegistrationAddress**
                  - **CityName**: Medellín
                  - **CountrySubentity**: Antioquia
                  - **AddressLine**
                    - **Line**: Carrera 4 # 5-6
                  - **Country**
                    - **IdentificationCode**: CO
                    - **Name** (languageID=es): Colombia
                - **TaxScheme**
                  - **ID**: ZZ
                  - **Name**: No aplica
              - **Contact**
                - **ElectronicMail**: compras@cliente.test
          - **PaymentMeans**
            - **ID**: 2
            - **PaymentMeansCode**: ZZZ
            - **PaymentDueDate**: 2026-04-03
          - **TaxTotal**
            - **TaxAmount** (currencyID=COP): 190000.00
            - **TaxSubtotal**
              - **TaxableAmount** (currencyID=COP): 1000000.00
              - **TaxAmount** (currencyID=COP): 190000.00
              - **TaxCategory**
                - **Percent**: 19.00
                - **TaxScheme**
                  - **ID**: 01
                  - **Name**: IVA
          - **LegalMonetaryTotal**
            - **LineExtensionAmount** (currencyID=COP): 1000000.00
            - **TaxExclusiveAmount** (currencyID=COP): 1000000.00
            - **TaxInclusiveAmount** (currencyID=COP): 1190000.00
            - **PayableAmount** (currencyID=COP): 1190000.00
          - **InvoiceLine**
            - **ID**: 1
            - **InvoicedQuantity** (unitCode=EA): 10.000000
            - **LineExtensionAmount** (currencyID=COP): 500000.00
            - **Item**
              - **Description**: Resma de papel carta (caja x 10)
            - **Price**
              - **PriceAmount** (currencyID=COP): 50000.00
              - **BaseQuantity** (unitCode=EA): 1.000000
          - **InvoiceLine**
            - **ID**: 2
            - **InvoicedQuantity** (unitCode=EA): 2.000000
            - **LineExtensionAmount** (currencyID=COP): 500000.00
            - **Item**
              - **Description**: Silla de oficina | modelo "Ejemplo" \*ergonómica\*
            - **Price**
              - **PriceAmount** (currencyID=COP): 250000.00
              - **BaseQuantity** (unitCode=EA): 1.000000
  - **ParentDocumentLineReference**
    - **LineID**: 1
    - **DocumentReference**
      - **ID**: SETP990000001
      - **UUID** (schemeName=CUFE-SHA384): 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
      - **IssueDate**: 2026-03-04
      - **DocumentType**: ApplicationResponse
      - **Attachment**
        - **ExternalReference**
          - **MimeCode**: text/xml
          - **EncodingCode**: UTF-8
          - **Description**: un documento XML guardado como texto
            - **ApplicationResponse**
              - **UBLVersionID**: UBL 2.1
              - **ID**: 990000001
              - **IssueDate**: 2026-03-04
              - **IssueTime**: 10:15:30-05:00
              - **SenderParty**
                - **PartyTaxScheme**
                  - **RegistrationName**: Dirección de Impuestos y Aduanas Nacionales
                  - **CompanyID** (schemeID=4, schemeName=31): 800197268
              - **DocumentResponse**
                - **Response**
                  - **ResponseCode**: 02
                  - **Description**: Documento validado por la DIAN (texto de ejemplo)
                - **DocumentReference**
                  - **ID**: SETP990000001
                - **LineResponse**
                  - **Response**
                    - **ResponseCode**: FAD00
                    - **Description**: Mensaje de ejemplo de una regla de validación
      - **ResultOfVerification**
        - **ValidatorID**: Dirección de Impuestos y Aduanas Nacionales
        - **ValidationResultCode**: 02
        - **ValidationDate**: 2026-03-04
        - **ValidationTime**: 10:15:30-05:00
