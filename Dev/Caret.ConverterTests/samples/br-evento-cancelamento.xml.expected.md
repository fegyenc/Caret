# Event of an NF-e: Cancelamento

> File: br-evento-cancelamento.xml

- **Access key:** 35260312345678000195550010000001231000000019
- **Type of event:** 110111 — Cancelamento
- **Date of the event:** 2026-03-10T09:00:00-03:00
- **Sequence number:** 1
- **Environment:** 1 — Produção
- **Tax ID (CNPJ / CPF):** 12345678000195
- **Description:** Cancelamento
- **Protocol:** 135260000000001
- **Justification:** Pedido cancelado a pedido do cliente (texto de exemplo)

## Answer of the tax authority

- **Result:** 135: Evento registrado e vinculado a NF-e
- **Event:** Cancelamento registrado
- **Protocol:** 135260000000099
- **Registered at:** 2026-03-10T09:00:05-03:00

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **procEventoNFe** (versao=1.00)
  - **evento** (versao=1.00)
    - **infEvento** (Id=ID1101113526031234567800019555001000000123100000001901)
      - **cOrgao**: 35
      - **tpAmb**: 1
      - **CNPJ**: 12345678000195
      - **chNFe**: 35260312345678000195550010000001231000000019
      - **dhEvento**: 2026-03-10T09:00:00-03:00
      - **tpEvento**: 110111
      - **nSeqEvento**: 1
      - **verEvento**: 1.00
      - **detEvento** (versao=1.00)
        - **descEvento**: Cancelamento
        - **nProt**: 135260000000001
        - **xJust**: Pedido cancelado a pedido do cliente (texto de exemplo)
    - **Signature**: digital signature (not printed)
  - **retEvento** (versao=1.00)
    - **infEvento**
      - **tpAmb**: 1
      - **verAplic**: SP_EVENTOS_PL_100
      - **cOrgao**: 35
      - **cStat**: 135
      - **xMotivo**: Evento registrado e vinculado a NF-e
      - **chNFe**: 35260312345678000195550010000001231000000019
      - **tpEvento**: 110111
      - **xEvento**: Cancelamento registrado
      - **nSeqEvento**: 1
      - **CNPJDest**: 98765432000110
      - **dhRegEvento**: 2026-03-10T09:00:05-03:00
      - **nProt**: 135260000000099
