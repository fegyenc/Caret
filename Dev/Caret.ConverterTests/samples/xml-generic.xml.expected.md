# Reporte

> File: xml-generic.xml
> Namespace: `http://ejemplo.test/reporte/1.0`

- **Reporte** (version=1.0, periodo=2026-02)
  - **Cabecera**
    - **Entidad**: Empresa Ejemplo S.A.S.
    - **Generado**: 2026-03-01T08:30:00-05:00
    - **Observacion**: Línea con | barra, \*asteriscos\* y \_guiones_bajos\_
    - **Vacio**
  - **Movimientos**

    | Fecha | Concepto | Valor |
    | --- | --- | --- |
    | 2026-02-03 | Venta de mostrador | 120000.50 [moneda=COP] |
    | 2026-02-10 | Servicio técnico | 80000.00 [moneda=COP] |
    | 2026-02-21 | Devolución | -15000.25 [moneda=COP] |

  - **Anexo** (formato=text/xml): <?xml version="1.0"?>&lt;Interno>&lt;Dato>valor&lt;/Dato>&lt;/Interno> con relleno para pasar de cuatrocientos caracteres: Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.
  - **Signature**: digital signature (not printed)
