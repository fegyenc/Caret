using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Typedown.WinUI.Services.Conversion
{
    // New since the fork: Brazil's national standard of the NFS-e, the electronic service invoice (Sistema Nacional NFS-e, Receita Federal and the
    // municipalities), in the namespace http://www.sped.fazenda.gov.br/nfse. Four documents: the DPS (Declaracao de Prestacao de Servicos, what the
    // provider sends), the NFS-e (what the national environment returns: the DPS inside it, with the number, the access key and the computed
    // values), and the events (evento, pedRegEvento: cancellation, substitution, manifestation, by the tax office).
    //
    // The structure, the formulas and the codes were read from the XSD package that the national portal publishes (NFSe-ESQUEMAS_XSD v1.01 of
    // 2026-02-09), and the code tables below are generated from the documentation of its simple types, not typed or remembered. The names of the codes are
    // the authority's, in Portuguese whatever the language of the interface (as the other packs do); the titles and labels follow the interface.
    //
    // The principles of the other packs: amounts and dates as the file has them, the digital signature named and not printed, and the caller adds
    // every field of the file after this summary. The checks (the ISSQN against its base and rate, the total of the withholdings, the net value, and the
    // access key against the number and the issuer) use the formulas written in the schema; they are not the validation of the national environment.
    internal static class XmlNfse
    {
        private const string Namespace = "http://www.sped.fazenda.gov.br/nfse";
        private const decimal Cent = 0.011m;

        internal static bool IsNfse(XElement root) =>
            root.Name.NamespaceName == Namespace && root.Name.LocalName is "NFSe" or "DPS" or "evento" or "pedRegEvento";

        public static bool TryRender(XElement root, ConversionContext context, XmlLabels xl, StringBuilder sb)
        {
            if (!IsNfse(root)) return false;
            var w = new Words(xl.IsPortuguese ? 2 : xl.IsSpanish ? 1 : 0);
            return root.Name.LocalName is "evento" or "pedRegEvento" ? Event(sb, root, context, xl, w) : Document(sb, root, context, xl, w);
        }

        // ---- the NFS-e and the DPS -------------------------------------------------------------------------------------------------------

        private static bool Document(StringBuilder sb, XElement root, ConversionContext context, XmlLabels xl, Words w)
        {
            var nfse = root.Name.LocalName == "NFSe" ? XmlUbl.Child(root, "infNFSe") : null;
            var dps = nfse != null ? XmlUbl.Child(nfse, "DPS/infDPS") : XmlUbl.Child(root, "infDPS");
            if (nfse == null && dps == null) return false;
            var key = Key(nfse);

            var number = T(nfse, "nNFSe");
            var provider = nfse != null ? T(nfse, "emit/xNome") : T(dps, "prest/xNome");
            var title = nfse != null
                ? "NFS-e" + (number == null ? "" : " " + number)
                : w.W("Service provision declaration (DPS)", "Declaración de prestación de servicios (DPS)", "Declaração de Prestação de Serviços (DPS)") + (T(dps, "nDPS") == null ? "" : " " + T(dps, "nDPS"));
            XmlUbl.Heading(sb, 1, title + (provider == null ? "" : " · " + provider));
            var version = root.Attribute("versao")?.Value;
            var line = XmlUbl.Safe(Join(" · ", string.IsNullOrEmpty(context.SourceName) ? null : xl.File + ": " + context.SourceName,
                w.W("National NFS-e standard", "Estándar nacional de la NFS-e", "Padrão nacional da NFS-e") + (version == null ? "" : " " + version),
                nfse != null ? Coded(Status, T(nfse, "cStat")) : w.W("Declaration only: not yet an NFS-e", "Solo la declaración: aún no es una NFS-e", "Apenas a declaração: ainda não é uma NFS-e")));
            sb.Append("> ").Append(line).Append("\n\n");

            XmlUbl.Heading(sb, 2, w.W("The document", "El documento", "O documento"));
            var facts = new List<(string, string)>();
            Add(facts, w.W("NFS-e number", "Número de la NFS-e", "Número da NFS-e"), number);
            Add(facts, w.W("Access key", "Clave de acceso", "Chave de acesso"), key);
            Add(facts, w.W("Series and DPS number", "Serie y número de la DPS", "Série e número da DPS"), Join(" / ", T(dps, "serie"), T(dps, "nDPS")));
            Add(facts, w.W("Issued on", "Emitida el", "Emitida em"), T(dps, "dhEmi"));
            Add(facts, w.W("Competence date", "Fecha de competencia", "Data de competência"), T(dps, "dCompet"));
            Add(facts, w.W("Processed on", "Procesada el", "Processada em"), T(nfse, "dhProc"));
            Add(facts, w.W("Issuing municipality", "Municipio emisor", "Município emissor"), Join(" · ", T(nfse, "xLocEmi"), T(dps, "cLocEmi") is { } mun ? "IBGE " + mun : null));
            Add(facts, w.W("Place of the service", "Lugar del servicio", "Local da prestação"), T(nfse, "xLocPrestacao") ?? Join(" · ", T(dps, "serv/locPrest/cLocPrestacao") is { } lp ? "IBGE " + lp : null, T(dps, "serv/locPrest/cPaisPrestacao")));
            Add(facts, w.W("Place of incidence of the ISSQN", "Lugar de incidencia del ISSQN", "Local de incidência do ISSQN"), Join(" · ", T(nfse, "xLocIncid"), T(nfse, "cLocIncid") is { } inc ? "IBGE " + inc : null));
            Add(facts, w.W("Environment", "Ambiente", "Ambiente"), Coded(Environment, T(dps, "tpAmb")));
            Add(facts, w.W("Generated by", "Generada por", "Gerada por"), Coded(Generator, T(nfse, "ambGer")));
            Add(facts, w.W("DPS issued by", "DPS emitida por", "DPS emitida por"), Coded(IssuerKind, T(dps, "tpEmit")));
            Add(facts, w.W("Reason (issued by the customer)", "Motivo (emitida por el tomador)", "Motivo (emitida pelo tomador)"), Coded(ReasonByTaker, T(dps, "cMotivoEmisTI")));
            Add(facts, w.W("Rejected NFS-e", "NFS-e rechazada", "NFS-e rejeitada"), T(dps, "chNFSeRej"));
            Add(facts, w.W("Kind of issue", "Tipo de emisión", "Tipo de emissão"), Coded(EmissionKind, T(nfse, "tpEmis")));
            Add(facts, w.W("Process of issue", "Proceso de emisión", "Processo de emissão"), Coded(EmissionProcess, T(nfse, "procEmi") ?? T(dps, "procEmi")));
            Add(facts, w.W("Application version", "Versión de la aplicación", "Versão do aplicativo"), Join(" / ", T(dps, "verAplic"), nfse != null && T(nfse, "verAplic") != T(dps, "verAplic") ? T(nfse, "verAplic") : null));
            Add(facts, w.W("Replaces the NFS-e", "Sustituye a la NFS-e", "Substitui a NFS-e"), T(dps, "subst/chSubstda"));
            Add(facts, w.W("Reason of the substitution", "Motivo de la sustitución", "Motivo da substituição"), Join(" · ", Coded(SubstitutionReason, T(dps, "subst/cMotivo")), T(dps, "subst/xMotivo")));
            if (root.Descendants().Any(e => e.Name.LocalName == "Signature")) Add(facts, w.W("Signature", "Firma", "Assinatura"), xl.SignaturePresent);
            XmlUbl.Facts(sb, facts);

            var providerNode = nfse != null ? XmlUbl.Child(nfse, "emit") : XmlUbl.Child(dps, "prest");
            Party(sb, w.W("Provider", "Prestador", "Prestador"), providerNode, w, XmlUbl.Child(dps, "prest/regTrib"));
            Party(sb, w.W("Customer", "Tomador", "Tomador"), XmlUbl.Child(dps, "toma"), w, null);
            Party(sb, w.W("Intermediary", "Intermediario", "Intermediário"), XmlUbl.Child(dps, "interm"), w, null);
            Service(sb, nfse, dps, w);
            Values(sb, nfse, dps, w);
            Taxes(sb, nfse, dps, w);
            Checks(sb, nfse, dps, key, w);
            return true;
        }

        // The identifier of the NFS-e is "NFS" and the access key of 50 digits: municipality 7, generating environment 1, kind of registration 1,
        // federal registration 14, number 13, year and month 4, numeric code 9, check digit 1 (the documentation of TSIdNFSe).
        private static string Key(XElement nfse)
        {
            var id = nfse?.Attribute("Id")?.Value;
            return id != null && id.Length == 53 && id.StartsWith("NFS", StringComparison.Ordinal) && id.Skip(3).All(char.IsAsciiDigit) ? id.Substring(3) : null;
        }

        private static void Party(StringBuilder sb, string title, XElement p, Words w, XElement regime)
        {
            if (p == null) return;
            XmlUbl.Heading(sb, 2, title);
            var facts = new List<(string, string)>();
            Add(facts, w.W("Name", "Nombre", "Nome"), T(p, "xNome"));
            Add(facts, w.W("Trade name", "Nombre comercial", "Nome fantasia"), T(p, "xFant"));
            if (T(p, "CNPJ") != null) Add(facts, "CNPJ", T(p, "CNPJ"));
            if (T(p, "CPF") != null) Add(facts, "CPF", T(p, "CPF"));
            Add(facts, "NIF", T(p, "NIF"));
            Add(facts, w.W("Reason for no NIF", "Motivo de no informar el NIF", "Motivo de não informar o NIF"), Coded(NoNif, T(p, "cNaoNIF")));
            Add(facts, w.W("Municipal registration (IM)", "Inscripción municipal (IM)", "Inscrição municipal (IM)"), T(p, "IM"));
            Add(facts, "CAEPF", T(p, "CAEPF"));
            Add(facts, w.W("Address", "Dirección", "Endereço"), Address(p));
            Add(facts, w.W("Phone", "Teléfono", "Telefone"), T(p, "fone"));
            Add(facts, "Email", T(p, "email"));
            if (regime != null)
            {
                Add(facts, w.W("Simples Nacional", "Simples Nacional", "Simples Nacional"), Coded(SimplesStatus, T(regime, "opSimpNac")));
                Add(facts, w.W("Tax computation under the Simples Nacional", "Régimen de cálculo en el Simples Nacional", "Regime de apuração no Simples Nacional"), Coded(SimplesRegime, T(regime, "regApTribSN")));
                Add(facts, w.W("Special tax regime", "Régimen especial de tributación", "Regime especial de tributação"), Coded(SpecialRegime, T(regime, "regEspTrib")));
            }
            XmlUbl.Facts(sb, facts);
        }

        // The national address of a party (the NFS-e's issuer has its own group, with the state), or the address abroad.
        private static string Address(XElement p)
        {
            var nac = XmlUbl.Child(p, "end/endNac") ?? XmlUbl.Child(p, "enderNac");
            var holder = XmlUbl.Child(p, "end") ?? XmlUbl.Child(p, "enderNac");
            if (holder == null) return null;
            var street = Join(", ", Join(" ", T(holder, "xLgr"), T(holder, "nro")), T(holder, "xCpl"), T(holder, "xBairro"));
            if (nac != null)
                return Join(" · ", street, T(nac, "cMun") is { } m ? "IBGE " + m : null, T(nac, "UF"), T(nac, "CEP") is { } c ? "CEP " + c : null);
            var ext = XmlUbl.Child(holder, "endExt");
            return ext == null ? street : Join(" · ", street, T(ext, "xCidade"), T(ext, "xEstProvReg"), T(ext, "cEndPost"), T(ext, "cPais"));
        }

        private static void Service(StringBuilder sb, XElement nfse, XElement dps, Words w)
        {
            var serv = XmlUbl.Child(dps, "serv");
            if (serv == null) return;
            XmlUbl.Heading(sb, 2, w.W("The service", "El servicio", "O serviço"));
            var facts = new List<(string, string)>();
            Add(facts, w.W("National tax code (cTribNac)", "Código de tributación nacional (cTribNac)", "Código de tributação nacional (cTribNac)"), Join(" — ", T(serv, "cServ/cTribNac"), T(nfse, "xTribNac")));
            Add(facts, w.W("Municipal tax code (cTribMun)", "Código de tributación municipal (cTribMun)", "Código de tributação municipal (cTribMun)"), Join(" — ", T(serv, "cServ/cTribMun"), T(nfse, "xTribMun")));
            Add(facts, "NBS", Join(" — ", T(serv, "cServ/cNBS"), T(nfse, "xNBS")));
            Add(facts, w.W("Description", "Descripción", "Descrição"), T(serv, "cServ/xDescServ"));
            Add(facts, w.W("Internal code of the provider", "Código interno del prestador", "Código interno do contribuinte"), T(serv, "cServ/cIntContrib"));
            var ext = XmlUbl.Child(serv, "comExt");
            if (ext != null)
            {
                Add(facts, w.W("Foreign trade: mode", "Comercio exterior: modo", "Comércio exterior: modo"), Coded(ProvisionMode, T(ext, "mdPrestacao")));
                Add(facts, w.W("Foreign trade: relationship", "Comercio exterior: vínculo", "Comércio exterior: vínculo"), Coded(Relationship, T(ext, "vincPrest")));
                Add(facts, w.W("Foreign trade: value in foreign currency", "Comercio exterior: valor en moneda extranjera", "Comércio exterior: valor em moeda estrangeira"), Join(" ", T(ext, "vServMoeda"), T(ext, "tpMoeda") is { } cur ? "(" + cur + ")" : null));
                Add(facts, w.W("Foreign trade: temporary movement of goods", "Comercio exterior: movimiento temporal de bienes", "Comércio exterior: movimentação temporária de bens"), Coded(TemporaryGoods, T(ext, "movTempBens")));
                Add(facts, w.W("Import declaration (DI)", "Declaración de importación (DI)", "Declaração de importação (DI)"), T(ext, "nDI"));
                Add(facts, w.W("Export registration (RE)", "Registro de exportación (RE)", "Registro de exportação (RE)"), T(ext, "nRE"));
            }
            var works = XmlUbl.Child(serv, "obra");
            if (works != null)
            {
                Add(facts, w.W("Works: property registration", "Obra: inscripción inmobiliaria", "Obra: inscrição imobiliária"), T(works, "inscImobFisc"));
                Add(facts, w.W("Works: identification (CNO/CEI)", "Obra: identificación (CNO/CEI)", "Obra: identificação (CNO/CEI)"), T(works, "cObra"));
                Add(facts, "CIB", T(works, "cCIB"));
            }
            var ev = XmlUbl.Child(serv, "atvEvento");
            if (ev != null)
                Add(facts, w.W("Event", "Evento", "Evento"), Join(" · ", T(ev, "xNome"), Join(" → ", T(ev, "dtIni"), T(ev, "dtFim")), T(ev, "idAtvEvt")));
            Add(facts, w.W("Technical responsibility (ART, RRT...)", "Responsabilidad técnica (ART, RRT...)", "Responsabilidade técnica (ART, RRT...)"), T(serv, "infoCompl/idDocTec"));
            Add(facts, w.W("Referenced document", "Documento referenciado", "Documento referenciado"), T(serv, "infoCompl/docRef"));
            Add(facts, w.W("Order or contract", "Pedido o contrato", "Pedido ou contrato"), T(serv, "infoCompl/xPed"));
            Add(facts, w.W("Additional information", "Información adicional", "Informações complementares"), T(serv, "infoCompl/xInfComp"));
            Add(facts, w.W("Note of the municipality", "Nota del municipio", "Uso da administração municipal"), T(nfse, "xOutInf"));
            XmlUbl.Facts(sb, facts);
        }

        private static void Values(StringBuilder sb, XElement nfse, XElement dps, Words w)
        {
            var v = XmlUbl.Child(dps, "valores");
            if (v == null && nfse == null) return;
            XmlUbl.Heading(sb, 2, w.W("Values and ISSQN", "Valores e ISSQN", "Valores e ISSQN"));
            var facts = new List<(string, string)>();
            var tm = XmlUbl.Child(v, "trib/tribMun");
            Add(facts, w.W("Value of the services", "Valor de los servicios", "Valor dos serviços"), Money(T(v, "vServPrest/vServ")));
            Add(facts, w.W("Received by the intermediary", "Recibido por el intermediario", "Recebido pelo intermediário"), Money(T(v, "vServPrest/vReceb")));
            Add(facts, w.W("Unconditional discount", "Descuento incondicional", "Desconto incondicionado"), Money(T(v, "vDescCondIncond/vDescIncond")));
            Add(facts, w.W("Conditional discount", "Descuento condicional", "Desconto condicionado"), Money(T(v, "vDescCondIncond/vDescCond")));
            Add(facts, w.W("Deduction or reduction (percent)", "Deducción o reducción (porcentaje)", "Dedução ou redução (percentual)"), Percent(T(v, "vDedRed/pDR")));
            Add(facts, w.W("Deduction or reduction (amount)", "Deducción o reducción (importe)", "Dedução ou redução (valor)"), Money(T(v, "vDedRed/vDR")));
            var documents = XmlUbl.Child(v, "vDedRed/documentos");
            if (documents != null) Add(facts, w.W("Documents of the deduction", "Documentos de la deducción", "Documentos da dedução"), documents.Elements().Count().ToString(CultureInfo.InvariantCulture));
            Add(facts, w.W("ISSQN taxation", "Tributación del ISSQN", "Tributação do ISSQN"), Coded(IssTaxation, T(tm, "tribISSQN")));
            Add(facts, w.W("Immunity", "Inmunidad", "Imunidade"), Coded(Immunity, T(tm, "tpImunidade")));
            Add(facts, w.W("Country of the result (export)", "País del resultado (exportación)", "País do resultado (exportação)"), T(tm, "cPaisResult"));
            Add(facts, w.W("Suspended payment", "Exigibilidad suspendida", "Exigibilidade suspensa"), Join(" · ", Coded(SuspendedKind, T(tm, "exigSusp/tpSusp")), T(tm, "exigSusp/nProcesso")));
            Add(facts, w.W("Municipal benefit", "Beneficio municipal", "Benefício municipal"), T(tm, "BM/nBM"));
            Add(facts, w.W("ISSQN withholding", "Retención del ISSQN", "Retenção do ISSQN"), Coded(IssWithholding, T(tm, "tpRetISSQN")));
            Add(facts, w.W("ISSQN rate (declared)", "Alícuota del ISSQN (declarada)", "Alíquota do ISSQN (declarada)"), Percent(T(tm, "pAliq")));
            var c = XmlUbl.Child(nfse, "valores");
            Add(facts, w.W("Deduction or reduction (calculated)", "Deducción o reducción (calculada)", "Dedução ou redução (calculada)"), Money(T(c, "vCalcDR")));
            Add(facts, w.W("Municipal benefit (calculated)", "Beneficio municipal (calculado)", "Benefício municipal (calculado)"), Money(T(c, "vCalcBM")));
            Add(facts, w.W("Tax base of the ISSQN", "Base de cálculo del ISSQN", "Base de cálculo do ISSQN"), Money(T(c, "vBC")));
            Add(facts, w.W("ISSQN rate applied", "Alícuota del ISSQN aplicada", "Alíquota do ISSQN aplicada"), Percent(T(c, "pAliqAplic")));
            Add(facts, "ISSQN", Money(T(c, "vISSQN")));
            Add(facts, w.W("Total withheld", "Total retenido", "Total das retenções"), Money(T(c, "vTotalRet")));
            Add(facts, w.W("Net value", "Valor líquido", "Valor líquido"), Money(T(c, "vLiq")));
            XmlUbl.Facts(sb, facts);
        }

        // The other taxes of the DPS (PIS, COFINS, withholdings, the approximate total of the taxes) and the IBS and CBS of the tax reform (what the issuer
        // declares, and what the system computes).
        private static void Taxes(StringBuilder sb, XElement nfse, XElement dps, Words w)
        {
            var fed = XmlUbl.Child(dps, "valores/trib/tribFed");
            var total = XmlUbl.Child(dps, "valores/trib/totTrib");
            var facts = new List<(string, string)>();
            Add(facts, w.W("PIS/COFINS situation (CST)", "Situación tributaria del PIS/COFINS (CST)", "Situação tributária do PIS/COFINS (CST)"), Coded(PisCofinsCst, T(fed, "piscofins/CST")));
            Add(facts, w.W("PIS/COFINS base", "Base del PIS/COFINS", "Base do PIS/COFINS"), Money(T(fed, "piscofins/vBCPisCofins")));
            Add(facts, "PIS", Join(" · ", Percent(T(fed, "piscofins/pAliqPis")), Money(T(fed, "piscofins/vPis"))));
            Add(facts, "COFINS", Join(" · ", Percent(T(fed, "piscofins/pAliqCofins")), Money(T(fed, "piscofins/vCofins"))));
            Add(facts, w.W("PIS/COFINS withholding", "Retención del PIS/COFINS", "Retenção do PIS/COFINS"), Coded(PisCofinsWithholding, T(fed, "piscofins/tpRetPisCofins")));
            Add(facts, w.W("Withheld: social security (CP)", "Retenido: seguridad social (CP)", "Retido: contribuição previdenciária (CP)"), Money(T(fed, "vRetCP")));
            Add(facts, w.W("Withheld: income tax (IRRF)", "Retenido: impuesto sobre la renta (IRRF)", "Retido: imposto de renda (IRRF)"), Money(T(fed, "vRetIRRF")));
            Add(facts, w.W("Withheld: CSLL", "Retenido: CSLL", "Retido: CSLL"), Money(T(fed, "vRetCSLL")));
            Add(facts, w.W("Approximate taxes: federal, state, municipal", "Impuestos aproximados: federal, estatal, municipal", "Tributos aproximados: federais, estaduais, municipais"),
                T(total, "vTotTrib") == null ? null : Join(" · ", Money(T(total, "vTotTrib/vTotTribFed")), Money(T(total, "vTotTrib/vTotTribEst")), Money(T(total, "vTotTrib/vTotTribMun"))));
            Add(facts, w.W("Approximate taxes (percent): federal, state, municipal", "Impuestos aproximados (porcentaje): federal, estatal, municipal", "Tributos aproximados (percentual): federais, estaduais, municipais"),
                T(total, "pTotTrib") == null ? null : Join(" · ", Percent(T(total, "pTotTrib/pTotTribFed")), Percent(T(total, "pTotTrib/pTotTribEst")), Percent(T(total, "pTotTrib/pTotTribMun"))));
            Add(facts, w.W("Approximate taxes of the Simples Nacional", "Impuestos aproximados del Simples Nacional", "Tributos aproximados do Simples Nacional"), Percent(T(total, "pTotTribSN")));
            if (facts.Count > 0)
            {
                XmlUbl.Heading(sb, 2, w.W("Other taxes", "Otros impuestos", "Outros tributos"));
                XmlUbl.Facts(sb, facts);
            }

            var declared = XmlUbl.Child(dps, "IBSCBS");
            var computed = XmlUbl.Child(nfse, "IBSCBS");
            if (declared == null && computed == null) return;
            XmlUbl.Heading(sb, 2, w.W("IBS and CBS (tax reform)", "IBS y CBS (reforma tributaria)", "IBS e CBS (reforma tributária)"));
            var ib = new List<(string, string)>();
            Add(ib, w.W("Purpose", "Finalidad", "Finalidade"), Coded(Purpose, T(declared, "finNFSe")));
            Add(ib, w.W("Personal use or consumption", "Uso o consumo personal", "Uso ou consumo pessoal"), Coded(PersonalUse, T(declared, "indFinal")));
            Add(ib, w.W("Operation indicator (cIndOp)", "Indicador de la operación (cIndOp)", "Indicador da operação (cIndOp)"), T(declared, "cIndOp"));
            Add(ib, w.W("Operation with governments or real estate", "Operación con entes públicos o inmuebles", "Operação com entes governamentais ou bens imóveis"), Coded(GovernmentOperation, T(declared, "tpOper")));
            Add(ib, w.W("Government entity", "Ente público", "Ente governamental"), Coded(GovernmentEntity, T(declared, "tpEnteGov")));
            Add(ib, w.W("Recipient", "Destinatario", "Destinatário"), Coded(RecipientIndicator, T(declared, "indDest")));
            Add(ib, w.W("Recipient: name", "Destinatario: nombre", "Destinatário: nome"), Join(" · ", T(declared, "dest/xNome"), T(declared, "dest/CNPJ") ?? T(declared, "dest/CPF") ?? T(declared, "dest/NIF")));
            Add(ib, w.W("Situation (CST) and classification of the IBS/CBS", "Situación (CST) y clasificación del IBS/CBS", "Situação (CST) e classificação do IBS/CBS"), Join(" · ", T(declared, "valores/trib/gIBSCBS/CST"), T(declared, "valores/trib/gIBSCBS/cClassTrib")));
            Add(ib, w.W("Place of incidence", "Lugar de incidencia", "Local de incidência"), Join(" · ", T(computed, "xLocalidadeIncid"), T(computed, "cLocalidadeIncid") is { } l ? "IBGE " + l : null));
            Add(ib, w.W("Tax base", "Base de cálculo", "Base de cálculo"), Money(T(computed, "valores/vBC")));
            Add(ib, w.W("IBS state: rate (effective) and value", "IBS estatal: alícuota (efectiva) y valor", "IBS estadual: alíquota (efetiva) e valor"), Join(" · ", Percent(T(computed, "valores/uf/pIBSUF")), Percent(T(computed, "valores/uf/pAliqEfetUF")), Money(T(computed, "totCIBS/gIBS/gIBSUFTot/vIBSUF"))));
            Add(ib, w.W("IBS municipal: rate (effective) and value", "IBS municipal: alícuota (efectiva) y valor", "IBS municipal: alíquota (efetiva) e valor"), Join(" · ", Percent(T(computed, "valores/mun/pIBSMun")), Percent(T(computed, "valores/mun/pAliqEfetMun")), Money(T(computed, "totCIBS/gIBS/gIBSMunTot/vIBSMun"))));
            Add(ib, w.W("CBS: rate (effective) and value", "CBS: alícuota (efectiva) y valor", "CBS: alíquota (efetiva) e valor"), Join(" · ", Percent(T(computed, "valores/fed/pCBS")), Percent(T(computed, "valores/fed/pAliqEfetCBS")), Money(T(computed, "totCIBS/gCBS/vCBS"))));
            Add(ib, w.W("Total IBS", "Total del IBS", "Total do IBS"), Money(T(computed, "totCIBS/gIBS/vIBSTot")));
            Add(ib, w.W("Total of the document with IBS and CBS", "Total del documento con IBS y CBS", "Total da nota com IBS e CBS"), Money(T(computed, "totCIBS/vTotNF")));
            XmlUbl.Facts(sb, ib);
        }

        // ---- the checks ----------------------------------------------------------------------------------------------------------------------

        private static void Checks(StringBuilder sb, XElement nfse, XElement dps, string key, Words w)
        {
            if (nfse == null) return;
            var findings = new List<string>();
            var c = XmlUbl.Child(nfse, "valores");
            var v = XmlUbl.Child(dps, "valores");
            var bc = Dec(T(c, "vBC")); var rate = Dec(T(c, "pAliqAplic")); var iss = Dec(T(c, "vISSQN"));
            if (bc != null && rate != null && iss != null && Math.Abs(Math.Round(bc.Value * rate.Value / 100m, 2, MidpointRounding.AwayFromZero) - iss.Value) > Cent)
                findings.Add(w.W($"The ISSQN is {Num(iss.Value)}; the tax base {Num(bc.Value)} times the rate {Num(rate.Value)} % is {Num(bc.Value * rate.Value / 100m)}.",
                    $"El ISSQN es {Num(iss.Value)}; la base {Num(bc.Value)} por la alícuota {Num(rate.Value)} % es {Num(bc.Value * rate.Value / 100m)}.",
                    $"O ISSQN é {Num(iss.Value)}; a base {Num(bc.Value)} vezes a alíquota {Num(rate.Value)} % é {Num(bc.Value * rate.Value / 100m)}."));

            // the total withheld is the sum of the contribution, the income tax, the CSLL and, when it is withheld, the ISSQN (the formula of vTotalRet)
            var fed = XmlUbl.Child(v, "trib/tribFed");
            var withheld = (Dec(T(fed, "vRetCP")) ?? 0) + (Dec(T(fed, "vRetIRRF")) ?? 0) + (Dec(T(fed, "vRetCSLL")) ?? 0);
            if (T(v, "trib/tribMun/tpRetISSQN") is "2" or "3") withheld += iss ?? 0;
            var total = Dec(T(c, "vTotalRet"));
            if (total != null && Math.Abs(total.Value - withheld) > Cent)
                findings.Add(w.W($"The total withheld is {Num(total.Value)}; the withholdings of the declaration add up to {Num(withheld)}.",
                    $"El total retenido es {Num(total.Value)}; las retenciones de la declaración suman {Num(withheld)}.",
                    $"O total das retenções é {Num(total.Value)}; as retenções da declaração somam {Num(withheld)}."));

            // the net value: the services, less the discounts and what is withheld (the formula of vLiq)
            var net = Dec(T(c, "vLiq")); var served = Dec(T(v, "vServPrest/vServ"));
            if (net != null && served != null)
            {
                var expected = served.Value - (Dec(T(v, "vDescCondIncond/vDescCond")) ?? 0) - (Dec(T(v, "vDescCondIncond/vDescIncond")) ?? 0) - (total ?? withheld);
                if (Math.Abs(net.Value - expected) > Cent)
                    findings.Add(w.W($"The net value is {Num(net.Value)}; the value of the services less the discounts and the withholdings is {Num(expected)}.",
                        $"El valor líquido es {Num(net.Value)}; el valor de los servicios menos los descuentos y las retenciones es {Num(expected)}.",
                        $"O valor líquido é {Num(net.Value)}; o valor dos serviços menos os descontos e as retenções é {Num(expected)}."));
            }

            // the access key repeats the number (13 digits), the federal registration of the issuer (14) and the generating environment of the document
            if (key != null)
            {
                var number = T(nfse, "nNFSe");
                if (number != null && number.All(char.IsAsciiDigit) && key.Substring(23, 13).TrimStart('0') != number)
                    findings.Add(w.W($"The access key has the number {key.Substring(23, 13).TrimStart('0')}; the document says {number}.", $"La clave de acceso tiene el número {key.Substring(23, 13).TrimStart('0')}; el documento dice {number}.", $"A chave de acesso tem o número {key.Substring(23, 13).TrimStart('0')}; o documento diz {number}."));
                var registration = T(nfse, "emit/CNPJ") ?? T(nfse, "emit/CPF");
                if (registration != null && key.Substring(9, 14).TrimStart('0') != registration.TrimStart('0'))
                    findings.Add(w.W($"The access key has the registration {key.Substring(9, 14)}; the issuer is {registration}.", $"La clave de acceso tiene la inscripción {key.Substring(9, 14)}; el emisor es {registration}.", $"A chave de acesso tem a inscrição {key.Substring(9, 14)}; o emitente é {registration}."));
                var generator = T(nfse, "ambGer");
                if (generator != null && key.Substring(7, 1) != generator)
                    findings.Add(w.W($"The access key says generating environment {key.Substring(7, 1)}; the document says {generator}.", $"La clave de acceso dice ambiente generador {key.Substring(7, 1)}; el documento dice {generator}.", $"A chave de acesso diz ambiente gerador {key.Substring(7, 1)}; o documento diz {generator}."));
            }

            XmlUbl.Heading(sb, 2, w.W("Things to look at", "Cosas que revisar", "Pontos a verificar"));
            if (findings.Count == 0)
                sb.Append(w.W("The checks below found nothing.", "Las verificaciones de abajo no encontraron nada.", "As verificações abaixo não encontraram nada.")).Append("\n\n");
            else
            {
                foreach (var f in findings) sb.Append("- ").Append(XmlUbl.Safe(f)).Append('\n');
                sb.Append('\n');
            }
            sb.Append(w.W(
                "What is checked: the ISSQN against its base and rate, the total withheld, the net value, and the access key against the number, the issuer and the generating environment, with the formulas written in the national schema. This is not the validation of the national environment: a document with nothing here can still be refused, and the schema (XSD) is checked with Edit > Check against schema.",
                "Qué se revisa: el ISSQN frente a su base y su alícuota, el total retenido, el valor líquido y la clave de acceso frente al número, el emisor y el ambiente generador, con las fórmulas escritas en el esquema nacional. No es la validación del ambiente nacional: un documento sin nada aquí aún puede ser rechazado, y el esquema (XSD) se verifica con Edición > Validar con un esquema.",
                "O que se verifica: o ISSQN face à sua base e alíquota, o total das retenções, o valor líquido e a chave de acesso face ao número, ao emitente e ao ambiente gerador, com as fórmulas escritas no esquema nacional. Não é a validação do ambiente nacional: um documento sem nada aqui pode ainda assim ser rejeitado, e o esquema (XSD) verifica-se com Editar > Validar com um esquema.")).Append("\n");
        }

        // ---- the events ---------------------------------------------------------------------------------------------------------------------

        private static bool Event(StringBuilder sb, XElement root, ConversionContext context, XmlLabels xl, Words w)
        {
            var inf = root.Name.LocalName == "evento" ? XmlUbl.Child(root, "infEvento") : null;
            var request = inf != null ? XmlUbl.Child(inf, "pedRegEvento/infPedReg") : XmlUbl.Child(root, "infPedReg");
            if (request == null) return false;
            var body = request.Elements().FirstOrDefault(e => EventNames.ContainsKey(e.Name.LocalName))
                ?? request.Elements().FirstOrDefault(e => e.Name.LocalName.Length == 7 && e.Name.LocalName[0] == 'e' && e.Name.LocalName.Skip(1).All(char.IsAsciiDigit));
            var code = body?.Name.LocalName;
            var name = code != null && EventNames.TryGetValue(code, out var n) ? n : (T(body, "xDesc") ?? w.W("Event of an NFS-e", "Evento de una NFS-e", "Evento de uma NFS-e"));
            XmlUbl.Heading(sb, 1, name);
            sb.Append("> ").Append(XmlUbl.Safe(Join(" · ", string.IsNullOrEmpty(context.SourceName) ? null : xl.File + ": " + context.SourceName,
                w.W("National NFS-e standard", "Estándar nacional de la NFS-e", "Padrão nacional da NFS-e") + (root.Attribute("versao") is { } ver ? " " + ver.Value : ""),
                root.Name.LocalName == "evento" ? w.W("Registered event", "Evento registrado", "Evento registrado") : w.W("Request for an event: not yet registered", "Solicitud de un evento: aún sin registrar", "Pedido de registro: ainda não registrado")))).Append("\n\n");

            var facts = new List<(string, string)>();
            Add(facts, w.W("Event", "Evento", "Evento"), code == null ? null : code + " — " + name);
            Add(facts, w.W("NFS-e (access key)", "NFS-e (clave de acceso)", "NFS-e (chave de acesso)"), T(request, "chNFSe"));
            Add(facts, w.W("Author", "Autor", "Autor"), T(request, "CNPJAutor") is { } cnpj ? "CNPJ " + cnpj : T(request, "CPFAutor") is { } cpf ? "CPF " + cpf : null);
            Add(facts, w.W("Requested on", "Solicitado el", "Solicitado em"), T(request, "dhEvento"));
            Add(facts, w.W("Environment", "Ambiente", "Ambiente"), Coded(Environment, T(request, "tpAmb")));
            Add(facts, w.W("Registered on", "Registrado el", "Registrado em"), T(inf, "dhProc"));
            Add(facts, w.W("Generated by", "Generado por", "Gerado por"), Coded(EventGenerator, T(inf, "ambGer")));
            Add(facts, w.W("Sequence number of the event", "Número de secuencia del evento", "Número sequencial do evento"), T(inf, "nSeqEvento"));
            Add(facts, w.W("Document number (nDFSe)", "Número del documento (nDFSe)", "Número do documento (nDFSe)"), T(inf, "nDFSe"));
            Add(facts, w.W("Application version", "Versión de la aplicación", "Versão do aplicativo"), Join(" / ", T(request, "verAplic"), T(inf, "verAplic")));
            if (body != null)
                foreach (var field in body.Elements())
                {
                    var field_ = field.Name.LocalName;
                    if (field_ == "xDesc" || string.IsNullOrWhiteSpace(field.Value)) continue;
                    var value = field.Value.Trim();
                    switch (field_)
                    {
                        case "cMotivo":
                            Add(facts, w.W("Reason", "Motivo", "Motivo"), Coded(ReasonTable(code), value));
                            break;
                        case "xMotivo":
                            Add(facts, w.W("Explanation", "Explicación", "Explicação"), value);
                            break;
                        case "chSubstituta":
                            Add(facts, w.W("Substitute NFS-e (access key)", "NFS-e sustituta (clave de acceso)", "NFS-e substituta (chave de acesso)"), value);
                            break;
                        case "CPFAgTrib":
                            Add(facts, w.W("Tax agent (CPF)", "Agente tributario (CPF)", "Agente tributário (CPF)"), value);
                            break;
                        case "nProcAdm":
                            Add(facts, w.W("Administrative process", "Proceso administrativo", "Processo administrativo"), value);
                            break;
                        default:
                            Add(facts, field_, value);
                            break;
                    }
                }
            if (root.Descendants().Any(e => e.Name.LocalName == "Signature")) Add(facts, w.W("Signature", "Firma", "Assinatura"), xl.SignaturePresent);
            XmlUbl.Facts(sb, facts);
            return true;
        }

        // The table of the reasons (cMotivo) that goes with each kind of event.
        private static IReadOnlyDictionary<string, string> ReasonTable(string code) => code switch
        {
            "e101101" or "e101103" => CancellationReason,
            "e105102" => SubstitutionReason,
            "e105105" => AnalysisRefusedReason,
            "e105104" => new Dictionary<string, string> { ["1"] = "Cancelamento de NFS-e Deferido" },
            "e202205" or "e203206" or "e204207" => RejectionReason,
            _ => new Dictionary<string, string>(),
        };

        private static readonly Dictionary<string, string> EventNames = new()
        {
            ["e101101"] = "Cancelamento de NFS-e",
            ["e105102"] = "Cancelamento de NFS-e por Substituição",
            ["e101103"] = "Solicitação de Análise Fiscal para Cancelamento de NFS-e",
            ["e105104"] = "Cancelamento de NFS-e Deferido por Análise Fiscal",
            ["e105105"] = "Cancelamento de NFS-e Indeferido por Análise Fiscal",
            ["e202201"] = "Manifestação de NFS-e - Confirmação do Prestador",
            ["e203202"] = "Manifestação de NFS-e - Confirmação do Tomador",
            ["e204203"] = "Manifestação de NFS-e - Confirmação do Intermediário",
            ["e205204"] = "Manifestação de NFS-e - Confirmação Tácita",
            ["e202205"] = "Manifestação de NFS-e - Rejeição do Prestador",
            ["e203206"] = "Manifestação de NFS-e - Rejeição do Tomador",
            ["e204207"] = "Manifestação de NFS-e - Rejeição do Intermediário",
            ["e205208"] = "Manifestação de NFS-e - Anulação da Rejeição",
            ["e305101"] = "Cancelamento de NFS-e por Ofício",
            ["e305102"] = "Bloqueio de NFS-e por Ofício",
            ["e305103"] = "Desbloqueio de NFS-e por Ofício",
        };

        // ---- small helpers ---------------------------------------------------------------------------------------------------------------------

        private static string T(XElement parent, string path) => parent == null ? null : XmlUbl.Text(parent, path) is { Length: > 0 } text ? text : null;

        private static decimal? Dec(string text) =>
            text != null && decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;

        private static string Num(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        // Amounts as the file has them, with the currency sign of the schema (R$) and the rates with a percent sign.
        private static string Money(string value) => string.IsNullOrWhiteSpace(value) ? null : "R$ " + value.Trim();

        private static string Percent(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim() + " %";

        private static void Add(List<(string, string)> facts, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) facts.Add((label, value));
        }

        private static string Join(string separator, params string[] parts) => string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        // "1 — Operação tributável": the code and the name the schema gives it; the code alone when it is not in the list.
        private static string Coded(IReadOnlyDictionary<string, string> table, string code) =>
            code == null ? null : table.TryGetValue(code, out var name) ? code + " — " + name : code;

        private sealed class Words
        {
            private readonly int language;
            public Words(int language) => this.language = language;
            public string W(string en, string es, string pt) => language == 2 ? pt : language == 1 ? es : en;
        }

        // ---- the codes of the schema (NFSe-ESQUEMAS_XSD v1.01, 2026-02-09): generated from the documentation of its simple types -------------

        // TSTipoAmbiente
        private static readonly Dictionary<string, string> Environment = new()
        {
            ["1"] = "Produção",
            ["2"] = "Homologação",
        };

        // TSEmitenteDPS
        private static readonly Dictionary<string, string> IssuerKind = new()
        {
            ["1"] = "Prestador",
            ["2"] = "Tomador",
            ["3"] = "Intermediário",
        };

        // TSMotivoEmisTI
        private static readonly Dictionary<string, string> ReasonByTaker = new()
        {
            ["1"] = "Importação de Serviço",
            ["2"] = "Tomador/Intermediário obrigado a emitir NFS-e por legislação municipal",
            ["3"] = "Tomador/Intermediário emitindo NFS-e por recusa de emissão pelo prestador",
            ["4"] = "Tomador/Intermediário emitindo por rejeitar a NFS-e emitida pelo prestador",
        };

        // TSOpSimpNac
        private static readonly Dictionary<string, string> SimplesStatus = new()
        {
            ["1"] = "Não Optante",
            ["2"] = "Optante - Microempreendedor Individual (MEI)",
            ["3"] = "Optante - Microempresa ou Empresa de Pequeno Porte (ME/EPP)",
        };

        // TSRegimeApuracaoSimpNac
        private static readonly Dictionary<string, string> SimplesRegime = new()
        {
            ["1"] = "Regime de apuração dos tributos federais e municipal pelo SN",
            ["2"] = "Regime de apuração dos tributos federais pelo SN e ISSQN por fora do SN conforme respectiva legislação municipal do tributo",
            ["3"] = "Regime de apuração dos tributos federais e municipal por fora do SN conforme respectivas legislações federal e municipal de cada tributo",
        };

        // TSRegEspTrib
        private static readonly Dictionary<string, string> SpecialRegime = new()
        {
            ["0"] = "Nenhum",
            ["1"] = "Ato Cooperado (Cooperativa)",
            ["2"] = "Estimativa",
            ["3"] = "Microempresa Municipal",
            ["4"] = "Notário ou Registrador",
            ["5"] = "Profissional Autônomo",
            ["6"] = "Sociedade de Profissionais",
            ["9"] = "Outros",
        };

        // TSTribISSQN
        private static readonly Dictionary<string, string> IssTaxation = new()
        {
            ["1"] = "Operação tributável",
            ["2"] = "Imunidade",
            ["3"] = "Exportação de serviço",
            ["4"] = "Não Incidência",
        };

        // TSTipoImunidadeISSQN
        private static readonly Dictionary<string, string> Immunity = new()
        {
            ["0"] = "Imunidade (tipo não informado na nota de origem)",
            ["1"] = "Patrimônio, renda ou serviços, uns dos outros (CF88, Art 150, VI, a)",
            ["2"] = "Templos de qualquer culto (CF88, Art 150, VI, b)",
            ["3"] = "Patrimônio, renda ou serviços dos partidos políticos, inclusive suas fundações, das entidades sindicais dos trabalhadores, das instituições de educação e de assistência social, sem fins lucrativos, atendidos os requisitos da lei (CF88, Art 150, VI, c)",
            ["4"] = "Livros, jornais, periódicos e o papel destinado a sua impressão (CF88, Art 150, VI, d)",
            ["5"] = "Fonogramas e videofonogramas musicais produzidos no Brasil contendo obras musicais ou literomusicais de autores brasileiros e/ou obras em geral interpretadas por artistas brasileiros bem como os suportes materiais ou arquivos digitais que os contenham, salvo na etapa de replicação industrial de mídias ópticas de leitura a laser. (CF88, Art 150, VI, e)",
        };

        // TSTipoRetISSQN
        private static readonly Dictionary<string, string> IssWithholding = new()
        {
            ["1"] = "Não Retido",
            ["2"] = "Retido pelo Tomador",
            ["3"] = "Retido pelo Intermediario",
        };

        // TSOpExigSuspensa
        private static readonly Dictionary<string, string> SuspendedKind = new()
        {
            ["1"] = "Exigibilidade Suspensa por Decisão Judicial",
            ["2"] = "Exigibilidade Suspensa por Processo Administrativo",
        };

        // TSModoPrestacao
        private static readonly Dictionary<string, string> ProvisionMode = new()
        {
            ["0"] = "Desconhecido (tipo não informado na nota de origem)",
            ["1"] = "Transfronteiriço",
            ["2"] = "Consumo no Brasil",
            ["3"] = "Presença Comercial no Exterior",
            ["4"] = "Movimento Temporário de Pessoas Físicas",
        };

        // TSVincPrest
        private static readonly Dictionary<string, string> Relationship = new()
        {
            ["0"] = "Sem vínculo com o Tomador/Prestador",
            ["1"] = "Controlada",
            ["2"] = "Controladora",
            ["3"] = "Coligada",
            ["4"] = "Matriz",
            ["5"] = "Filial ou sucursal",
            ["6"] = "Outro vínculo",
            ["9"] = "Desconhecido (tipo não informado na nota de origem)",
        };

        // TSCodJustSubst
        private static readonly Dictionary<string, string> SubstitutionReason = new()
        {
            ["01"] = "Desenquadramento de NFS-e do Simples Nacional",
            ["02"] = "Enquadramento de NFS-e no Simples Nacional",
            ["03"] = "Inclusão Retroativa de Imunidade/Isenção para NFS-e",
            ["04"] = "Exclusão Retroativa de Imunidade/Isenção para NFS-e",
            ["05"] = "Rejeição de NFS-e pelo tomador ou pelo intermediário se responsável pelo recolhimento do tributo",
            ["99"] = "Outros",
        };

        // TSCodJustCanc
        private static readonly Dictionary<string, string> CancellationReason = new()
        {
            ["1"] = "Erro na Emissão",
            ["2"] = "Serviço não Prestado",
            ["9"] = "Outros",
        };

        // TSCodMotivoRejeicao
        private static readonly Dictionary<string, string> RejectionReason = new()
        {
            ["1"] = "NFS-e em duplicidade",
            ["2"] = "NFS-e já emitida pelo tomador",
            ["3"] = "Não ocorrência do fato gerador",
            ["4"] = "Erro quanto a responsabilidade tributária",
            ["5"] = "Erro quanto ao valor do serviço, valor das deduções ou serviço prestado ou data do fato gerador",
            ["9"] = "Outros",
        };

        // TSTipoCST
        private static readonly Dictionary<string, string> PisCofinsCst = new()
        {
            ["00"] = "Nenhum",
            ["01"] = "Operação Tributável com Alíquota Básica",
            ["02"] = "Operação Tributável com Alíquota Diferenciada",
            ["03"] = "Operação Tributável com Alíquota por Unidade de Medida de Produto",
            ["04"] = "Operação Tributável monofásica - Revenda a Alíquota Zero",
            ["05"] = "Operação Tributável por Substituição Tributária",
            ["06"] = "Operação Tributável a Alíquota Zero",
            ["07"] = "Operação Isenta da Contribuição",
            ["08"] = "Operação sem Incidência da Contribuição",
            ["09"] = "Operação com Suspensão da Contribuição",
            ["49"] = "Outras Operações de Saída",
            ["50"] = "Operação com Direito a Crédito – Vinculada Exclusivamente a Receita Tributada no Mercado Interno",
            ["51"] = "Operação com Direito a Crédito – Vinculada Exclusivamente a Receita Não-Tributada no Mercado Interno",
            ["52"] = "Operação com Direito a Crédito – Vinculada Exclusivamente a Receita de Exportação",
            ["53"] = "Operação com Direito a Crédito – Vinculada a Receitas Tributadas e Não-Tributadas no Mercado Interno",
            ["54"] = "Operação com Direito a Crédito – Vinculada a Receitas Tributadas no Mercado Interno e de Exportação",
            ["55"] = "Operação com Direito a Crédito – Vinculada a Receitas Não Tributadas no Mercado Interno e de Exportação",
            ["56"] = "Operação com Direito a Crédito – Vinculada a Receitas Tributadas e Não-Tributadas no Mercado Interno e de Exportação",
            ["60"] = "Crédito Presumido – Operação de Aquisição Vinculada Exclusivamente a Receita Tributada no Mercado Interno",
            ["61"] = "Crédito Presumido – Operação de Aquisição Vinculada Exclusivamente a Receita Não-Tributada no Mercado Interno",
            ["62"] = "Crédito Presumido – Operação de Aquisição Vinculada Exclusivamente a Receita de Exportação",
            ["63"] = "Crédito Presumido – Operação de Aquisição Vinculada a Receitas Tributadas e Não-Tributadas no Mercado Interno",
            ["64"] = "Crédito Presumido – Operação de Aquisição Vinculada a Receitas Tributadas no Mercado Interno e de Exportação",
            ["65"] = "Crédito Presumido – Operação de Aquisição Vinculada a Receitas Não-Tributadas no Mercado Interno e de Exportação",
            ["66"] = "Crédito Presumido – Operação de Aquisição Vinculada a Receitas Tributadas e Não-Tributadas no Mercado Interno e de Exportação",
            ["67"] = "Crédito Presumido – Outras Operações",
            ["70"] = "Operação de Aquisição sem Direito a Crédito",
            ["71"] = "Operação de Aquisição com Isenção",
            ["72"] = "Operação de Aquisição com Suspensão",
            ["73"] = "Operação de Aquisição a Alíquota Zero",
            ["74"] = "Operação de Aquisição sem Incidência da Contribuição",
            ["75"] = "Operação de Aquisição por Substituição Tributária",
            ["98"] = "Outras Operações de Entrada",
            ["99"] = "Outras Operações",
        };

        // TSTipoRetPISCofins
        private static readonly Dictionary<string, string> PisCofinsWithholding = new()
        {
            ["0"] = "PIS/COFINS/CSLL Não Retidos",
            ["1"] = "PIS/COFINS Retidos",
            ["2"] = "PIS/COFINS Não Retidos",
            ["3"] = "PIS/COFINS/CSLL Retidos",
            ["4"] = "PIS/COFINS Retidos, CSLL Não Retido",
            ["5"] = "PIS Retido, COFINS/CSLL Não Retido",
            ["6"] = "COFINS Retido, PIS/CSLL Não Retido",
            ["7"] = "PIS Não Retido, COFINS/CSLL Retidos",
            ["8"] = "PIS/COFINS Não Retidos, CSLL Retido",
            ["9"] = "COFINS Não Retido, PIS/CSLL Retidos",
        };

        // TStat
        private static readonly Dictionary<string, string> Status = new()
        {
            ["100"] = "NFS-e Gerada",
            ["102"] = "NFS-e de Decisão Judicial",
            ["103"] = "NFS-e Avulsa",
            ["107"] = "NFS-e MEI",
        };

        // TSAmbGeradorNFSe
        private static readonly Dictionary<string, string> Generator = new()
        {
            ["1"] = "Prefeitura",
            ["2"] = "Sistema Nacional da NFS-e",
        };

        // TSTipoEmissao
        private static readonly Dictionary<string, string> EmissionKind = new()
        {
            ["1"] = "Emissão normal no modelo da NFS-e Nacional",
            ["2"] = "Emissão original em leiaute próprio do município com transcrição para o modelo da NFS-e Nacional",
        };

        // TSProcEmissao
        private static readonly Dictionary<string, string> EmissionProcess = new()
        {
            ["1"] = "Emissão com aplicativo do contribuinte (via Web Service)",
            ["2"] = "Emissão com aplicativo disponibilizado pelo fisco (Web)",
            ["3"] = "Emissão com aplicativo disponibilizado pelo fisco (App)",
        };

        // TSAmbGeradorEvt
        private static readonly Dictionary<string, string> EventGenerator = new()
        {
            ["1"] = "Prefeitura",
            ["2"] = "Sefin Nacional",
            ["3"] = "Ambiente Nacional",
        };

        // TSIdeDedRed
        private static readonly Dictionary<string, string> DeductionKind = new()
        {
            ["1"] = "Alimentação e bebidas/frigobar",
            ["2"] = "Materiais",
            ["3"] = "Produção Externa",
            ["4"] = "Reembolso de despesas",
            ["5"] = "Repasse consorciado",
            ["6"] = "Repasse plano de saúde",
            ["7"] = "Serviços",
            ["8"] = "Subempreitada de mão de obra",
            ["9"] = "Profissional parceiro",
            ["99"] = "Outras deduções",
        };

        // TSRTCTpOper
        private static readonly Dictionary<string, string> GovernmentOperation = new()
        {
            ["1"] = "Fornecimento com pagamento posterior",
            ["2"] = "Recebimento do pagamento com fornecimento já realizado",
            ["3"] = "Fornecimento com pagamento já realizado",
            ["4"] = "Recebimento do pagamento com fornecimento posterior",
            ["5"] = "Fornecimento e recebimento do pagamento concomitantes",
        };

        // TSRTCTpEnteGov
        private static readonly Dictionary<string, string> GovernmentEntity = new()
        {
            ["1"] = "União",
            ["2"] = "Estado",
            ["3"] = "Distrito Federal",
            ["4"] = "Município",
        };

        // TSRTCIndDest
        private static readonly Dictionary<string, string> RecipientIndicator = new()
        {
            ["0"] = "o destinatário é o próprio tomador/adquirente identificado na NFS-e (tomador = adquirente = destinatário)",
            ["1"] = "o destinatário não é o próprio adquirente, podendo ser outra pessoa, física ou jurídica (ou equiparada), ou um estabelecimento diferente do indicado como tomador (tomador = adquirente ≠ destinatário)",
        };

        // TSRTCIndFinal
        private static readonly Dictionary<string, string> PersonalUse = new()
        {
            ["0"] = "Não",
            ["1"] = "Sim",
        };

        // TSRTCFinNFSe
        private static readonly Dictionary<string, string> Purpose = new()
        {
            ["0"] = "NFS-e regular",
        };

        // TSCodNaoNIF
        private static readonly Dictionary<string, string> NoNif = new()
        {
            ["0"] = "Não informado na nota de origem",
            ["1"] = "Dispensado do NIF",
            ["2"] = "Não exigência do NIF",
        };

        // TSMovTempBens
        private static readonly Dictionary<string, string> TemporaryGoods = new()
        {
            ["0"] = "Desconhecido (tipo não informado na nota de origem)",
            ["1"] = "Não",
            ["2"] = "Vinculada - Declaração de Importação",
            ["3"] = "Vinculada - Declaração de Exportação",
        };

        // TSCodJustAnaliseFiscalCancIndef
        private static readonly Dictionary<string, string> AnalysisRefusedReason = new()
        {
            ["1"] = "Cancelamento Extemporâneo Indeferido",
            ["2"] = "Cancelamento Extemporâneo Indeferido Sem Análise de Mérito",
        };

    }
}
