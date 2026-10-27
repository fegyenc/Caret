# NF-e (electronic invoice) 123 — series 1

> File: br-nfe-autorizada.xml
> NF-e 4.00

- **Access key:** 35260312345678000195550010000001231000000019
- **Nature of the operation:** Venda de mercadoria
- **Issue date:** 2026-03-04T10:15:30-03:00
- **Model:** 55 — NF-e
- **Series:** 1
- **Number:** 123
- **Type:** 1 — Saída
- **Destination of the operation:** 2 — Operação interestadual
- **Purpose of the issue:** 1 — NF-e normal
- **Final consumer:** 0 — Não
- **Presence of the buyer:** 1 — Operação presencial
- **Environment:** 1 — Produção
- **Form of issue:** 1 — Normal
- **Version of the issuing software:** Sistema Exemplo 1.0

## Issuer

- **Name:** EMPRESA EXEMPLO COMERCIO LTDA
- **Trade name:** Exemplo
- **Tax ID (CNPJ / CPF):** 12345678000195
- **State registration (IE):** 111111111111
- **Tax regime (CRT):** 3 — Regime Normal
- **Address:** Rua das Amostras, 100, Centro — Sao Paulo/SP — 01000000 — Brasil
- **Phone:** 1130000000

## Recipient

- **Name:** CLIENTE DE TESTE LTDA
- **Tax ID (CNPJ / CPF):** 98765432000110
- **State registration (IE):** 222222222222
- **Indicator of the state registration:** 1 — Contribuinte ICMS
- **Address:** Avenida Fictícia, S/N, Jardim — Rio de Janeiro/RJ — 20000000 — Brasil
- **Email:** compras@cliente.exemplo

## Items

| # | Code | Description | NCM | CFOP | Unit | Quantity | Unit price | Amount | Discount |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | A-001 | Caderno espiral \| \*capa dura\* | 48201000 | 6102 | UN | 10.0000 | 25.0000000000 | 250.00 | 10.00 |
| 2 | B-777 | Caneta esferográfica azul | 96081000 | 6102 | CX | 5.0000 | 40.0000000000 | 200.00 |  |

## Totals

- **ICMS base:** 440.00
- **ICMS:** 52.80
- **ICMS exempted:** 0.00
- **ICMS-ST base:** 0.00
- **ICMS-ST:** 0.00
- **Total of the products:** 450.00
- **Freight:** 20.00
- **Insurance:** 0.00
- **Discount:** 10.00
- **Import tax (II):** 0.00
- **IPI:** 0.00
- **PIS:** 0.00
- **COFINS:** 0.00
- **Other expenses:** 0.00
- **Total of the invoice:** 460.00
- **Approximate total of taxes:** 95.20

## Transport

- **Mode of freight:** 0 — Contratação do frete por conta do remetente (CIF)
- **Carrier:** TRANSPORTES EXEMPLO S.A. · 11222333000181
- **Volumes:** 3 · CAIXAS · net weight 12.500 · gross weight 13.200

## Billing

- **Bill number:** 123
- **Original value:** 460.00
- **Net value:** 460.00

| Installment | Due date | Amount |
| --- | --- | --- |
| 001 | 2026-04-03 | 230.00 |
| 002 | 2026-05-03 | 230.00 |

## Payment

| Means of payment | Description | Amount |
| --- | --- | --- |
| 17 — Pagamento instantâneo (PIX) |  | 460.00 |

## Additional information

- **For the tax authority:** Texto de exemplo para o fisco.
- **Complementary:** Pedido 4567. Entrega em 5 dias úteis.

## Answer of the tax authority

