# MDF-e (electronic manifest of fiscal documents) 123 — series 1

> File: br-mdfe-autorizado.xml
> MDF-e 3.00

- **Access key:** 35261011222333000181580010000001231000000073
- **Issue date:** 2026-10-20T11:00:00-03:00
- **Model:** 58 — MDF-e
- **Series:** 1
- **Number:** 123
- **Mode of transport:** 1 — Rodoviário
- **Type of issuer:** 1 — Prestador de serviço de transporte
- **Type of carrier:** 1 — ETC
- **Environment:** 1 — Produção
- **Form of issue:** 1 — Normal
- **State of loading:** SP
- **State of unloading:** RJ
- **Places of loading:** SAO PAULO
- **Route (states):** MG
- **Planned start of the trip:** 2026-10-20T14:00:00-03:00
- **Version of the issuing software:** Exemplo 1.0

## Issuer

- **Name:** Transportadora Exemplo Ltda
- **Trade name:** Transporte Exemplo
- **Tax ID (CNPJ / CPF):** 11222333000181
- **State registration (IE):** 111222333444
- **Address:** Rua das Rodovias, 100, Centro — SAO PAULO/SP — 01001000
- **Phone:** 1130000000

## Road transport

- **RNTRC (registry of road carriers):** 12345678

| Vehicle | Plate | Tare (kg) | Capacity (kg) | Capacity (m³) | Wheel type | Body | State | Owner |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Tractor | ABC1D23 | 9000 | 25000 |  | 03 — Cavalo Mecânico | 00 — não aplicável | SP |  |
| Trailer | XYZ9A87 | 6000 | 30000 |  |  | 02 — Fechada/Baú | SP |  |

| Driver | CPF |
| --- | --- |
| Motorista Inventado | 12345678909 |

## Unloading: RIO DE JANEIRO

| Document | Access key |
| --- | --- |
| CT-e | 35261011222333000181570010000004561000000019 |
| CT-e | 35261011222333000181570010000004571000000026 |

## Cargo

- **Type of cargo:** 05 — Carga Geral
- **Main product:** Material de escritorio
- **Number of CT-e:** 2
- **Total value of the cargo:** 30000.00
- **Gross weight:** 1700.0000 KG
- **Seals:** 00012345

## Additional information

- **Complementary:** Viagem de teste com dados inventados.

## Answer of the tax authority

- **Result:** 100: Autorizado o uso do MDF-e
- **Protocol:** 935260000000123
- **Received at:** 2026-10-20T11:01:05-03:00
- **Environment:** 1 — Produção
- **Version of the application:** SVRS202610
- **Access key:** 35261011222333000181580010000001231000000073

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **mdfeProc** (versao=3.00)
  - **MDFe**
    - **infMDFe** (Id=MDFe35261011222333000181580010000001231000000073, versao=3.00)
      - **ide**
        - **cUF**: 35
        - **tpAmb**: 1
        - **tpEmit**: 1
        - **tpTransp**: 1
        - **mod**: 58
        - **serie**: 1
        - **nMDF**: 123
        - **cMDF**: 00000007
        - **cDV**: 3
        - **modal**: 1
        - **dhEmi**: 2026-10-20T11:00:00-03:00
        - **tpEmis**: 1
        - **procEmi**: 0
        - **verProc**: Exemplo 1.0
        - **UFIni**: SP
        - **UFFim**: RJ
        - **infMunCarrega**
          - **cMunCarrega**: 3550308
          - **xMunCarrega**: SAO PAULO
        - **infPercurso**
          - **UFPer**: MG
        - **dhIniViagem**: 2026-10-20T14:00:00-03:00
      - **emit**
        - **CNPJ**: 11222333000181
        - **IE**: 111222333444
        - **xNome**: Transportadora Exemplo Ltda
        - **xFant**: Transporte Exemplo
        - **enderEmit**
          - **xLgr**: Rua das Rodovias
          - **nro**: 100
          - **xBairro**: Centro
          - **cMun**: 3550308
          - **xMun**: SAO PAULO
          - **CEP**: 01001000
          - **UF**: SP
          - **fone**: 1130000000
      - **infModal** (versaoModal=3.00)
        - **rodo**
          - **infANTT**
            - **RNTRC**: 12345678
          - **veicTracao**
            - **placa**: ABC1D23
            - **tara**: 9000
            - **capKG**: 25000
            - **condutor**
              - **xNome**: Motorista Inventado
              - **CPF**: 12345678909
            - **tpRod**: 03
            - **tpCar**: 00
            - **UF**: SP
          - **veicReboque**
            - **placa**: XYZ9A87
            - **tara**: 6000
            - **capKG**: 30000
            - **tpCar**: 02
            - **UF**: SP
      - **infDoc**
        - **infMunDescarga**
          - **cMunDescarga**: 3304557
          - **xMunDescarga**: RIO DE JANEIRO
          - **infCTe**
            - **chCTe**: 35261011222333000181570010000004561000000019
          - **infCTe**
            - **chCTe**: 35261011222333000181570010000004571000000026
      - **prodPred**
        - **tpCarga**: 05
        - **xProd**: Material de escritorio
      - **tot**
        - **qCTe**: 2
        - **vCarga**: 30000.00
        - **cUnid**: 01
        - **qCarga**: 1700.0000
      - **lacres**
        - **nLacre**: 00012345
      - **infAdic**
        - **infCpl**: Viagem de teste com dados inventados.
    - **Signature**: digital signature (not printed)
  - **protMDFe** (versao=3.00)
    - **infProt**
      - **tpAmb**: 1
      - **verAplic**: SVRS202610
      - **chMDFe**: 35261011222333000181580010000001231000000073
      - **dhRecbto**: 2026-10-20T11:01:05-03:00
      - **nProt**: 935260000000123
      - **digVal**: aW52ZW50YWRvaW52ZW50YWRvaW52ZW50YWQ=
      - **cStat**: 100
      - **xMotivo**: Autorizado o uso do MDF-e
