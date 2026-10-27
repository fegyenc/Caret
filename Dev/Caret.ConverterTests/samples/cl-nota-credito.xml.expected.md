# Electronic credit note 9

> File: cl-nota-credito.xml

- **Type:** 61 — Nota de Crédito Electrónica
- **Folio:** 9
- **Issue date:** 2026-03-10
- **Currency:** CLP (Chilean pesos)
- **Format version:** 1.0

## Issuer

- **Name:** Muestra Proveedora SpA
- **RUT:** 76000001-1
- **Line of business:** Venta de artículos de muestra
- **Economic activity code:** 479100
- **Address:** Calle de Prueba 100, Providencia

## Receiver

- **Name:** Cliente de Ejemplo Limitada
- **RUT:** 76000002-K

## Items

| # | Code | Name | Quantity | Unit price | Amount |
| --- | --- | --- | --- | --- | --- |
| 1 |  | Devolución de ejemplo | 1 | 10000 | 10000 |

## Totals

- **Net amount:** 10000
- **VAT rate:** 19.00 %
- **VAT:** 1900
- **Total:** 11900

## Referenced documents

| # | Document type | Folio | Date | Use | Reason |
| --- | --- | --- | --- | --- | --- |
| 1 | 33 — Factura Electrónica | 1234 | 2026-03-04 | 3 — Corrige Montos | Corrige montos de la factura (texto de ejemplo) |

## Electronic stamp

- **Stamp issued at:** 2026-03-10T09:15:00
- **Signature of the stamp:** digital signature (not printed)
- **Document signed at:** 2026-03-10T09:15:01

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **DTE** (version=1.0)
  - **Documento** (ID=F9T61)
    - **Encabezado**
      - **IdDoc**
        - **TipoDTE**: 61
        - **Folio**: 9
        - **FchEmis**: 2026-03-10
      - **Emisor**
        - **RUTEmisor**: 76000001-1
        - **RznSoc**: Muestra Proveedora SpA
        - **GiroEmis**: Venta de artículos de muestra
        - **Acteco**: 479100
        - **DirOrigen**: Calle de Prueba 100
        - **CmnaOrigen**: Providencia
      - **Receptor**
        - **RUTRecep**: 76000002-K
        - **RznSocRecep**: Cliente de Ejemplo Limitada
      - **Totales**
        - **MntNeto**: 10000
        - **TasaIVA**: 19.00
        - **IVA**: 1900
        - **MntTotal**: 11900
    - **Detalle**
      - **NroLinDet**: 1
      - **NmbItem**: Devolución de ejemplo
      - **QtyItem**: 1
      - **PrcItem**: 10000
      - **MontoItem**: 10000
    - **Referencia**
      - **NroLinRef**: 1
      - **TpoDocRef**: 33
      - **FolioRef**: 1234
      - **FchRef**: 2026-03-04
      - **CodRef**: 3
      - **RazonRef**: Corrige montos de la factura (texto de ejemplo)
    - **TED** (version=1.0)
      - **DD**
        - **RE**: 76000001-1
        - **TD**: 61
        - **F**: 9
        - **FE**: 2026-03-10
        - **RR**: 76000002-K
        - **RSR**: Cliente de Ejemplo Limitada
        - **MNT**: 11900
        - **IT1**: Devolución de ejemplo
        - **TSTED**: 2026-03-10T09:15:00
      - **FRMT**: digital signature (not printed)
    - **TmstFirma**: 2026-03-10T09:15:01