- **Result:** 100: Autorizado o uso da NF-e
- **Protocol:** 135260000000001
- **Received at:** 2026-03-04T10:15:40-03:00
- **Environment:** 1 — Produção
- **Version of the application:** SP_NFE_PL_009_V4
- **Access key:** 35260312345678000195550010000001231000000019

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **nfeProc** (versao=4.00)
  - **NFe**
    - **infNFe** (versao=4.00, Id=NFe35260312345678000195550010000001231000000019)
      - **ide**
        - **cUF**: 35
        - **cNF**: 00000001
        - **natOp**: Venda de mercadoria
        - **mod**: 55
        - **serie**: 1
        - **nNF**: 123
        - **dhEmi**: 2026-03-04T10:15:30-03:00
        - **tpNF**: 1
        - **idDest**: 2
        - **cMunFG**: 3550308
        - **tpImp**: 1
        - **tpEmis**: 1
        - **cDV**: 9
        - **tpAmb**: 1
        - **finNFe**: 1
        - **indFinal**: 0
        - **indPres**: 1
        - **procEmi**: 0
        - **verProc**: Sistema Exemplo 1.0
      - **emit**
        - **CNPJ**: 12345678000195
        - **xNome**: EMPRESA EXEMPLO COMERCIO LTDA
        - **xFant**: Exemplo
        - **enderEmit**
          - **xLgr**: Rua das Amostras
          - **nro**: 100
          - **xBairro**: Centro
          - **cMun**: 3550308
          - **xMun**: Sao Paulo
          - **UF**: SP
          - **CEP**: 01000000
          - **cPais**: 1058
          - **xPais**: Brasil
          - **fone**: 1130000000
        - **IE**: 111111111111
        - **CRT**: 3
      - **dest**
        - **CNPJ**: 98765432000110
        - **xNome**: CLIENTE DE TESTE LTDA
        - **enderDest**
          - **xLgr**: Avenida Fictícia
          - **nro**: S/N
          - **xBairro**: Jardim
          - **cMun**: 3304557
          - **xMun**: Rio de Janeiro
          - **UF**: RJ
          - **CEP**: 20000000
          - **cPais**: 1058
          - **xPais**: Brasil
        - **indIEDest**: 1
        - **IE**: 222222222222
        - **email**: compras@cliente.exemplo
      - **det** (nItem=1)
        - **prod**
          - **cProd**: A-001
          - **cEAN**: SEM GTIN
          - **xProd**: Caderno espiral | \*capa dura\*
          - **NCM**: 48201000
          - **CFOP**: 6102
          - **uCom**: UN
          - **qCom**: 10.0000
          - **vUnCom**: 25.0000000000
          - **vProd**: 250.00
          - **cEANTrib**: SEM GTIN
          - **uTrib**: UN
          - **qTrib**: 10.0000
          - **vUnTrib**: 25.0000000000
          - **vDesc**: 10.00
          - **indTot**: 1
        - **imposto**
          - **ICMS**
            - **ICMS00**
              - **orig**: 0
              - **CST**: 00
              - **modBC**: 3
              - **vBC**: 240.00
              - **pICMS**: 12.0000
              - **vICMS**: 28.80
      - **det** (nItem=2)
        - **prod**
          - **cProd**: B-777
          - **cEAN**: SEM GTIN
          - **xProd**: Caneta esferográfica azul
          - **NCM**: 96081000
          - **CFOP**: 6102
          - **uCom**: CX
          - **qCom**: 5.0000
          - **vUnCom**: 40.0000000000
          - **vProd**: 200.00
          - **cEANTrib**: SEM GTIN
          - **uTrib**: CX
          - **qTrib**: 5.0000
          - **vUnTrib**: 40.0000000000
          - **indTot**: 1
        - **imposto**
          - **ICMS**
            - **ICMS00**
              - **orig**: 0
              - **CST**: 00
              - **modBC**: 3
              - **vBC**: 200.00
              - **pICMS**: 12.0000
              - **vICMS**: 24.00
      - **total**
        - **ICMSTot**
          - **vBC**: 440.00
          - **vICMS**: 52.80
          - **vICMSDeson**: 0.00
          - **vFCP**: 0.00
          - **vBCST**: 0.00
          - **vST**: 0.00
          - **vFCPST**: 0.00
          - **vFCPSTRet**: 0.00
          - **vProd**: 450.00
          - **vFrete**: 20.00
          - **vSeg**: 0.00
          - **vDesc**: 10.00
          - **vII**: 0.00
          - **vIPI**: 0.00
          - **vIPIDevol**: 0.00
          - **vPIS**: 0.00
          - **vCOFINS**: 0.00
          - **vOutro**: 0.00
          - **vNF**: 460.00
          - **vTotTrib**: 95.20
      - **transp**
        - **modFrete**: 0
        - **transporta**
          - **xNome**: TRANSPORTES EXEMPLO S.A.
          - **CNPJ**: 11222333000181
        - **vol**
          - **qVol**: 3
          - **esp**: CAIXAS
          - **pesoL**: 12.500
          - **pesoB**: 13.200
      - **cobr**
        - **fat**
          - **nFat**: 123
          - **vOrig**: 460.00
          - **vLiq**: 460.00
        - **dup**
          - **nDup**: 001
          - **dVenc**: 2026-04-03
          - **vDup**: 230.00
        - **dup**
          - **nDup**: 002
          - **dVenc**: 2026-05-03
          - **vDup**: 230.00
      - **pag**
        - **detPag**
          - **indPag**: 1
          - **tPag**: 17
          - **vPag**: 460.00
      - **infAdic**
        - **infAdFisco**: Texto de exemplo para o fisco.
        - **infCpl**: Pedido 4567. Entrega em 5 dias úteis.
    - **Signature**: digital signature (not printed)
  - **protNFe** (versao=4.00)
    - **infProt**
      - **tpAmb**: 1
      - **verAplic**: SP_NFE_PL_009_V4
      - **chNFe**: 35260312345678000195550010000001231000000019
      - **dhRecbto**: 2026-03-04T10:15:40-03:00
      - **nProt**: 135260000000001
      - **digVal**: U1lOVEhFVElDRElHRVNUCg==
      - **cStat**: 100
      - **xMotivo**: Autorizado o uso da NF-e
