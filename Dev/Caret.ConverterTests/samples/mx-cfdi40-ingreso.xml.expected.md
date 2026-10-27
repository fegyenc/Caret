# CFDI of income (invoice) A 1001

> File: mx-cfdi40-ingreso.xml
> CFDI 4.0

- **Issue date:** 2026-03-04T10:15:30
- **Series:** A
- **Folio:** 1001
- **Currency:** MXN
- **Place of issue (postal code):** 06600
- **Form of payment:** 03 — Transferencia electrónica de fondos
- **Method of payment:** PUE — Pago en una sola exhibición
- **Payment terms:** Contado
- **Export:** 01 — No aplica

## Issuer

- **Name:** ESCUELA KEMPER URGATE
- **RFC:** EKU9003173C9
- **Tax regime:** 601 — General de Ley Personas Morales

## Receiver

- **Name:** UNIVERSIDAD ROBOTICA ESPAÑOLA
- **RFC:** URE180429TM6
- **Tax regime:** 601 — General de Ley Personas Morales
- **Fiscal postal code:** 65000
- **Use of the CFDI:** G03 — Gastos en general

## Lines

| # | Product/service key | Description | Quantity | Unit | Unit price | Discount | Amount | Tax object |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 43211503 | Computadora portátil (ejemplo) | 2 | H87 · Pieza | 400.00 | 80.00 | 800.00 | 02 |
| 2 | 81111500 | Servicio de instalación \| \*ejemplo\* | 1 | E48 · Servicio | 200.00 | 20.00 | 200.00 | 02 |

## Taxes

- **Total transferred taxes:** 144.00

**Transferred**

| Tax | Base | Factor | Rate | Amount |
| --- | --- | --- | --- | --- |
| 002 — IVA | 900.00 | Tasa | 0.160000 | 144.00 |

## Totals

- **Subtotal:** 1000.00
- **Discount:** 100.00
- **Total:** 1044.00

## Digital stamp (Timbre Fiscal Digital)

- **UUID:** 6128396f-c09b-4ec6-8699-43c5f7e3b230
- **Stamp date:** 2026-03-04T10:15:35
- **Certification provider (RFC):** AAA010101AAA
- **SAT certificate number:** 30001000000500003456

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **Comprobante** (schemaLocation=http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd, Version=4.0, Serie=A, Folio=1001, Fecha=2026-03-04T10:15:30, Sello=digital seal of 180 characters (not printed), FormaPago=03, NoCertificado=30001000000500003416, Certificado=digital seal of 180 characters (not printed), CondicionesDePago=Contado, SubTotal=1000.00, Descuento=100.00, Moneda=MXN, Total=1044.00, TipoDeComprobante=I, Exportacion=01, MetodoPago=PUE, LugarExpedicion=06600)
  - **Emisor** (Rfc=EKU9003173C9, Nombre=ESCUELA KEMPER URGATE, RegimenFiscal=601)
  - **Receptor** (Rfc=URE180429TM6, Nombre=UNIVERSIDAD ROBOTICA ESPAÑOLA, DomicilioFiscalReceptor=65000, RegimenFiscalReceptor=601, UsoCFDI=G03)
  - **Conceptos**
    - **Concepto** (ClaveProdServ=43211503, NoIdentificacion=PC-01, Cantidad=2, ClaveUnidad=H87, Unidad=Pieza, Descripcion=Computadora portátil (ejemplo), ValorUnitario=400.00, Importe=800.00, Descuento=80.00, ObjetoImp=02)
      - **Impuestos**
        - **Traslados**
          - **Traslado** (Base=720.00, Impuesto=002, TipoFactor=Tasa, TasaOCuota=0.160000, Importe=115.20)
    - **Concepto** (ClaveProdServ=81111500, Cantidad=1, ClaveUnidad=E48, Unidad=Servicio, Descripcion=Servicio de instalación | \*ejemplo\*, ValorUnitario=200.00, Importe=200.00, Descuento=20.00, ObjetoImp=02)
      - **Impuestos**
        - **Traslados**
          - **Traslado** (Base=180.00, Impuesto=002, TipoFactor=Tasa, TasaOCuota=0.160000, Importe=28.80)
  - **Impuestos** (TotalImpuestosTrasladados=144.00)
    - **Traslados**
      - **Traslado** (Base=900.00, Impuesto=002, TipoFactor=Tasa, TasaOCuota=0.160000, Importe=144.00)
  - **Complemento**
    - **TimbreFiscalDigital** (schemaLocation=http://www.sat.gob.mx/TimbreFiscalDigital http://www.sat.gob.mx/sitio_internet/cfd/TimbreFiscalDigital/TimbreFiscalDigitalv11.xsd, Version=1.1, UUID=6128396f-c09b-4ec6-8699-43c5f7e3b230, FechaTimbrado=2026-03-04T10:15:35, RfcProvCertif=AAA010101AAA, SelloCFD=digital seal of 180 characters (not printed), NoCertificadoSAT=30001000000500003456, SelloSAT=digital seal of 180 characters (not printed))
